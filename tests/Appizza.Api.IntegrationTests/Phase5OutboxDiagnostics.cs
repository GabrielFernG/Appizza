using Appizza.Api;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

internal static class Phase5OutboxDiagnostics
{
    internal sealed record PendingEvent(int Ordinal, Guid EventId, string EventType, DateTimeOffset OccurredAt, DateTimeOffset? ProcessedAt);
    internal sealed record Snapshot(
        int PendingTotal,
        int EligiblePendingTotal,
        IReadOnlyDictionary<string, int> ByEventType,
        Guid? TargetEventId,
        DateTimeOffset? TargetOccurredAt,
        int? TargetOrdinal,
        bool TargetWithinFirst50,
        IReadOnlyList<PendingEvent> First50,
        IReadOnlyList<PendingEvent> Neighbours);

    public static async Task<Snapshot> CaptureAsync(AppizzaDbContext db, Guid? targetEventId = null, CancellationToken cancellationToken = default)
    {
        var pending = await db.OutboxMessages.AsNoTracking()
            .Where(x => x.ProcessedAt == null)
            .OrderBy(x => x.OccurredAt)
            .Select(x => new PendingEvent(0, x.Id, x.EventType, x.OccurredAt, x.ProcessedAt))
            .ToListAsync(cancellationToken);
        var eligible = pending.Where(x => Phase4OutboxDispatcher.DeliveryConsumerRegistry.ContainsKey(x.EventType)).ToList();
        var numbered = eligible.Select((x, index) => x with { Ordinal = index + 1 }).ToList();
        var target = targetEventId is Guid id ? numbered.FirstOrDefault(x => x.EventId == id) : null;
        var first50 = numbered.Take(50).ToArray();
        var neighbours = target is null ? [] : numbered.Where(x => Math.Abs(x.Ordinal - target.Ordinal) <= 10).ToArray();
        return new(
            pending.Count,
            numbered.Count,
            numbered.GroupBy(x => x.EventType).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal),
            target?.EventId,
            target?.OccurredAt,
            target?.Ordinal,
            target is not null && target.Ordinal <= 50,
            first50,
            neighbours);
    }

    public static void Print(string label, Snapshot snapshot)
    {
        Console.WriteLine($"[F6.15] {label}: pending={snapshot.PendingTotal}; eligible={snapshot.EligiblePendingTotal}; target={snapshot.TargetEventId}; occurredAt={snapshot.TargetOccurredAt:O}; ordinal={snapshot.TargetOrdinal}; withinFirst50={snapshot.TargetWithinFirst50}");
        Console.WriteLine($"[F6.15] ByEventType: {string.Join(", ", snapshot.ByEventType.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}"))}");
        Console.WriteLine($"[F6.15] First50: {string.Join(" | ", snapshot.First50.Select(Format))}");
        if (snapshot.Neighbours.Count > 0) Console.WriteLine($"[F6.15] Neighbours: {string.Join(" | ", snapshot.Neighbours.Select(Format))}");
    }

    private static string Format(PendingEvent item) => $"#{item.Ordinal} {item.EventId} {item.EventType} {item.OccurredAt:O} processedAt={item.ProcessedAt:O}";
}
