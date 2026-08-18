using System.Net;
using System.Text.Json;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Modules.Tables;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5CancellationApiTests(Phase1ApiFixture fixture)
{
    [Theory]
    [InlineData(null)]
    [InlineData("awaiting_acceptance")]
    [InlineData("accepted")]
    [InlineData("awaiting_preparation")]
    public async Task AutomaticCancellationIsAtomicHistoricalAndEventuallyCancelsProduction(string? status)
    {
        var seed = await Seed(status, 2); var key = Guid.NewGuid(); var response = await Cancel(seed, seed.ItemIds[0], key); Assert.Equal(HttpStatusCode.Created, response.StatusCode); using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal("approved", body.RootElement.GetProperty("status").GetString()); Assert.True(body.RootElement.GetProperty("automatic").GetBoolean());
        var replay = await Cancel(seed, seed.ItemIds[0], key); using var replayBody = JsonDocument.Parse(await replay.Content.ReadAsStringAsync()); Assert.True(JsonElement.DeepEquals(body.RootElement, replayBody.RootElement));
        await using (var db = fixture.CreateDbContext()) { var item = await db.Set<OrderItem>().SingleAsync(x => x.Id == seed.ItemIds[0]); Assert.Equal("cancelled", item.CommercialStatus); using var expectedSnapshot = JsonDocument.Parse(seed.Snapshot); using var actualSnapshot = JsonDocument.Parse(item.Snapshot); Assert.True(JsonElement.DeepEquals(expectedSnapshot.RootElement, actualSnapshot.RootElement)); var order = await db.Set<Order>().SingleAsync(x => x.Id == seed.OrderId); Assert.Equal("partially_cancelled", order.Status); Assert.Equal(10, order.TotalAmount); var session = await db.Set<TableSession>().SingleAsync(x => x.Id == seed.SessionId); Assert.Equal(10, session.TotalAmount); Assert.Equal(2, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == seed.Tenant.EstablishmentId && (x.EventType == "order-item-cancellation-approved.v1" || x.EventType == "order-item-cancelled.v1"))); }
        await fixture.DispatchPhase4Async(); await fixture.DispatchPhase4Async(); if (status is not null) { await using var db = fixture.CreateDbContext(); var production = await db.Set<ProductionItem>().SingleAsync(x => x.OrderItemId == seed.ItemIds[0]); Assert.Equal("cancelled", production.Status); Assert.Equal(1, await db.Set<ProductionStatusHistory>().CountAsync(x => x.ProductionItemId == production.Id && x.NewStatus == "cancelled")); var cancelled = await db.OutboxMessages.SingleAsync(x => x.EstablishmentId == seed.Tenant.EstablishmentId && x.EventType == "order-item-cancelled.v1"); Assert.True(await db.InboxMessages.AnyAsync(x => x.EventId == cancelled.Id && x.ConsumerName == "kitchen-commercial-change-v1")); }
    }

    [Theory]
    [InlineData("in_preparation")]
    [InlineData("paused")]
    public async Task KitchenDecisionCanApproveOrRequestCanBeWithdrawn(string status)
    {
        var seed = await Seed(status); var created = await Cancel(seed, seed.ItemIds[0], Guid.NewGuid()); created.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync()); var requestId = json.RootElement.GetProperty("requestId").GetGuid(); Assert.Equal("pending_operational_decision", json.RootElement.GetProperty("status").GetString());
        var detail = await fixture.GetAsync($"api/v1/table-device/orders/{seed.OrderId}", seed.DeviceToken); detail.EnsureSuccessStatusCode(); Assert.Contains("attention_required", await detail.Content.ReadAsStringAsync());
        var user = await fixture.CreateUserTokenAsync(seed.Tenant.EstablishmentId, "kitchen.order_item_request.decide", "ordering.order_item_request.view"); var approve = await fixture.PostWithIdempotencyAsync($"api/v1/operations/order-item-requests/{requestId}/decide", new { decision = "approve", reason = "Aprovado", expectedVersion = 1 }, user, Guid.NewGuid()); approve.EnsureSuccessStatusCode();
        await using var db = fixture.CreateDbContext(); Assert.Equal("cancelled", await db.Set<OrderItem>().Where(x => x.Id == seed.ItemIds[0]).Select(x => x.CommercialStatus).SingleAsync());
    }

    [Fact]
    public async Task PendingRequestCanBeWithdrawnWithoutCommercialEffect()
    {
        var seed = await Seed("in_preparation"); var created = await Cancel(seed, seed.ItemIds[0], Guid.NewGuid()); using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync()); var requestId = json.RootElement.GetProperty("requestId").GetGuid(); var key = Guid.NewGuid(); var withdrawn = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-item-requests/{requestId}/withdraw", new { expectedVersion = 1 }, seed.DeviceToken, key); withdrawn.EnsureSuccessStatusCode(); var replay = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-item-requests/{requestId}/withdraw", new { expectedVersion = 1 }, seed.DeviceToken, key); replay.EnsureSuccessStatusCode();
        await using var db = fixture.CreateDbContext(); Assert.Equal("withdrawn", await db.Set<OrderItemRequest>().Where(x => x.Id == requestId).Select(x => x.Status).SingleAsync()); Assert.Equal("submitted", await db.Set<OrderItem>().Where(x => x.Id == seed.ItemIds[0]).Select(x => x.CommercialStatus).SingleAsync());
    }

    [Fact]
    public async Task ReadyRequiresManagerPermissionAndCrossTenantIsHidden()
    {
        var seed = await Seed("ready"); var created = await Cancel(seed, seed.ItemIds[0], Guid.NewGuid()); using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync()); var requestId = json.RootElement.GetProperty("requestId").GetGuid(); var kitchen = await fixture.CreateUserTokenAsync(seed.Tenant.EstablishmentId, "kitchen.order_item_request.decide"); var denied = await fixture.PostWithIdempotencyAsync($"api/v1/operations/order-item-requests/{requestId}/decide", new { decision = "approve", reason = "x" }, kitchen, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var manager = await fixture.CreateUserTokenAsync(seed.Tenant.EstablishmentId, "ordering.order_item.cancel_ready"); (await fixture.PostWithIdempotencyAsync($"api/v1/operations/order-item-requests/{requestId}/decide", new { decision = "approve", reason = "gerencial" }, manager, Guid.NewGuid())).EnsureSuccessStatusCode();
        var other = await fixture.CreateTenantAsync(1, 1); Assert.Equal(HttpStatusCode.NotFound, (await fixture.PostWithIdempotencyAsync($"api/v1/operations/order-item-requests/{requestId}/decide", new { decision = "reject", reason = "x" }, other.AccessToken, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task ConcurrentRequestsAndConcurrentItemCancellationsDoNotDuplicateOrLoseTotals()
    {
        var duplicate = await Seed("awaiting_preparation"); var requests = await fixture.ConcurrentAsync(() => Cancel(duplicate, duplicate.ItemIds[0], Guid.NewGuid()), () => Cancel(duplicate, duplicate.ItemIds[0], Guid.NewGuid())); Assert.Single(requests, x => x.StatusCode == HttpStatusCode.Created); Assert.Single(requests, x => x.StatusCode == HttpStatusCode.Conflict);
        var seed = await Seed("awaiting_preparation", 2); var cancellations = await fixture.ConcurrentAsync(() => Cancel(seed, seed.ItemIds[0], Guid.NewGuid()), () => Cancel(seed, seed.ItemIds[1], Guid.NewGuid())); Assert.All(cancellations, x => Assert.Equal(HttpStatusCode.Created, x.StatusCode));
        await using var db = fixture.CreateDbContext(); var order = await db.Set<Order>().SingleAsync(x => x.Id == seed.OrderId); Assert.Equal("cancelled", order.Status); Assert.Equal(0, order.TotalAmount); var session = await db.Set<TableSession>().SingleAsync(x => x.Id == seed.SessionId); Assert.Equal(0, session.TotalAmount); Assert.Equal(2, await db.Set<OrderItemRequest>().CountAsync(x => x.OrderId == seed.OrderId));
    }

    [Fact]
    public async Task ForbiddenFutureStateAndTenantAwareIdempotencyHaveNoMutation()
    {
        var seed = await Seed("cancelled"); var forbidden = await Cancel(seed, seed.ItemIds[0], Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, forbidden.StatusCode); Assert.Equal("ORDER_ITEM_CANCELLATION_NOT_ALLOWED", await fixture.ErrorCodeAsync(forbidden)); var other = await Seed(null); var same = Guid.NewGuid(); Assert.Equal(HttpStatusCode.Created, (await Cancel(other, other.ItemIds[0], same)).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await Cancel(seed, seed.ItemIds[0], same)).StatusCode);
    }

    [Fact]
    public async Task ApproveRejectAndWithdrawDecisionRacesHaveOneTerminalWinner()
    {
        var seed = await Seed("in_preparation"); var created = await Cancel(seed, seed.ItemIds[0], Guid.NewGuid()); using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync()); var requestId = json.RootElement.GetProperty("requestId").GetGuid(); var user = await fixture.CreateUserTokenAsync(seed.Tenant.EstablishmentId, "kitchen.order_item_request.decide");
        var decisions = await fixture.ConcurrentAsync(() => fixture.PostWithIdempotencyAsync($"api/v1/operations/order-item-requests/{requestId}/decide", new { decision = "approve", reason = "approve" }, user, Guid.NewGuid()), () => fixture.PostWithIdempotencyAsync($"api/v1/operations/order-item-requests/{requestId}/decide", new { decision = "reject", reason = "reject" }, user, Guid.NewGuid())); Assert.Single(decisions, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(decisions, x => x.StatusCode == HttpStatusCode.Conflict);
        var second = await Seed("paused"); var pending = await Cancel(second, second.ItemIds[0], Guid.NewGuid()); using var pendingJson = JsonDocument.Parse(await pending.Content.ReadAsStringAsync()); var secondId = pendingJson.RootElement.GetProperty("requestId").GetGuid(); var secondUser = await fixture.CreateUserTokenAsync(second.Tenant.EstablishmentId, "kitchen.order_item_request.decide"); var race = await fixture.ConcurrentAsync(() => fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-item-requests/{secondId}/withdraw", new { }, second.DeviceToken, Guid.NewGuid()), () => fixture.PostWithIdempotencyAsync($"api/v1/operations/order-item-requests/{secondId}/decide", new { decision = "reject", reason = "no" }, secondUser, Guid.NewGuid())); Assert.Single(race, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(race, x => x.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CrossTenantCreationIsHiddenAndConcurrentSameKeyReplaysOneEffect()
    {
        var a = await Seed(null); var b = await Seed(null); var denied = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{b.ItemIds[0]}/cancellation-requests", new { reasonCode = "X" }, a.DeviceToken, Guid.NewGuid()); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); var key = Guid.NewGuid(); var same = await fixture.ConcurrentAsync(() => Cancel(a, a.ItemIds[0], key), () => Cancel(a, a.ItemIds[0], key)); Assert.All(same, x => Assert.Equal(HttpStatusCode.Created, x.StatusCode)); await using var db = fixture.CreateDbContext(); Assert.Equal(1, await db.Set<OrderItemRequest>().CountAsync(x => x.OrderItemId == a.ItemIds[0])); Assert.Equal(1, await db.IdempotencyRecords.CountAsync(x => x.EstablishmentId == a.Tenant.EstablishmentId && x.OperationType == "ordering.cancellation.create" && x.IdempotencyKey == key.ToString())); Assert.Equal("submitted", await db.Set<OrderItem>().Where(x => x.Id == b.ItemIds[0]).Select(x => x.CommercialStatus).SingleAsync());
    }

    [Theory]
    [InlineData("awaiting_preparation", "start-preparation")]
    [InlineData("in_preparation", "pause")]
    [InlineData("in_preparation", "ready")]
    public async Task CancellationCreationAndProductionTransitionSerializeWithoutPartialState(string initialStatus, string action)
    {
        var seed = await Seed(initialStatus);
        Guid productionId;
        await using (var setup = fixture.CreateDbContext())
        {
            productionId = await setup.Set<ProductionItem>().Where(x => x.OrderItemId == seed.ItemIds[0]).Select(x => x.Id).SingleAsync();
            if (initialStatus == "in_preparation")
            {
                setup.Add(new ProductionAttempt { Id = Guid.NewGuid(), ProductionItemId = productionId, AttemptNumber = 1, Status = "active", StartedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
                await setup.SaveChangesAsync();
            }
        }

        var productionPayload = action == "pause" ? new { reasonCode = "WAIT" } : (object)new { };
        var responses = await fixture.ConcurrentAsync(
            () => Cancel(seed, seed.ItemIds[0], Guid.NewGuid()),
            () => fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{productionId}/{action}", productionPayload, seed.Tenant.AccessToken, Guid.NewGuid()));

        Assert.DoesNotContain(responses, response => response.StatusCode == HttpStatusCode.InternalServerError);
        await fixture.DispatchPhase4Async();
        await fixture.DispatchPhase4Async();
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Set<OrderItemRequest>().CountAsync(x => x.OrderItemId == seed.ItemIds[0]));
        var itemStatus = await db.Set<OrderItem>().Where(x => x.Id == seed.ItemIds[0]).Select(x => x.CommercialStatus).SingleAsync();
        var productionStatus = await db.Set<ProductionItem>().Where(x => x.Id == productionId).Select(x => x.Status).SingleAsync();
        if (itemStatus == "cancelled") Assert.Equal("cancelled", productionStatus);
        else Assert.Equal("pending_operational_decision", await db.Set<OrderItemRequest>().Where(x => x.OrderItemId == seed.ItemIds[0]).Select(x => x.Status).SingleAsync());
    }

    private Task<HttpResponseMessage> Cancel(SeedData seed, Guid item, Guid key) => fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{item}/cancellation-requests", new { reasonCode = "CUSTOMER_REQUEST", customerNote = "Cancelar", expectedOrderItemVersion = 1 }, seed.DeviceToken, key);
    private async Task<SeedData> Seed(string? productionStatus, int count = 1)
    {
        var tenant = await fixture.CreateTenantAsync(2, 1); var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]); var sessionId = await fixture.OpenSessionAsync(device.AccessToken); var now = DateTimeOffset.UtcNow; const string snapshot = "{\"Snapshot\":{\"product\":{\"Name\":\"Histórico\"}}}"; var order = new Order { Id = Guid.NewGuid(), EstablishmentId = tenant.EstablishmentId, TableSessionId = sessionId, SourceDeviceId = device.DeviceId, ClientSubmissionId = Guid.NewGuid(), SubtotalAmount = count * 10, TotalAmount = count * 10, SubmittedAt = now, CreatedAt = now, UpdatedAt = now }; var station = new Station { Id = Guid.NewGuid(), EstablishmentId = tenant.EstablishmentId, Name = Guid.NewGuid().ToString(), CreatedAt = now, UpdatedAt = now }; var ids = new List<Guid>(); await using var db = fixture.CreateDbContext(); db.AddRange(order, station); var session = await db.Set<TableSession>().SingleAsync(x => x.Id == sessionId); session.SubtotalAmount = count * 10; session.TotalAmount = count * 10; session.RemainingAmount = count * 10;
        for (var i = 0; i < count; i++) { var id = Guid.NewGuid(); ids.Add(id); db.Add(new OrderItem { Id = id, OrderId = order.Id, LocalCartItemId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductType = "simple", ProductName = "Histórico", Quantity = 1, UnitAmount = 10, TotalAmount = 10, ConfigurationVersion = "v1", CatalogRevisionId = Guid.NewGuid(), CatalogVersion = 1, AvailabilityVersion = 1, Snapshot = snapshot, CreatedAt = now, UpdatedAt = now }); if (productionStatus is not null) db.Add(new ProductionItem { Id = Guid.NewGuid(), EstablishmentId = tenant.EstablishmentId, OrderItemId = id, StationId = station.Id, Status = productionStatus, ReceivedAt = now, PreparationStartedAt = productionStatus is "in_preparation" or "paused" or "ready" ? now : null, ReadyAt = productionStatus == "ready" ? now : null, CreatedAt = now, UpdatedAt = now }); }
        await db.SaveChangesAsync(); return new(tenant, device.AccessToken, sessionId, order.Id, ids, snapshot);
    }
    private sealed record SeedData(Phase1ApiFixture.TenantContext Tenant, string DeviceToken, Guid SessionId, Guid OrderId, IReadOnlyList<Guid> ItemIds, string Snapshot);
}
