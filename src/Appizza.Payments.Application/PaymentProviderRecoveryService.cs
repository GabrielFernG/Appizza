using Appizza.Modules.Payments;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Payments.Application;

/// <summary>Processes durable provider executions without HTTP concerns.</summary>
public sealed class PaymentProviderRecoveryService(
    AppizzaDbContext db,
    IPaymentProvider provider,
    PaymentProcessingService processing)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    public async Task<int> ProcessBatchAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var candidates = await db.PaymentProviderExecutions.AsNoTracking()
            .Where(x => x.Status == PaymentProviderExecutionStatus.Pending ||
                        x.Status == PaymentProviderExecutionStatus.Unknown ||
                        x.Status == PaymentProviderExecutionStatus.OutcomeObserved ||
                        (x.Status == PaymentProviderExecutionStatus.Processing &&
                         (x.ClaimedUntil == null || x.ClaimedUntil <= DateTimeOffset.UtcNow)))
            .OrderBy(x => x.UpdatedAt)
            .Take(batchSize)
            .Select(x => new { x.EstablishmentId, x.PaymentAttemptId })
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ProcessOneAsync(candidate.EstablishmentId, candidate.PaymentAttemptId, cancellationToken))
                processed++;
        }
        return processed;
    }

    public async Task<bool> ProcessOneAsync(Guid establishmentId, Guid attemptId, CancellationToken cancellationToken = default)
    {
        var execution = await db.PaymentProviderExecutions.SingleOrDefaultAsync(
            x => x.EstablishmentId == establishmentId && x.PaymentAttemptId == attemptId, cancellationToken);
        if (execution is null || execution.Status is PaymentProviderExecutionStatus.Completed or PaymentProviderExecutionStatus.TerminalFailure)
            return false;

        if (execution.Status == PaymentProviderExecutionStatus.OutcomeObserved)
        {
            await processing.ApplyResultAsync(establishmentId, attemptId,
                new PaymentProviderStatus(execution.NormalizedOutcome ?? "processing", execution.ProviderReference), cancellationToken);
            return true;
        }

        // The claim transition is intentionally persisted as Processing, but
        // provider-operation routing must retain the durable provenance that
        // existed before claiming. In particular, Unknown must reconcile via
        // Lookup and must never be converted into a blind Start.
        var statusBeforeClaim = execution.Status;

        var claimant = Guid.NewGuid();
        var leaseUntil = DateTimeOffset.UtcNow.Add(Lease);
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            update payments.payment_provider_execution
               set claimed_by = {claimant}, claimed_until = {leaseUntil}, status = 'processing'
             where id = {execution.Id}
               and establishment_id = {establishmentId}
               and (claimed_until is null or claimed_until <= now())
               and status in ('pending','processing','unknown','awaiting_customer_action')
            """, cancellationToken);
        if (claimed != 1) return false;
        await db.Entry(execution).ReloadAsync(cancellationToken);

        var attemptRow = await db.PaymentAttempts.AsNoTracking().SingleAsync(x => x.Id == attemptId && x.EstablishmentId == establishmentId, cancellationToken);
        PaymentProviderStatus result;
        try
        {
            if (statusBeforeClaim == PaymentProviderExecutionStatus.Unknown || statusBeforeClaim == PaymentProviderExecutionStatus.AwaitingCustomerAction)
            {
                var lookup = await provider.LookupPaymentAsync(new PaymentProviderLookupRequest(attemptRow.Id, execution.ProviderIdempotencyKey, execution.ProviderReference, attemptRow.Method, attemptRow.Amount), cancellationToken);
                result = new PaymentProviderStatus(lookup.Status, lookup.ProviderReference);
            }
            else
            {
                result = await provider.StartPaymentAsync(new StartPaymentRequest(attemptId, attemptRow.Amount, attemptRow.Method, execution.ProviderIdempotencyKey), cancellationToken);
            }
        }
        catch (PaymentProviderOperationException exception) when (exception.Classification == PaymentRecoveryClassification.SafeToRetry)
        {
            await db.Entry(execution).ReloadAsync(cancellationToken);
            var retryable = execution;
            retryable.Status = PaymentProviderExecutionStatus.Pending;
            retryable.LastErrorClassification = exception.Classification.ToString();
            retryable.ClaimedBy = null;
            retryable.ClaimedUntil = null;
            retryable.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (PaymentProviderOperationException exception) when (exception.Classification == PaymentRecoveryClassification.AmbiguousRequiresReconciliation)
        {
            await db.Entry(execution).ReloadAsync(cancellationToken);
            var ambiguous = execution;
            ambiguous.NormalizedOutcome = "unknown";
            ambiguous.Status = PaymentProviderExecutionStatus.Unknown;
            ambiguous.ReconciliationRequired = true;
            ambiguous.LastErrorClassification = exception.Classification.ToString();
            ambiguous.ClaimedBy = null;
            ambiguous.ClaimedUntil = null;
            ambiguous.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await processing.ApplyResultAsync(establishmentId, attemptId, new PaymentProviderStatus("unknown", ambiguous.ProviderReference), cancellationToken);
            return true;
        }

        var current = await db.PaymentProviderExecutions.SingleAsync(x => x.Id == execution.Id && x.EstablishmentId == establishmentId && x.ClaimedBy == claimant && x.ClaimedUntil > DateTimeOffset.UtcNow, cancellationToken);
        current.NormalizedOutcome = result.Status.Trim().ToLowerInvariant();
        current.ProviderReference = result.ProviderReference ?? current.ProviderReference;
        current.Status = current.NormalizedOutcome switch
        {
            "approved" or "declined" => PaymentProviderExecutionStatus.OutcomeObserved,
            "unknown" => PaymentProviderExecutionStatus.Unknown,
            "pending_customer_action" => PaymentProviderExecutionStatus.AwaitingCustomerAction,
            _ => PaymentProviderExecutionStatus.Processing
        };
        current.ReconciliationRequired = current.Status == PaymentProviderExecutionStatus.Unknown;
        current.ClaimedBy = null;
        current.ClaimedUntil = null;
        current.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await processing.ApplyResultAsync(establishmentId, attemptId, result, cancellationToken);
        return true;
    }
}
