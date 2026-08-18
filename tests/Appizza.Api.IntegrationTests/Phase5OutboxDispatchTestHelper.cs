using Appizza.Api;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

internal static class Phase5OutboxDispatchTestHelper
{
    private static readonly string[] DispatcherEventTypes =
    [
        "order-submitted.v1", "production-item-created.v1", "production-item-accepted.v1",
        "production-item-preparation-started.v1", "production-item-paused.v1", "production-item-resumed.v1",
        "production-attempt-failed.v1", "production-attempt-restarted.v1", "production-item-ready.v1",
        "order-item-cancellation-requested.v1", "order-item-cancellation-approved.v1",
        "order-item-cancellation-rejected.v1", "order-item-request-withdrawn.v1", "order-item-cancelled.v1",
        "production-item-cancelled.v1", "order-item-change-requested.v1", "order-item-change-review-confirmed.v1",
        "order-item-change-approved.v1", "order-item-change-rejected.v1", "order-item-changed.v1",
        "production-item-sent-to-table.v1", "delivery-confirmation-requested.v1",
        "delivery-confirmed-by-customer.v1", "delivery-confirmed-by-employee.v1", "delivery-auto-confirmed.v1",
        "production-item-delivered.v1", "delivery-contested.v1", "delivery-contest-resolved.v1"
    ];

    public static Task DispatchUntilInboxAsync(Phase1ApiFixture fixture, Guid eventId, string consumerName, int maxBatches = 10, CancellationToken cancellationToken = default) =>
        DispatchUntilAsync(fixture, eventId, snapshot => snapshot.InboxConsumers.Contains(consumerName), maxBatches, cancellationToken);

    public static Task DispatchUntilProcessedAsync(Phase1ApiFixture fixture, Guid eventId, int maxBatches = 10, CancellationToken cancellationToken = default) =>
        DispatchUntilAsync(fixture, eventId, snapshot => snapshot.ProcessedAt is not null, maxBatches, cancellationToken);

    public static Task DispatchUntilInboxAsync(Phase4OutboxDispatcher dispatcher, Phase1ApiFixture fixture, Guid eventId, string consumerName, int maxBatches = 10, CancellationToken cancellationToken = default) =>
        DispatchUntilAsync(fixture, () => dispatcher.DispatchOnceAsync(cancellationToken), eventId, snapshot => snapshot.InboxConsumers.Contains(consumerName), maxBatches, cancellationToken);

    public static async Task DrainEligibleBacklogAsync(Phase1ApiFixture fixture, int maxBatches = 10, CancellationToken cancellationToken = default)
    {
        DispatchSnapshot? previous = null;
        for (var batch = 0; batch < maxBatches; batch++)
        {
            await using var before = fixture.CreateDbContext();
            var current = await SnapshotAsync(before, null, cancellationToken);
            if (current.EligiblePending == 0) return;
            if (previous is not null && !current.ProgressedFrom(previous)) throw new InvalidOperationException($"Outbox backlog made no progress while draining: eligible={current.EligiblePending}.");
            previous = current;
            await fixture.DispatchPhase4Async();
        }
        await using var finalDb = fixture.CreateDbContext();
        var final = await SnapshotAsync(finalDb, null, cancellationToken);
        if (final.EligiblePending > 0) throw new InvalidOperationException($"Outbox backlog was not drained within {maxBatches} batches: eligible={final.EligiblePending}.");
    }

    private static Task DispatchUntilAsync(Phase1ApiFixture fixture, Guid eventId, Func<DispatchSnapshot, bool> predicate, int maxBatches, CancellationToken cancellationToken) =>
        DispatchUntilAsync(fixture, fixture.DispatchPhase4Async, eventId, predicate, maxBatches, cancellationToken);

    private static async Task DispatchUntilAsync(Phase1ApiFixture fixture, Func<Task> dispatch, Guid eventId, Func<DispatchSnapshot, bool> predicate, int maxBatches, CancellationToken cancellationToken)
    {
        DispatchSnapshot? previous = null;
        for (var batch = 0; batch < maxBatches; batch++)
        {
            await using var before = fixture.CreateDbContext();
            var current = await SnapshotAsync(before, eventId, cancellationToken);
            if (predicate(current)) return;
            if (previous is not null && !current.ProgressedFrom(previous)) throw new InvalidOperationException($"Outbox dispatch made no progress for {eventId}: eligible={current.EligiblePending}, ordinal={current.Ordinal}, inbox=[{string.Join(',', current.InboxConsumers)}].");
            previous = current;
            await dispatch();
        }
        await using var finalDb = fixture.CreateDbContext();
        var final = await SnapshotAsync(finalDb, eventId, cancellationToken);
        throw new InvalidOperationException($"Event {eventId} was not reached within {maxBatches} batches: eligible={final.EligiblePending}, ordinal={final.Ordinal}, processedAt={final.ProcessedAt}, inbox=[{string.Join(',', final.InboxConsumers)}].");
    }

    private static async Task<DispatchSnapshot> SnapshotAsync(AppizzaDbContext db, Guid? eventId, CancellationToken cancellationToken)
    {
        var eligibleTypes = DispatcherEventTypes;
        var pending = await db.OutboxMessages.AsNoTracking().Where(x => x.ProcessedAt == null && eligibleTypes.Contains(x.EventType)).OrderBy(x => x.OccurredAt).Select(x => new { x.Id, x.ProcessedAt }).ToListAsync(cancellationToken);
        var target = eventId is Guid id ? await db.OutboxMessages.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.ProcessedAt }).SingleOrDefaultAsync(cancellationToken) : null;
        var inbox = eventId is Guid targetId ? await db.InboxMessages.AsNoTracking().Where(x => x.EventId == targetId).Select(x => x.ConsumerName).ToListAsync(cancellationToken) : [];
        var ordinal = eventId is Guid targetEventId ? pending.FindIndex(x => x.Id == targetEventId) + 1 : 0;
        return new(pending.Count, ordinal == 0 ? null : ordinal, target?.ProcessedAt, inbox);
    }

    private sealed record DispatchSnapshot(int EligiblePending, int? Ordinal, DateTimeOffset? ProcessedAt, IReadOnlyList<string> InboxConsumers)
    {
        public bool ProgressedFrom(DispatchSnapshot previous) => EligiblePending < previous.EligiblePending || (Ordinal is not null && previous.Ordinal is not null && Ordinal < previous.Ordinal) || InboxConsumers.Count > previous.InboxConsumers.Count || ProcessedAt is not null && previous.ProcessedAt is null;
    }
}
