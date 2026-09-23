using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Appizza.Modules.Auditing;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

internal sealed record PaymentAttemptReservationCommand(
    Guid EstablishmentId, Guid TableSessionId, Guid PaymentPlanId,
    IReadOnlyCollection<Guid> AllocationIds, PaymentMethod Method,
    string IdempotencyKey);

internal sealed record PaymentAttemptReservationResult(PaymentAttempt Attempt, bool Replay);

internal sealed class PaymentAttemptReservationService(AppizzaDbContext db)
{
    public async Task<PaymentAttemptReservationResult> CreateAsync(PaymentAttemptReservationCommand command, CancellationToken ct)
    {
        var selectors = command.AllocationIds.Distinct().OrderBy(x => x).ToArray();
        if (selectors.Length != command.AllocationIds.Count || selectors.Length == 0) throw new InvalidOperationException("INVALID_ALLOCATION_SELECTION");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { command.PaymentPlanId, allocations = selectors, method = command.Method.ToString().ToLowerInvariant() })))).ToLowerInvariant();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({$"{command.EstablishmentId:N}|payment-attempt|{command.TableSessionId:N}"}, 0))", ct);
        var key = command.IdempotencyKey;
        var existing = await db.Set<PaymentAttempt>().SingleOrDefaultAsync(x => x.EstablishmentId == command.EstablishmentId && x.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            if (existing.ProviderReference != fingerprint) throw new InvalidOperationException("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST");
            await tx.CommitAsync(ct); return new(existing, true);
        }
        var session = await db.Set<TableSession>().FromSqlInterpolated($"select * from tables.table_session where id = {command.TableSessionId} and establishment_id = {command.EstablishmentId} for update").SingleOrDefaultAsync(ct) ?? throw new InvalidOperationException("RESOURCE_NOT_FOUND");
        if (session.Status is "paid" or "closed" or "suspended" or "cancelled") throw new InvalidOperationException("SESSION_INVALID_STATE");
        var plan = await db.Set<PaymentPlan>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.PaymentPlanId && x.EstablishmentId == command.EstablishmentId && x.TableSessionId == command.TableSessionId, ct) ?? throw new InvalidOperationException("RESOURCE_NOT_FOUND");
        var allocations = await db.Set<PaymentPlanAllocation>().Where(x => x.PaymentPlanId == plan.Id && x.EstablishmentId == command.EstablishmentId && selectors.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync(ct);
        if (allocations.Count != selectors.Length) throw new InvalidOperationException("RESOURCE_NOT_FOUND");
        var held = await (from link in db.Set<PaymentAttemptAllocation>() join priorAttempt in db.Set<PaymentAttempt>() on link.PaymentAttemptId equals priorAttempt.Id where selectors.Contains(link.PaymentPlanAllocationId) && priorAttempt.Status != PaymentAttemptStatus.Declined && priorAttempt.Status != PaymentAttemptStatus.Cancelled && priorAttempt.Status != PaymentAttemptStatus.Expired select link.PaymentPlanAllocationId).ToListAsync(ct);
        if (held.Count != 0) throw new InvalidOperationException("ALLOCATION_UNAVAILABLE");
        var amount = allocations.Sum(x => x.Amount); var available = Math.Max(0m, session.TotalAmount - session.PaidAmount - session.ReservedAmount);
        if (amount > available) throw new InvalidOperationException("INSUFFICIENT_AVAILABLE_BALANCE");
        var now = DateTimeOffset.UtcNow; var attempt = new PaymentAttempt { Id = Guid.NewGuid(), EstablishmentId = command.EstablishmentId, TableSessionId = command.TableSessionId, PaymentPlanId = plan.Id, PaymentPlanVersion = plan.Version, Method = command.Method, Amount = amount, ReservedAmount = amount, Status = PaymentAttemptStatus.Created, IdempotencyKey = key, ProviderReference = fingerprint, CreatedAt = now, UpdatedAt = now, Version = 1 };
        foreach (var allocation in allocations) attempt.Allocations.Add(new PaymentAttemptAllocation { Id = Guid.NewGuid(), PaymentAttemptId = attempt.Id, PaymentPlanAllocationId = allocation.Id, Amount = allocation.Amount });
        session.ReservedAmount += amount; db.Add(attempt); AddObservability(command.EstablishmentId, attempt, "Created", now); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(attempt, false);
    }

    private void AddObservability(Guid tenant, PaymentAttempt attempt, string status, DateTimeOffset occurredAt)
    {
        var eventId = Guid.NewGuid();
        db.Add(new OutboxMessage { Id = eventId, EstablishmentId = tenant, EventType = $"payment-attempt-{status.ToLowerInvariant()}.v1", SchemaVersion = 1, OccurredAt = occurredAt, Payload = JsonSerializer.Serialize(new { eventId, eventType = $"payment-attempt-{status.ToLowerInvariant()}.v1", schemaVersion = 1, occurredAtUtc = occurredAt, establishmentId = tenant, paymentAttemptId = attempt.Id, tableSessionId = attempt.TableSessionId, paymentPlanId = attempt.PaymentPlanId, paymentMethod = attempt.Method.ToString().ToLowerInvariant(), amount = attempt.Amount, status }) });
        db.Add(new AuditEntry { Id = Guid.NewGuid(), EstablishmentId = tenant, Action = "payment_attempt.created", AggregateType = "payment_attempt", AggregateId = attempt.Id, OccurredAt = occurredAt });
    }
}
