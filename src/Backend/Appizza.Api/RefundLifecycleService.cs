using Appizza.Modules.Auditing;
using Appizza.Modules.Payments;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Appizza.Api;

public sealed class RefundLifecycleService(AppizzaDbContext db)
{
    public async Task<Refund> CompleteCashAsync(Guid establishmentId, Guid refundId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var refund = await db.Refunds.FromSqlInterpolated($"select * from payments.refund where id = {refundId} and establishment_id = {establishmentId} for update").SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("REFUND_NOT_FOUND");
        var attempt = await db.Set<PaymentAttempt>().AsNoTracking().SingleAsync(x => x.Id == refund.PaymentAttemptId && x.EstablishmentId == establishmentId, ct);
        if (attempt.Method != PaymentMethod.Cash) throw new InvalidOperationException("REFUND_NOT_CASH");
        if (refund.Status == RefundStatus.Completed)
        {
            await transaction.CommitAsync(ct);
            return refund;
        }
        if (refund.Status != RefundStatus.Created) throw new InvalidOperationException("REFUND_INVALID_STATE");
        var now = DateTimeOffset.UtcNow;
        refund.Status = RefundStatus.Completed;
        refund.UpdatedAt = now;
        db.AuditEntries.Add(new AuditEntry { Id = Guid.NewGuid(), EstablishmentId = establishmentId, Action = "refund.completed", AggregateType = "refund", AggregateId = refund.Id, OccurredAt = now });
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(), EstablishmentId = establishmentId, EventType = "refund-completed.v1", SchemaVersion = 1, OccurredAt = now,
            Payload = JsonSerializer.Serialize(new { eventId = refund.Id, eventType = "refund-completed.v1", schemaVersion = 1, occurredAtUtc = now, establishmentId, data = new { refundId = refund.Id, paymentAttemptId = refund.PaymentAttemptId, amount = refund.Amount, status = "completed" } })
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return refund;
    }
}
