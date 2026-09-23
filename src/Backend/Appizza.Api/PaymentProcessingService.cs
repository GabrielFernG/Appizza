using Appizza.Modules.Payments;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Payments.Application;

/// <summary>Bridges provider results to the certified A2 lifecycle.</summary>
public sealed class PaymentProcessingService(AppizzaDbContext db, IPaymentProvider provider, PaymentAttemptLifecycleService lifecycle)
{
    public async Task<PaymentProviderStatus> StartAsync(Guid tenant, Guid attemptId, CancellationToken ct = default)
    {
        var attempt = await db.Set<PaymentAttempt>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == attemptId && x.EstablishmentId == tenant, ct)
            ?? throw new InvalidOperationException("PAYMENT_ATTEMPT_NOT_FOUND");
        if (attempt.Method == PaymentMethod.Cash) throw new InvalidOperationException("CASH_DOES_NOT_USE_PROVIDER");
        var execution = await GetOrCreateExecutionAsync(tenant, attempt, ct);
        if (execution.Status is PaymentProviderExecutionStatus.Completed or PaymentProviderExecutionStatus.TerminalFailure)
            return new PaymentProviderStatus(execution.NormalizedOutcome ?? "unknown", execution.ProviderReference);
        if (execution.Status == PaymentProviderExecutionStatus.Unknown)
            return new PaymentProviderStatus("unknown", execution.ProviderReference);

        var claimant = Guid.NewGuid();
        var claimed = await ClaimAsync(execution.Id, claimant, ct);
        if (!claimed)
        {
            var current = await db.PaymentProviderExecutions.AsNoTracking().SingleAsync(x => x.Id == execution.Id, ct);
            return new PaymentProviderStatus(current.NormalizedOutcome ?? "processing", current.ProviderReference);
        }

        var result = await provider.StartPaymentAsync(new StartPaymentRequest(attempt.Id, attempt.Amount, attempt.Method, execution.ProviderIdempotencyKey), ct);
        await using (var outcomeTx = await db.Database.BeginTransactionAsync(ct))
        {
            var current = await db.PaymentProviderExecutions.SingleOrDefaultAsync(x => x.Id == execution.Id && x.EstablishmentId == tenant && x.ClaimedBy == claimant && x.ClaimedUntil > DateTimeOffset.UtcNow, ct)
                ?? throw new InvalidOperationException("PAYMENT_EXECUTION_CLAIM_LOST");
            current.ProviderReference = result.ProviderReference;
            current.NormalizedOutcome = result.Status.Trim().ToLowerInvariant();
            current.Status = result.Status.Trim().ToLowerInvariant() switch
            {
                "approved" => PaymentProviderExecutionStatus.OutcomeObserved,
                "declined" => PaymentProviderExecutionStatus.OutcomeObserved,
                "unknown" => PaymentProviderExecutionStatus.Unknown,
                "pending_customer_action" => PaymentProviderExecutionStatus.AwaitingCustomerAction,
                _ => PaymentProviderExecutionStatus.Processing
            };
            current.ReconciliationRequired = current.Status == PaymentProviderExecutionStatus.Unknown;
            current.ClaimedBy = null;
            current.ClaimedUntil = null;
            current.UpdatedAt = DateTimeOffset.UtcNow;
            var attemptRow = await db.PaymentAttempts.SingleAsync(x => x.Id == attemptId && x.EstablishmentId == tenant, ct);
            attemptRow.Provider = provider.GetType().Name;
            attemptRow.ProviderReference = result.ProviderReference;
            await db.SaveChangesAsync(ct);
            await outcomeTx.CommitAsync(ct);
        }
        return result;
    }

    public async Task ApplyResultAsync(Guid tenant, Guid attemptId, PaymentProviderStatus result, CancellationToken ct = default)
    {
        var status = result.Status.Trim().ToLowerInvariant();
        await (status switch
        {
            "pending_customer_action" => lifecycle.MarkAwaitingCustomerActionAsync(tenant, attemptId, ct),
            "processing" => lifecycle.MarkProcessingAsync(tenant, attemptId, ct),
            "approved" => lifecycle.SucceedAsync(tenant, attemptId, ct),
            "declined" => lifecycle.FailAsync(tenant, attemptId, ct),
            "unknown" => lifecycle.MarkUnknownAsync(tenant, attemptId, ct),
            _ => Task.CompletedTask
        });
        if (status is "approved" or "declined")
        {
            var execution = await db.PaymentProviderExecutions.SingleOrDefaultAsync(x => x.EstablishmentId == tenant && x.PaymentAttemptId == attemptId, ct);
            if (execution is not null) { execution.Status = status == "approved" ? PaymentProviderExecutionStatus.Completed : PaymentProviderExecutionStatus.TerminalFailure; execution.LifecycleApplied = true; execution.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); }
        }
    }

    private async Task<PaymentProviderExecution> GetOrCreateExecutionAsync(Guid tenant, PaymentAttempt attempt, CancellationToken ct)
    {
        var existing = await db.PaymentProviderExecutions.SingleOrDefaultAsync(x => x.EstablishmentId == tenant && x.PaymentAttemptId == attempt.Id, ct);
        if (existing is not null) return existing;
        var created = new PaymentProviderExecution { Id = Guid.NewGuid(), EstablishmentId = tenant, PaymentAttemptId = attempt.Id, Provider = provider.GetType().Name, ProviderIdempotencyKey = $"attempt:{attempt.Id:N}", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Add(created);
        try { await db.SaveChangesAsync(ct); return created; }
        catch (DbUpdateException)
        {
            db.Entry(created).State = EntityState.Detached;
            return await db.PaymentProviderExecutions.SingleAsync(x => x.EstablishmentId == tenant && x.PaymentAttemptId == attempt.Id, ct);
        }
    }

    private async Task<bool> ClaimAsync(Guid executionId, Guid claimant, CancellationToken ct)
    {
        var until = DateTimeOffset.UtcNow.AddMinutes(2);
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"update payments.payment_provider_execution set claimed_by = {claimant}, claimed_until = {until}, status = 'processing' where id = {executionId} and (claimed_until is null or claimed_until < now()) and status in ('pending','processing')", ct);
        return changed == 1;
    }
}
