using System.Text.Json;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

#pragma warning disable CA1848

public interface IPhase4NotificationPublisher { Task PublishAsync(Guid tenant, Guid eventId, string eventType, CancellationToken cancellationToken); }
public interface IPhase5OutboxTestHook
{
    Task BeforeConsumerAsync(string consumerName, Guid eventId, CancellationToken cancellationToken);
}
public sealed class Phase5OutboxTestHook : IPhase5OutboxTestHook
{
    private sealed class Gate { public readonly object Sync = new(); public TaskCompletionSource<bool>? Reached; public TaskCompletionSource<bool>? Release; public int Invocations; public bool Block; public bool Fail; public Exception? Failure; }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Gate> gates = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> eventInvocations = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Exception> eventFailures = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (TaskCompletionSource<bool> Reached, TaskCompletionSource<bool> Release)> eventBlocks = new(StringComparer.Ordinal);
    private Gate For(string name) => gates.GetOrAdd(name, _ => new Gate());
    public Task BeforeConsumerAsync(string consumerName, Guid eventId, CancellationToken cancellationToken)
    {
        var gate = For(consumerName); lock (gate.Sync) { gate.Invocations++; eventInvocations.AddOrUpdate($"{consumerName}|{eventId:N}", 1, static (_, value) => value + 1); gate.Reached?.TrySetResult(true); if (eventFailures.TryRemove($"{consumerName}|{eventId:N}", out var eventFailure)) throw eventFailure; if (eventBlocks.TryGetValue($"{consumerName}|{eventId:N}", out var eventBlock)) { eventBlock.Reached.TrySetResult(true); return eventBlock.Release.Task.WaitAsync(cancellationToken); } if (gate.Fail) { gate.Fail = false; throw gate.Failure ?? new InvalidOperationException($"Testing failure for {consumerName}"); } if (!gate.Block) return Task.CompletedTask; gate.Release ??= NewTcs(); }
        return gate.Release.Task.WaitAsync(cancellationToken);
    }
    public void BlockNext(string consumerName) { var g = For(consumerName); lock (g.Sync) { g.Block = true; g.Reached = NewTcs(); g.Release = null; } }
    public Task WaitUntilReachedAsync(string consumerName, CancellationToken ct = default) => (For(consumerName).Reached ?? NewTcs()).Task.WaitAsync(ct);
    public void Release(string consumerName) { var g = For(consumerName); lock (g.Sync) { g.Block = false; g.Release?.TrySetResult(true); } }
    public void FailNext(string consumerName, Exception? exception = null) { var g = For(consumerName); lock (g.Sync) { g.Fail = true; g.Failure = exception; } }
    public void FailNext(string consumerName, Guid eventId, Exception exception) => eventFailures[$"{consumerName}|{eventId:N}"] = exception;
    public void BlockNext(string consumerName, Guid eventId) => eventBlocks[$"{consumerName}|{eventId:N}"] = (NewTcs(), NewTcs());
    public Task WaitUntilReachedAsync(string consumerName, Guid eventId, CancellationToken ct = default) => eventBlocks[$"{consumerName}|{eventId:N}"].Reached.Task.WaitAsync(ct);
    public void Release(string consumerName, Guid eventId) { if (eventBlocks.TryRemove($"{consumerName}|{eventId:N}", out var block)) block.Release.TrySetResult(true); }
    public int GetInvocationCount(string consumerName) => For(consumerName).Invocations;
    public int GetInvocationCount(string consumerName, Guid eventId) => eventInvocations.TryGetValue($"{consumerName}|{eventId:N}", out var count) ? count : 0;
    public void Reset() { gates.Clear(); eventInvocations.Clear(); eventFailures.Clear(); eventBlocks.Clear(); }
    private static TaskCompletionSource<bool> NewTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class Phase4SignalRNotificationPublisher(Microsoft.AspNetCore.SignalR.IHubContext<Phase1Hub> hub) : IPhase4NotificationPublisher
{
    public Task PublishAsync(Guid tenant, Guid eventId, string eventType, CancellationToken cancellationToken)
    { var method = eventType == "order-submitted.v1" ? "OrderSubmitted" : eventType == "production-item-accepted.v1" ? "ProductionItemAccepted" : "ProductionQueueChanged"; return hub.Clients.Group($"establishment:{tenant}").SendAsync(method, new { eventId, eventType }, cancellationToken); }
}

public sealed class Phase4OutboxDispatcher(IServiceScopeFactory scopeFactory, IPhase4NotificationPublisher notifications, ILogger<Phase4OutboxDispatcher> logger, IPhase5OutboxTestHook? testHook = null) : BackgroundService
{
    private static readonly IReadOnlyDictionary<string, string[]> Consumers = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["order-submitted.v1"] = ["kitchen-intake-v1", "notifications-v1"],
        ["production-item-created.v1"] = ["notifications-v1"],
        ["production-item-accepted.v1"] = ["notifications-v1"],
        ["production-item-preparation-started.v1"] = ["kitchen-status-projection-v1", "kitchen-signalr-v1"],
        ["production-item-paused.v1"] = ["kitchen-status-projection-v1", "kitchen-signalr-v1"],
        ["production-item-resumed.v1"] = ["kitchen-status-projection-v1", "kitchen-signalr-v1"],
        ["production-attempt-failed.v1"] = ["kitchen-status-projection-v1", "kitchen-signalr-v1"],
        ["production-attempt-restarted.v1"] = ["kitchen-status-projection-v1", "kitchen-signalr-v1"],
        ["production-item-ready.v1"] = ["kitchen-status-projection-v1", "kitchen-signalr-v1"],
        ["order-item-cancellation-requested.v1"] = ["kitchen-request-v1", "ordering-signalr-v1"],
        ["order-item-cancellation-approved.v1"] = ["kitchen-request-v1", "ordering-signalr-v1"],
        ["order-item-cancellation-rejected.v1"] = ["kitchen-request-v1", "ordering-signalr-v1"],
        ["order-item-request-withdrawn.v1"] = ["kitchen-request-v1", "ordering-signalr-v1"],
        ["order-item-cancelled.v1"] = ["kitchen-commercial-change-v1", "ordering-signalr-v1"],
        ["production-item-cancelled.v1"] = ["kitchen-status-projection-v1", "kitchen-signalr-v1"],
        ["order-item-change-requested.v1"] = ["kitchen-request-v1", "ordering-signalr-v1"],
        ["order-item-change-review-confirmed.v1"] = ["kitchen-request-v1", "ordering-signalr-v1"],
        ["order-item-change-approved.v1"] = ["kitchen-request-v1", "ordering-signalr-v1"],
        ["order-item-change-rejected.v1"] = ["kitchen-request-v1", "ordering-signalr-v1"],
        ["order-item-changed.v1"] = ["kitchen-item-change-v1", "ordering-signalr-v1"]
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
        await (testHook ?? new Phase5OutboxTestHook()).BeforeConsumerAsync(consumer, message.Id, ct);
        if (consumer == "kitchen-intake-v1") await Intake(db, message, ct);
        else if (consumer == "kitchen-commercial-change-v1") await CancelProduction(db, message, ct);
        else if (consumer == "kitchen-item-change-v1") await ChangeProduction(db, message, ct);
        else if (consumer.EndsWith("signalr-v1", StringComparison.Ordinal) || consumer == "notifications-v1") await Notify(message, ct);
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

