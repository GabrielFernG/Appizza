using System.Net;
using System.Text.Json;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase4OutboxKitchenTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task FullKitchenHttpFlowAndConcurrentAcceptanceAreIdempotent()
    {
        fixture.Notifications.Reset(); var tenant = await fixture.CreateTenantAsync(2, 1); var seeded = await SeedOrderSubmitted(tenant, station: true);
        await fixture.DispatchPhase4Async();
        await using var db = fixture.CreateDbContext(); var production = await db.Set<ProductionItem>().SingleAsync(x => x.OrderItemId == seeded.OrderItemId);
        var queue = await fixture.GetAsync("api/v1/operations/kitchen/production-items", tenant.AccessToken); queue.EnsureSuccessStatusCode(); Assert.Contains(production.Id.ToString(), await queue.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var detail = await fixture.GetAsync($"api/v1/operations/kitchen/production-items/{production.Id}", tenant.AccessToken); detail.EnsureSuccessStatusCode(); Assert.Contains("Hist", await detail.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var key = Guid.NewGuid(); var responses = await fixture.ConcurrentAsync(
            () => fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{production.Id}/accept", null, tenant.AccessToken, key),
            () => fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{production.Id}/accept", null, tenant.AccessToken, key));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode)); db.ChangeTracker.Clear();
        var persisted = await db.Set<ProductionItem>().SingleAsync(x => x.Id == production.Id); Assert.Equal("awaiting_preparation", persisted.Status);
        var transitions = await db.Set<ProductionStatusHistory>().Where(x => x.ProductionItemId == production.Id).ToListAsync(); Assert.Equal(3, transitions.Count); Assert.Single(transitions, x => x.PreviousStatus is null && x.NewStatus == "awaiting_acceptance"); Assert.Single(transitions, x => x.PreviousStatus == "awaiting_acceptance" && x.NewStatus == "accepted"); Assert.Single(transitions, x => x.PreviousStatus == "accepted" && x.NewStatus == "awaiting_preparation");
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == tenant.EstablishmentId && x.EventType == "production-item-accepted.v1"));
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync(x => x.EstablishmentId == tenant.EstablishmentId && x.OperationType == "kitchen.accept" && x.IdempotencyKey == key.ToString()));
        var after = await fixture.GetAsync($"api/v1/operations/kitchen/production-items/{production.Id}", tenant.AccessToken); after.EnsureSuccessStatusCode(); Assert.Contains("awaiting_preparation", await after.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task KitchenResourcesAreTenantIsolatedThroughHttp()
    {
        var tenantA = await fixture.CreateTenantAsync(2, 1); var tenantB = await fixture.CreateTenantAsync(2, 1); var seeded = await SeedOrderSubmitted(tenantB, station: true); await fixture.DispatchPhase4Async();
        await using var db = fixture.CreateDbContext(); var production = await db.Set<ProductionItem>().SingleAsync(x => x.OrderItemId == seeded.OrderItemId); var station = production.StationId;
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.GetAsync($"api/v1/operations/kitchen/production-items/{production.Id}", tenantA.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.GetAsync($"api/v1/operations/kitchen/production-items?stationId={station}", tenantA.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{production.Id}/accept", null, tenantA.AccessToken, Guid.NewGuid())).StatusCode);
        db.ChangeTracker.Clear(); Assert.Equal("awaiting_acceptance", (await db.Set<ProductionItem>().SingleAsync(x => x.Id == production.Id)).Status); var history = await db.Set<ProductionStatusHistory>().Where(x => x.ProductionItemId == production.Id).ToListAsync(); Assert.Single(history); Assert.Equal("awaiting_acceptance", history[0].NewStatus);
    }

    [Fact]
    public async Task DistinctAcceptanceKeysPersistWinnerAndLoserResultsWithoutDuplicateEffect()
    {
        var tenant = await fixture.CreateTenantAsync(2, 1); var seeded = await SeedOrderSubmitted(tenant, station: true); await fixture.DispatchPhase4Async(); await using var db = fixture.CreateDbContext(); var production = await db.Set<ProductionItem>().SingleAsync(x => x.OrderItemId == seeded.OrderItemId); var firstKey = Guid.NewGuid(); var secondKey = Guid.NewGuid();
        var responses = await fixture.ConcurrentAsync(
            () => fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{production.Id}/accept", null, tenant.AccessToken, firstKey),
            () => fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{production.Id}/accept", null, tenant.AccessToken, secondKey));
        Assert.Contains(responses, x => x.StatusCode == HttpStatusCode.OK); var loser = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict); Assert.Equal("PRODUCTION_ITEM_ALREADY_ACCEPTED", await fixture.ErrorCodeAsync(loser)); Assert.DoesNotContain(responses, x => x.StatusCode == HttpStatusCode.InternalServerError);
        var winnerKey = responses[0].StatusCode == HttpStatusCode.OK ? firstKey : secondKey; var loserKey = winnerKey == firstKey ? secondKey : firstKey; db.ChangeTracker.Clear(); Assert.Equal("awaiting_preparation", (await db.Set<ProductionItem>().SingleAsync(x => x.Id == production.Id)).Status); Assert.Equal(3, await db.Set<ProductionStatusHistory>().CountAsync(x => x.ProductionItemId == production.Id)); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == tenant.EstablishmentId && x.EventType == "production-item-accepted.v1")); Assert.Equal(2, await db.IdempotencyRecords.CountAsync(x => x.EstablishmentId == tenant.EstablishmentId && x.OperationType == "kitchen.accept"));
        var winnerReplay = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{production.Id}/accept", null, tenant.AccessToken, winnerKey); Assert.Equal(HttpStatusCode.OK, winnerReplay.StatusCode); var loserReplay = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{production.Id}/accept", null, tenant.AccessToken, loserKey); Assert.Equal(HttpStatusCode.Conflict, loserReplay.StatusCode); Assert.Equal("PRODUCTION_ITEM_ALREADY_ACCEPTED", await fixture.ErrorCodeAsync(loserReplay)); db.ChangeTracker.Clear(); Assert.Equal(3, await db.Set<ProductionStatusHistory>().CountAsync(x => x.ProductionItemId == production.Id)); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == tenant.EstablishmentId && x.EventType == "production-item-accepted.v1"));
    }

    [Fact]
    public async Task KitchenRbacSeparatesQueueViewFromAcceptanceWithoutUnauthorizedMutation()
    {
        var tenant = await fixture.CreateTenantAsync(2, 1); var seeded = await SeedOrderSubmitted(tenant, station: true); await fixture.DispatchPhase4Async(); await using var db = fixture.CreateDbContext(); var production = await db.Set<ProductionItem>().SingleAsync(x => x.OrderItemId == seeded.OrderItemId); var noPermissions = await fixture.CreateUserTokenAsync(tenant.EstablishmentId); var viewOnly = await fixture.CreateUserTokenAsync(tenant.EstablishmentId, "kitchen.queue.view", "kitchen.production.view"); var acceptOnly = await fixture.CreateUserTokenAsync(tenant.EstablishmentId, "kitchen.production.accept");
        var deniedQueue = await fixture.GetAsync("api/v1/operations/kitchen/production-items", noPermissions); Assert.Equal(HttpStatusCode.Forbidden, deniedQueue.StatusCode); Assert.Equal("INSUFFICIENT_PERMISSION", await fixture.ErrorCodeAsync(deniedQueue)); Assert.Equal(HttpStatusCode.OK, (await fixture.GetAsync("api/v1/operations/kitchen/production-items", viewOnly)).StatusCode);
        var deniedAccept = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{production.Id}/accept", null, viewOnly, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Forbidden, deniedAccept.StatusCode); Assert.Equal("INSUFFICIENT_PERMISSION", await fixture.ErrorCodeAsync(deniedAccept)); db.ChangeTracker.Clear(); Assert.Equal("awaiting_acceptance", (await db.Set<ProductionItem>().SingleAsync(x => x.Id == production.Id)).Status); Assert.Equal(1, await db.Set<ProductionStatusHistory>().CountAsync(x => x.ProductionItemId == production.Id)); Assert.Equal(0, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == tenant.EstablishmentId && x.EventType == "production-item-accepted.v1"));
        Assert.Equal(HttpStatusCode.OK, (await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{production.Id}/accept", null, acceptOnly, Guid.NewGuid())).StatusCode);
    }
    [Fact]
    public async Task AllConsumersCompleteExactlyOnceAndOnlyThenOutboxIsProcessed()
    {
        fixture.Notifications.Reset(); var seeded = await SeedOrderSubmitted(true);
        await fixture.DispatchPhase4Async(); await fixture.DispatchPhase4Async();
        await using var db = fixture.CreateDbContext(); var consumers = await db.InboxMessages.Where(x => x.EventId == seeded.EventId).OrderBy(x => x.ConsumerName).Select(x => x.ConsumerName).ToListAsync(); Assert.Equal(["kitchen-intake-v1", "notifications-v1"], consumers); Assert.NotNull(await db.OutboxMessages.Where(x => x.Id == seeded.EventId).Select(x => x.ProcessedAt).SingleAsync()); Assert.Equal(1, await db.Set<ProductionItem>().CountAsync(x => x.OrderItemId == seeded.OrderItemId)); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EventType == "production-item-created.v1" && x.EstablishmentId == seeded.TenantId)); Assert.Equal(1, fixture.Notifications.Count(seeded.EventId));
    }

    [Fact]
    public async Task KitchenSuccessNotificationFailureRetriesOnlyNotificationAfterRestart()
    {
        fixture.Notifications.Reset(); fixture.Notifications.Fail = true; var seeded = await SeedOrderSubmitted(true); await fixture.DispatchPhase4Async();
        await using (var db = fixture.CreateDbContext()) { Assert.True(await db.InboxMessages.AnyAsync(x => x.EventId == seeded.EventId && x.ConsumerName == "kitchen-intake-v1")); Assert.False(await db.InboxMessages.AnyAsync(x => x.EventId == seeded.EventId && x.ConsumerName == "notifications-v1")); Assert.Null(await db.OutboxMessages.Where(x => x.Id == seeded.EventId).Select(x => x.ProcessedAt).SingleAsync()); Assert.Equal(1, await db.Set<ProductionItem>().CountAsync(x => x.OrderItemId == seeded.OrderItemId)); }
        fixture.Notifications.Fail = false; await fixture.DispatchPhase4Async(); await using var verified = fixture.CreateDbContext(); Assert.Equal(1, await verified.Set<ProductionItem>().CountAsync(x => x.OrderItemId == seeded.OrderItemId)); Assert.True(await verified.InboxMessages.AnyAsync(x => x.EventId == seeded.EventId && x.ConsumerName == "notifications-v1")); Assert.NotNull(await verified.OutboxMessages.Where(x => x.Id == seeded.EventId).Select(x => x.ProcessedAt).SingleAsync());
    }

    [Fact]
    public async Task NotificationSuccessKitchenFailureAndConcurrentWorkersDoNotDuplicateEffects()
    {
        fixture.Notifications.Reset(); var seeded = await SeedOrderSubmitted(false); await using (var db = fixture.CreateDbContext()) { db.Add(new InboxMessage { EventId = seeded.EventId, ConsumerName = "notifications-v1", ProcessedAt = DateTimeOffset.UtcNow, Result = "succeeded" }); await db.SaveChangesAsync(); }
        await fixture.DispatchPhase4Async(); await using (var failed = fixture.CreateDbContext()) { Assert.False(await failed.InboxMessages.AnyAsync(x => x.EventId == seeded.EventId && x.ConsumerName == "kitchen-intake-v1")); Assert.Null(await failed.OutboxMessages.Where(x => x.Id == seeded.EventId).Select(x => x.ProcessedAt).SingleAsync()); failed.Add(new Station { Id = Guid.NewGuid(), EstablishmentId = seeded.TenantId, Name = "Cozinha Geral", IsDefault = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); await failed.SaveChangesAsync(); }
        await Task.WhenAll(fixture.DispatchPhase4Async(), fixture.DispatchPhase4Async()); await using var verified = fixture.CreateDbContext(); Assert.Equal(1, await verified.Set<ProductionItem>().CountAsync(x => x.OrderItemId == seeded.OrderItemId)); Assert.Equal(1, await verified.OutboxMessages.CountAsync(x => x.EventType == "production-item-created.v1" && x.EstablishmentId == seeded.TenantId)); Assert.Equal(2, await verified.InboxMessages.CountAsync(x => x.EventId == seeded.EventId)); Assert.NotNull(await verified.OutboxMessages.Where(x => x.Id == seeded.EventId).Select(x => x.ProcessedAt).SingleAsync()); Assert.Equal(0, fixture.Notifications.Count(seeded.EventId));
    }

    [Fact]
    public async Task IntakeUsesSpecificOrDefaultStationAndQueueSequenceIsMonotonic()
    {
        fixture.Notifications.Reset(); var first = await SeedOrderSubmitted(true); var specific = Guid.NewGuid(); await using (var db = fixture.CreateDbContext()) { db.Add(new Station { Id = specific, EstablishmentId = first.TenantId, Name = "Forno", IsDefault = false, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); var item = await db.Set<OrderItem>().SingleAsync(x => x.Id == first.OrderItemId); item.Snapshot = JsonSerializer.Serialize(new { snapshot = new { product = new { requiresProduction = true, preparationStationId = specific } } }); await db.SaveChangesAsync(); }
        var second = await SeedOrderSubmittedForTenant(first, Guid.NewGuid()); await Task.WhenAll(fixture.DispatchPhase4Async(), fixture.DispatchPhase4Async()); await using var verified = fixture.CreateDbContext(); var rows = await verified.Set<ProductionItem>().Where(x => x.EstablishmentId == first.TenantId).OrderBy(x => x.QueuePosition).ToListAsync(); Assert.Equal(2, rows.Count); Assert.Equal(specific, rows.Single(x => x.OrderItemId == first.OrderItemId).StationId); Assert.True(rows[0].QueuePosition < rows[1].QueuePosition); Assert.All(rows, x => Assert.True(x.QueuePosition > 0));
    }

    private async Task<Seeded> SeedOrderSubmitted(bool station)
    {
        var tenant = await fixture.CreateTenantAsync(2, 1); return await SeedOrderSubmitted(tenant, station);
    }
    private async Task<Seeded> SeedOrderSubmitted(Phase1ApiFixture.TenantContext tenant, bool station)
    { var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]); var session = await fixture.OpenSessionAsync(device.AccessToken); var seed = new Seeded(tenant.EstablishmentId, device.DeviceId, session, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()); await using var db = fixture.CreateDbContext(); if (station) db.Add(new Station { Id = Guid.NewGuid(), EstablishmentId = tenant.EstablishmentId, Name = "Cozinha Geral", IsDefault = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); AddOrder(db, seed); await db.SaveChangesAsync(); return seed; }
    private async Task<Seeded> SeedOrderSubmittedForTenant(Seeded parent, Guid itemId) { var seed = parent with { OrderId = Guid.NewGuid(), OrderItemId = itemId, EventId = Guid.NewGuid() }; await using var db = fixture.CreateDbContext(); AddOrder(db, seed); await db.SaveChangesAsync(); return seed; }
    private static void AddOrder(AppizzaDbContext db, Seeded seed) { var now = DateTimeOffset.UtcNow; db.Add(new Order { Id = seed.OrderId, EstablishmentId = seed.TenantId, TableSessionId = seed.SessionId, SourceDeviceId = seed.DeviceId, ClientSubmissionId = Guid.NewGuid(), SubtotalAmount = 10, TotalAmount = 10, SubmittedAt = now, CreatedAt = now, UpdatedAt = now }); db.Add(new OrderItem { Id = seed.OrderItemId, OrderId = seed.OrderId, LocalCartItemId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductType = "simple", ProductName = "Histórico", Quantity = 1, UnitAmount = 10, TotalAmount = 10, ConfigurationVersion = "appizza-config-v1:test", CatalogRevisionId = Guid.NewGuid(), CatalogVersion = 1, AvailabilityVersion = 1, Snapshot = JsonSerializer.Serialize(new { snapshot = new { product = new { requiresProduction = false, preparationStationId = (Guid?)null } } }), CreatedAt = now, UpdatedAt = now }); db.Add(new OutboxMessage { Id = seed.EventId, EstablishmentId = seed.TenantId, EventType = "order-submitted.v1", SchemaVersion = 1, Payload = JsonSerializer.Serialize(new { eventId = seed.EventId, data = new { orderId = seed.OrderId } }), OccurredAt = now }); }
    private sealed record Seeded(Guid TenantId, Guid DeviceId, Guid SessionId, Guid OrderId, Guid OrderItemId, Guid EventId);
}
