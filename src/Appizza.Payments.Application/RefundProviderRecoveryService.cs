using System.Text.Json;
using Appizza.Modules.Auditing;
using Appizza.Modules.Payments;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Payments.Application;

public sealed class RefundProviderRecoveryService(AppizzaDbContext db, IPaymentProvider provider)
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    public async Task<int> ProcessBatchAsync(int batchSize, CancellationToken ct = default)
    {
        var ids = await db.RefundProviderExecutions.AsNoTracking().Where(x =>
            x.Status == RefundProviderExecutionStatus.Pending ||
            x.Status == RefundProviderExecutionStatus.Unknown ||
            (x.Status == RefundProviderExecutionStatus.Processing && (x.ClaimedUntil == null || x.ClaimedUntil <= DateTimeOffset.UtcNow)))
            .OrderBy(x => x.UpdatedAt).Take(batchSize).Select(x => new { x.EstablishmentId, x.RefundId }).ToListAsync(ct);
        var processed = 0;
        foreach (var id in ids) if (await ProcessOneAsync(id.EstablishmentId, id.RefundId, ct)) processed++;
        return processed;
    }

    public async Task<bool> ProcessOneAsync(Guid establishmentId, Guid refundId, CancellationToken ct = default)
    {
        var execution = await db.RefundProviderExecutions.SingleOrDefaultAsync(x => x.EstablishmentId == establishmentId && x.RefundId == refundId, ct);
        if (execution is null) return false;
        if (execution.Status is RefundProviderExecutionStatus.Completed or RefundProviderExecutionStatus.TerminalFailure)
        {
            if (!execution.LifecycleApplied) await ConvergeAsync(establishmentId, refundId, execution.Status, ct);
            return !execution.LifecycleApplied;
        }
        if (execution.Status == RefundProviderExecutionStatus.OutcomeObserved)
        {
            await ConvergeAsync(establishmentId, refundId, execution.NormalizedOutcome switch
            {
                "completed" or "approved" => RefundProviderExecutionStatus.Completed,
                "failed" or "declined" => RefundProviderExecutionStatus.TerminalFailure,
                _ => RefundProviderExecutionStatus.Unknown
            }, ct);
            return true;
        }
        var prior = execution.Status;
        var claimant = Guid.NewGuid(); var until = DateTimeOffset.UtcNow.Add(Lease);
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            update payments.refund_provider_execution set claimed_by = {claimant}, claimed_until = {until}, status = 'processing', attempt_count = attempt_count + 1
            where id = {execution.Id} and establishment_id = {establishmentId}
              and (claimed_until is null or claimed_until <= now())
              and status in ('pending','processing','unknown')
            """, ct);
        if (claimed != 1) return false;
        await db.Entry(execution).ReloadAsync(ct);
        var refund = await db.Refunds.AsNoTracking().SingleAsync(x => x.Id == refundId && x.EstablishmentId == establishmentId, ct);
        var attempt = await db.PaymentAttempts.AsNoTracking().SingleAsync(x => x.Id == refund.PaymentAttemptId && x.EstablishmentId == establishmentId, ct);
        RefundProviderResult result;
        if (prior == RefundProviderExecutionStatus.Unknown)
            result = await provider.LookupRefundAsync(new RefundProviderLookupRequest(refund.Id, execution.ProviderIdempotencyKey, execution.ProviderReference, attempt.Method, refund.Amount), ct);
        else
            result = await provider.RefundAsync(new RefundProviderRequest(refund.Id, execution.ProviderIdempotencyKey, attempt.Method, refund.Amount, execution.ProviderReference), ct);
        await db.Entry(execution).ReloadAsync(ct);
        if (execution.ClaimedBy != claimant || execution.ClaimedUntil <= DateTimeOffset.UtcNow) return false;
        var normalized = result.Status.Trim().ToLowerInvariant();
        execution.NormalizedOutcome = normalized;
        execution.ProviderReference = result.ProviderReference ?? execution.ProviderReference;
        execution.ClaimedBy = null; execution.ClaimedUntil = null; execution.UpdatedAt = DateTimeOffset.UtcNow;
        execution.Status = normalized switch { "completed" or "approved" => RefundProviderExecutionStatus.Completed, "failed" or "declined" => RefundProviderExecutionStatus.TerminalFailure, "unknown" => RefundProviderExecutionStatus.Unknown, _ => RefundProviderExecutionStatus.Processing };
        execution.ReconciliationRequired = execution.Status == RefundProviderExecutionStatus.Unknown;
        await db.SaveChangesAsync(ct);
        await ConvergeAsync(establishmentId, refundId, execution.Status, ct);
        return true;
    }

    private async Task ConvergeAsync(Guid tenant, Guid refundId, RefundProviderExecutionStatus executionStatus, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var refund = await db.Refunds.SingleAsync(x => x.Id == refundId && x.EstablishmentId == tenant, ct);
        var now = DateTimeOffset.UtcNow;
        var target = executionStatus switch { RefundProviderExecutionStatus.Completed => RefundStatus.Completed, RefundProviderExecutionStatus.TerminalFailure => RefundStatus.Failed, RefundProviderExecutionStatus.Unknown => RefundStatus.Processing, _ => RefundStatus.Processing };
        if (refund.Status != target)
        {
            refund.Status = target; refund.UpdatedAt = now;
            var action = target switch { RefundStatus.Completed => "refund.completed", RefundStatus.Failed => "refund.failed", _ => "refund.reconciliation_required" };
            var eventType = target switch { RefundStatus.Completed => "refund-completed.v1", RefundStatus.Failed => "refund-failed.v1", _ => "refund-reconciliation-required.v1" };
            db.AuditEntries.Add(new AuditEntry { Id = Guid.NewGuid(), EstablishmentId = tenant, Action = action, AggregateType = "refund", AggregateId = refundId, OccurredAt = now });
            db.OutboxMessages.Add(new OutboxMessage { Id = Guid.NewGuid(), EstablishmentId = tenant, EventType = eventType, SchemaVersion = 1, OccurredAt = now, Payload = JsonSerializer.Serialize(new { eventId = refundId, eventType, schemaVersion = 1, occurredAtUtc = now, establishmentId = tenant, data = new { refundId, paymentAttemptId = refund.PaymentAttemptId, amount = refund.Amount, status = target.ToString().ToLowerInvariant() } }) });
        }
        var execution = await db.RefundProviderExecutions.SingleAsync(x => x.RefundId == refundId && x.EstablishmentId == tenant, ct);
        execution.Status = executionStatus;
        execution.LifecycleApplied = target is RefundStatus.Completed or RefundStatus.Failed;
        execution.ReconciliationRequired = target == RefundStatus.Processing;
        execution.UpdatedAt = now;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