    private static async Task CancelProduction(AppizzaDbContext db, OutboxMessage message, CancellationToken ct)
    {
        if (message.EstablishmentId is not Guid tenant) throw new InvalidOperationException("OrderItemCancelled without tenant."); using var payload = JsonDocument.Parse(message.Payload); var orderItemId = Guid.Parse(Get(payload.RootElement.GetProperty("data"), "orderItemId").GetString()!);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM kitchen.production_item WHERE order_item_id = {orderItemId} AND establishment_id = {tenant} FOR UPDATE", ct); var item = await db.Set<ProductionItem>().SingleOrDefaultAsync(x => x.OrderItemId == orderItemId && x.EstablishmentId == tenant, ct); if (item is null || item.Status == "cancelled") return; var previous = item.Status; var now = DateTimeOffset.UtcNow;
        var attempt = await db.Set<ProductionAttempt>().SingleOrDefaultAsync(x => x.ProductionItemId == item.Id && x.Status == "active", ct); if (attempt is not null) { attempt.Status = "abandoned"; attempt.FinishedAt = now; } var pause = await db.Set<ProductionPause>().SingleOrDefaultAsync(x => x.ProductionItemId == item.Id && x.ResumedAt == null, ct); if (pause is not null) pause.ResumedAt = now;
        item.Status = "cancelled"; item.UpdatedAt = now; db.Add(new ProductionStatusHistory { Id = Guid.NewGuid(), ProductionItemId = item.Id, PreviousStatus = previous, NewStatus = "cancelled", ChangedAt = now }); var eventId = Guid.NewGuid(); db.Add(new OutboxMessage { Id = eventId, EstablishmentId = tenant, EventType = "production-item-cancelled.v1", SchemaVersion = 1, Payload = JsonSerializer.Serialize(new { eventId, eventType = "ProductionItemCancelled", schemaVersion = 1, occurredAtUtc = now, establishmentId = tenant, data = new { productionItemId = item.Id, orderItemId, previousStatus = previous, resultingStatus = "cancelled" } }), OccurredAt = now });
    }

