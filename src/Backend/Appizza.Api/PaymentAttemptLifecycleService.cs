using Appizza.Modules.Payments;
using Appizza.Modules.Auditing;
using System.Text.Json;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

internal enum PaymentAttemptLifecycleAction { Succeed, Fail, Cancel, Expire, MarkUnknown, ResolveUnknownSuccess, ResolveUnknownFailure }

internal sealed class PaymentAttemptLifecycleService(AppizzaDbContext db)
{
    public Task<PaymentAttempt> SucceedAsync(Guid tenant, Guid attemptId, CancellationToken ct = default) => TransitionAsync(tenant, attemptId, PaymentAttemptLifecycleAction.Succeed, ct);
    public Task<PaymentAttempt> FailAsync(Guid tenant, Guid attemptId, CancellationToken ct = default) => TransitionAsync(tenant, attemptId, PaymentAttemptLifecycleAction.Fail, ct);
    public Task<PaymentAttempt> CancelAsync(Guid tenant, Guid attemptId, CancellationToken ct = default) => TransitionAsync(tenant, attemptId, PaymentAttemptLifecycleAction.Cancel, ct);
    public Task<PaymentAttempt> ExpireAsync(Guid tenant, Guid attemptId, CancellationToken ct = default) => TransitionAsync(tenant, attemptId, PaymentAttemptLifecycleAction.Expire, ct);
    public Task<PaymentAttempt> MarkUnknownAsync(Guid tenant, Guid attemptId, CancellationToken ct = default) => TransitionAsync(tenant, attemptId, PaymentAttemptLifecycleAction.MarkUnknown, ct);
    public Task<PaymentAttempt> ResolveUnknownSuccessAsync(Guid tenant, Guid attemptId, CancellationToken ct = default) => TransitionAsync(tenant, attemptId, PaymentAttemptLifecycleAction.ResolveUnknownSuccess, ct);
    public Task<PaymentAttempt> ResolveUnknownFailureAsync(Guid tenant, Guid attemptId, CancellationToken ct = default) => TransitionAsync(tenant, attemptId, PaymentAttemptLifecycleAction.ResolveUnknownFailure, ct);

    private async Task<PaymentAttempt> TransitionAsync(Guid tenant, Guid attemptId, PaymentAttemptLifecycleAction action, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var attemptReference = await db.Set<PaymentAttempt>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == attemptId && x.EstablishmentId == tenant, ct)
            ?? throw new InvalidOperationException("PAYMENT_ATTEMPT_NOT_FOUND");
        await db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({$"{tenant:N}|payment-attempt|{attemptReference.TableSessionId:N}"}, 0))", ct);
        var attempt = await db.Set<PaymentAttempt>().SingleOrDefaultAsync(x => x.Id == attemptId && x.EstablishmentId == tenant, ct)
            ?? throw new InvalidOperationException("PAYMENT_ATTEMPT_NOT_FOUND");
        var session = await db.Set<TableSession>().FromSqlInterpolated($"select * from tables.table_session where id = {attempt.TableSessionId} and establishment_id = {tenant} for update").SingleAsync(ct);
        var target = action switch
        {
            PaymentAttemptLifecycleAction.Succeed or PaymentAttemptLifecycleAction.ResolveUnknownSuccess => PaymentAttemptStatus.Approved,
            PaymentAttemptLifecycleAction.Fail or PaymentAttemptLifecycleAction.ResolveUnknownFailure => PaymentAttemptStatus.Declined,
            PaymentAttemptLifecycleAction.Cancel => PaymentAttemptStatus.Cancelled,
            PaymentAttemptLifecycleAction.Expire => PaymentAttemptStatus.Expired,
            PaymentAttemptLifecycleAction.MarkUnknown => PaymentAttemptStatus.Unknown,
            _ => throw new InvalidOperationException("PAYMENT_ATTEMPT_INVALID_STATE")
        };
        if (attempt.Status == target) { await tx.CommitAsync(ct); return attempt; }
        if (action == PaymentAttemptLifecycleAction.MarkUnknown && attempt.Status is not (PaymentAttemptStatus.Created or PaymentAttemptStatus.AwaitingCustomerAction or PaymentAttemptStatus.Processing)) throw new InvalidOperationException("PAYMENT_ATTEMPT_INVALID_STATE");
        if (action is PaymentAttemptLifecycleAction.ResolveUnknownSuccess or PaymentAttemptLifecycleAction.ResolveUnknownFailure && attempt.Status != PaymentAttemptStatus.Unknown) throw new InvalidOperationException("PAYMENT_ATTEMPT_INVALID_STATE");
        if (action == PaymentAttemptLifecycleAction.Cancel && attempt.Status == PaymentAttemptStatus.Approved) throw new InvalidOperationException("PAYMENT_ATTEMPT_INVALID_STATE");
        if (attempt.Status is PaymentAttemptStatus.Approved or PaymentAttemptStatus.Declined or PaymentAttemptStatus.Cancelled or PaymentAttemptStatus.Expired) throw new InvalidOperationException("PAYMENT_ATTEMPT_INVALID_STATE");
        var releases = target is PaymentAttemptStatus.Approved or PaymentAttemptStatus.Declined or PaymentAttemptStatus.Cancelled or PaymentAttemptStatus.Expired;
        if (releases)
        {
            if (target == PaymentAttemptStatus.Approved) session.PaidAmount += attempt.Amount;
            session.ReservedAmount = Math.Max(0m, session.ReservedAmount - attempt.Amount);
            if (target == PaymentAttemptStatus.Approved) session.Status = session.PaidAmount >= session.TotalAmount ? "paid" : "partially_paid";
        }
        attempt.Status = target; attempt.UpdatedAt = DateTimeOffset.UtcNow; if (target == PaymentAttemptStatus.Approved) attempt.ApprovedAt = attempt.UpdatedAt;
        AddObservability(tenant, attempt, target, attempt.UpdatedAt);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return attempt;
    }

    private void AddObservability(Guid tenant, PaymentAttempt attempt, PaymentAttemptStatus status, DateTimeOffset occurredAt)
    {
        var eventType = $"payment-attempt-{status.ToString().ToLowerInvariant()}.v1";
        var eventId = Guid.NewGuid();
        db.Add(new OutboxMessage { Id = eventId, EstablishmentId = tenant, EventType = eventType, SchemaVersion = 1, OccurredAt = occurredAt, Payload = JsonSerializer.Serialize(new { eventId, eventType, schemaVersion = 1, occurredAtUtc = occurredAt, establishmentId = tenant, paymentAttemptId = attempt.Id, tableSessionId = attempt.TableSessionId, paymentPlanId = attempt.PaymentPlanId, paymentMethod = attempt.Method.ToString().ToLowerInvariant(), amount = attempt.Amount, status = status.ToString() }) });
        db.Add(new AuditEntry { Id = Guid.NewGuid(), EstablishmentId = tenant, Action = $"payment_attempt.{status.ToString().ToLowerInvariant()}", AggregateType = "payment_attempt", AggregateId = attempt.Id, OccurredAt = occurredAt });
    }
}
