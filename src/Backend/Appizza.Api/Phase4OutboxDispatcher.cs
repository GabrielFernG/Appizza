using System.Text.Json;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

#pragma warning disable CA1848

public interface IPhase4NotificationPublisher { Task PublishAsync(Guid tenant, Guid eventId, string eventType, CancellationToken cancellationToken); }

public sealed class Phase4SignalRNotificationPublisher(Microsoft.AspNetCore.SignalR.IHubContext<Phase1Hub> hub) : IPhase4NotificationPublisher
{
    public Task PublishAsync(Guid tenant, Guid eventId, string eventType, CancellationToken cancellationToken)
    { var method = eventType == "order-submitted.v1" ? "OrderSubmitted" : eventType == "production-item-accepted.v1" ? "ProductionItemAccepted" : "ProductionQueueChanged"; return hub.Clients.Group($"establishment:{tenant}").SendAsync(method, new { eventId, eventType }, cancellationToken); }
}

public sealed class Phase4OutboxDispatcher(IServiceScopeFactory scopeFactory, IPhase4NotificationPublisher notifications, ILogger<Phase4OutboxDispatcher> logger) : BackgroundService
{
    private static readonly IReadOnlyDictionary<string, string[]> Consumers = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["order-submitted.v1"] = ["kitchen-intake-v1", "notifications-v1"],
        ["production-item-created.v1"] = ["notifications-v1"],
        ["production-item-accepted.v1"] = ["notifications-v1"]
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do { await DispatchOnceAsync(stoppingToken); } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task DispatchOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppizzaDbContext>();
        var messages = await db.OutboxMessages.AsNoTracking().Where(x => x.ProcessedAt == null && Consumers.Keys.Contains(x.EventType)).OrderBy(x => x.OccurredAt).Take(50).ToListAsync(ct);
        foreach (var message in messages)
        {
            var failed = false;
            foreach (var consumer in Consumers[message.EventType])
            {
                if (await db.InboxMessages.AsNoTracking().AnyAsync(x => x.EventId == message.Id && x.ConsumerName == consumer, ct)) continue;
                try { await Consume(message, consumer, ct); }
                catch (Exception ex) { failed = true; logger.LogError(ex, "Phase4 consumer {Consumer} failed for event {EventId}", consumer, message.Id); await RecordFailure(message.Id, ex, ct); break; }
            }
            if (!failed) await CompleteIfAllConsumersFinished(message, ct);
        }
    }

    private async Task Consume(OutboxMessage message, string consumer, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppizzaDbContext>(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({message.Id.ToString("N") + "|" + consumer}, 0))", ct);
        if (await db.InboxMessages.AnyAsync(x => x.EventId == message.Id && x.ConsumerName == consumer, ct)) { await tx.CommitAsync(ct); return; }
        if (consumer == "kitchen-intake-v1") await Intake(db, message, ct);
        else await Notify(message, ct);
        db.Add(new InboxMessage { EventId = message.Id, ConsumerName = consumer, ProcessedAt = DateTimeOffset.UtcNow, Result = "succeeded" }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    private static async Task Intake(AppizzaDbContext db, OutboxMessage message, CancellationToken ct)
    {
        if (message.EstablishmentId is not Guid tenant) throw new InvalidOperationException("OrderSubmitted without tenant."); using var payload = JsonDocument.Parse(message.Payload); var orderId = Guid.Parse(Get(payload.RootElement.GetProperty("data"), "orderId").GetString()!); var order = await db.Set<Order>().AsNoTracking().SingleAsync(x => x.Id == orderId && x.EstablishmentId == tenant, ct); var items = await db.Set<OrderItem>().AsNoTracking().Where(x => x.OrderId == order.Id).OrderBy(x => x.Id).ToListAsync(ct); var activeStations = await db.Set<Station>().Where(x => x.EstablishmentId == tenant && x.Status == "active").ToListAsync(ct); var fallback = activeStations.SingleOrDefault(x => x.IsDefault) ?? throw new InvalidOperationException("NO_ACTIVE_PRODUCTION_STATION");
        foreach (var item in items)
        {
            if (await db.Set<ProductionItem>().AnyAsync(x => x.OrderItemId == item.Id, ct)) continue; using var snapshot = JsonDocument.Parse(item.Snapshot); var historic = Get(snapshot.RootElement, "snapshot"); var product = Get(historic, "product"); var requested = NullableGuid(product, "preparationStationId"); var station = requested is Guid stationId ? activeStations.SingleOrDefault(x => x.Id == stationId) ?? fallback : fallback; var requiresProduction = Get(product, "requiresProduction").ValueKind == JsonValueKind.True; var now = DateTimeOffset.UtcNow; var production = new ProductionItem { Id = Guid.NewGuid(), EstablishmentId = tenant, OrderItemId = item.Id, StationId = station.Id, RequiresProduction = requiresProduction, ReceivedAt = now, CreatedAt = now, UpdatedAt = now }; db.Add(production); db.Add(new ProductionStatusHistory { Id = Guid.NewGuid(), ProductionItemId = production.Id, NewStatus = "awaiting_acceptance", ChangedAt = now }); var eventId = Guid.NewGuid(); db.Add(new OutboxMessage { Id = eventId, EstablishmentId = tenant, EventType = "production-item-created.v1", SchemaVersion = 1, Payload = JsonSerializer.Serialize(new { eventId, eventType = "ProductionItemCreated", schemaVersion = 1, occurredAtUtc = now, establishmentId = tenant, data = new { productionItemId = production.Id, orderItemId = item.Id, stationId = station.Id } }), OccurredAt = now });
        }
    }

    private async Task Notify(OutboxMessage message, CancellationToken ct)
    {
        if (message.EstablishmentId is not Guid tenant) return; await notifications.PublishAsync(tenant, message.Id, message.EventType, ct);
    }

    private async Task CompleteIfAllConsumersFinished(OutboxMessage message, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppizzaDbContext>(); await using var tx = await db.Database.BeginTransactionAsync(ct); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({message.Id.ToString("N") + "|complete"}, 0))", ct); var row = await db.OutboxMessages.SingleOrDefaultAsync(x => x.Id == message.Id && x.ProcessedAt == null, ct); if (row is null) { await tx.CommitAsync(ct); return; } var completed = await db.InboxMessages.Where(x => x.EventId == message.Id).Select(x => x.ConsumerName).ToListAsync(ct); if (Consumers[message.EventType].All(completed.Contains)) { row.ProcessedAt = DateTimeOffset.UtcNow; row.ErrorMessage = null; } await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    private async Task RecordFailure(Guid eventId, Exception exception, CancellationToken ct) { await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppizzaDbContext>(); var row = await db.OutboxMessages.SingleOrDefaultAsync(x => x.Id == eventId, ct); if (row is null) return; row.RetryCount++; row.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(30, row.RetryCount)); row.ErrorMessage = exception.Message[..Math.Min(exception.Message.Length, 1000)]; await db.SaveChangesAsync(ct); }
    private static JsonElement Get(JsonElement element, string name) { foreach (var property in element.EnumerateObject()) if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value; throw new InvalidOperationException($"Missing {name}."); }
    private static Guid? NullableGuid(JsonElement element, string name) { try { var value = Get(element, name); return value.ValueKind == JsonValueKind.Null ? null : Guid.Parse(value.GetString()!); } catch (InvalidOperationException) { return null; } }
}