    private static async Task ChangeProduction(AppizzaDbContext db, OutboxMessage message, CancellationToken ct)
    {
        if (message.EstablishmentId is not Guid tenant) throw new InvalidOperationException("OrderItemChanged without tenant."); using var payload = JsonDocument.Parse(message.Payload); var data = payload.RootElement.GetProperty("data"); var orderItemId = Guid.Parse(Get(data, "orderItemId").GetString()!); var actionNode = Get(data, "productionAction"); var action = actionNode.ValueKind == JsonValueKind.Null ? null : actionNode.GetString();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM kitchen.production_item WHERE order_item_id = {orderItemId} AND establishment_id = {tenant} FOR UPDATE", ct); var item = await db.Set<ProductionItem>().SingleOrDefaultAsync(x => x.OrderItemId == orderItemId && x.EstablishmentId == tenant, ct); if (item is null) return; var now = DateTimeOffset.UtcNow;
        if (action == "restart") { var active = await db.Set<ProductionAttempt>().SingleOrDefaultAsync(x => x.ProductionItemId == item.Id && x.Status == "active", ct); if (active is not null) { active.Status = "abandoned"; active.FinishedAt = now; } var number = await db.Set<ProductionAttempt>().Where(x => x.ProductionItemId == item.Id).MaxAsync(x => (int?)x.AttemptNumber, ct) ?? 0; db.Add(new ProductionAttempt { Id = Guid.NewGuid(), ProductionItemId = item.Id, AttemptNumber = number + 1, Status = "active", StartedAt = now, CreatedAt = now }); var previous = item.Status; item.Status = "in_preparation"; item.UpdatedAt = now; db.Add(new ProductionStatusHistory { Id = Guid.NewGuid(), ProductionItemId = item.Id, PreviousStatus = previous, NewStatus = "in_preparation", ChangedAt = now }); }
        else if (action == "continue") db.Add(new ProductionStatusHistory { Id = Guid.NewGuid(), ProductionItemId = item.Id, PreviousStatus = item.Status, NewStatus = item.Status, ChangedAt = now });
    }

    private async Task CompleteIfAllConsumersFinished(OutboxMessage message, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppizzaDbContext>(); await using var tx = await db.Database.BeginTransactionAsync(ct); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({message.Id.ToString("N") + "|complete"}, 0))", ct); var row = await db.OutboxMessages.SingleOrDefaultAsync(x => x.Id == message.Id && x.ProcessedAt == null, ct); if (row is null) { await tx.CommitAsync(ct); return; } var completed = await db.InboxMessages.Where(x => x.EventId == message.Id).Select(x => x.ConsumerName).ToListAsync(ct); if (Consumers[message.EventType].All(completed.Contains)) { row.ProcessedAt = DateTimeOffset.UtcNow; row.ErrorMessage = null; } await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    private async Task RecordFailure(Guid eventId, Exception exception, CancellationToken ct) { await using var scope = scopeFactory.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppizzaDbContext>(); var row = await db.OutboxMessages.SingleOrDefaultAsync(x => x.Id == eventId, ct); if (row is null) return; row.RetryCount++; row.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(30, row.RetryCount)); row.ErrorMessage = exception.Message[..Math.Min(exception.Message.Length, 1000)]; await db.SaveChangesAsync(ct); }
    private static JsonElement Get(JsonElement element, string name) { foreach (var property in element.EnumerateObject()) if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value; throw new InvalidOperationException($"Missing {name}."); }
    private static Guid? NullableGuid(JsonElement element, string name) { try { var value = Get(element, name); return value.ValueKind == JsonValueKind.Null ? null : Guid.Parse(value.GetString()!); } catch (InvalidOperationException) { return null; } }
}
