using System.Net;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5DeliveryConfirmationApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task CustomerConfirmationDeliversAndEmitsTwoEvents()
    {
        var s = await Seed(); var key = Guid.NewGuid(); var response = await Customer(s, s.ProductionVersion, key); response.EnsureSuccessStatusCode();
        await using var db = fixture.CreateDbContext(); var item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == s.ProductionItemId); var confirmation = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.ConfirmationId);
        Assert.Equal("delivered", item.Status); Assert.Equal(s.ProductionVersion + 1, item.Version); Assert.Equal("confirmed_manual", confirmation.Status); Assert.Equal("customer", confirmation.ConfirmationSource); Assert.Equal(s.DeviceId, confirmation.ConfirmedByDeviceId); Assert.NotNull(confirmation.ConfirmedAt);
        Assert.Equal(2, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && (x.EventType == "delivery-confirmed-by-customer.v1" || x.EventType == "production-item-delivered.v1")));
    }

    [Fact]
    public async Task EmployeeConfirmationDeliversWithPermission()
    {
        var s = await Seed(); var employee = await fixture.CreateUserTokenAsync(s.EstablishmentId, "kitchen.delivery.confirm"); var response = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-confirmations/{s.ConfirmationId}/confirm", new { expectedVersion = s.ProductionVersion }, employee, Guid.NewGuid()); response.EnsureSuccessStatusCode();
        await using var db = fixture.CreateDbContext(); var item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == s.ProductionItemId); var confirmation = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.ConfirmationId); Assert.Equal("delivered", item.Status); Assert.Equal("confirmed_manual", confirmation.Status); Assert.Equal("employee", confirmation.ConfirmationSource); Assert.Equal(2, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && (x.EventType == "delivery-confirmed-by-employee.v1" || x.EventType == "production-item-delivered.v1")));
    }

    [Fact]
    public async Task CustomerReplayAndDifferentPayloadAreIdempotent()
    {
        var s = await Seed(); var key = Guid.NewGuid(); var first = await Customer(s, s.ProductionVersion, key); first.EnsureSuccessStatusCode(); var replay = await Customer(s, s.ProductionVersion, key); Assert.Equal(HttpStatusCode.OK, replay.StatusCode); var divergent = await Customer(s, s.ProductionVersion + 1, key); Assert.Equal(HttpStatusCode.Conflict, divergent.StatusCode); Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", await fixture.ErrorCodeAsync(divergent));
        await using var db = fixture.CreateDbContext(); Assert.Equal(1, await db.Set<DeliveryConfirmation>().CountAsync(x => x.ProductionItemId == s.ProductionItemId)); Assert.Equal(1, await db.IdempotencyRecords.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.OperationType == "kitchen.delivery.confirm.customer"));
    }

    [Fact]
    public async Task StaleVersionAndMissingEmployeePermissionDoNotMutate()
    {
        var s = await Seed(); var stale = await Customer(s, s.ProductionVersion - 1, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.Equal("CONCURRENCY_CONFLICT", await fixture.ErrorCodeAsync(stale)); var noPermission = await fixture.CreateUserTokenAsync(s.EstablishmentId, "kitchen.production.view"); var denied = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-confirmations/{s.ConfirmationId}/confirm", new { expectedVersion = s.ProductionVersion }, noPermission, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal("INSUFFICIENT_PERMISSION", await fixture.ErrorCodeAsync(denied));
        await using var db = fixture.CreateDbContext(); Assert.Equal("awaiting_delivery_confirmation", await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync()); Assert.Equal("pending", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task CustomerAndEmployeeRaceHasSingleWinner()
    {
        var s = await Seed(); var employee = await fixture.CreateUserTokenAsync(s.EstablishmentId, "kitchen.delivery.confirm"); var responses = await fixture.ConcurrentAsync(() => Customer(s, s.ProductionVersion, Guid.NewGuid()), () => fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-confirmations/{s.ConfirmationId}/confirm", new { expectedVersion = s.ProductionVersion }, employee, Guid.NewGuid())); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound); Assert.DoesNotContain(responses, x => x.StatusCode == HttpStatusCode.InternalServerError);
        await using var db = fixture.CreateDbContext(); Assert.Equal("delivered", await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync()); Assert.Equal(1, await db.Set<DeliveryConfirmation>().CountAsync(x => x.ProductionItemId == s.ProductionItemId && x.Status == "confirmed_manual")); Assert.Equal(2, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && (x.EventType == "delivery-confirmed-by-customer.v1" || x.EventType == "delivery-confirmed-by-employee.v1" || x.EventType == "production-item-delivered.v1")));
    }

    [Fact]
    public async Task OtherDeviceSessionCannotConfirmAnotherSessionItem()
    {
        var s = await Seed("awaiting_delivery_confirmation", 2); var other = await fixture.RegisterAndBindAsync(s.Tenant.AccessToken, s.Tenant.TableIds[1]); await fixture.OpenSessionAsync(other.AccessToken); var otherToken = other.AccessToken;
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-confirmation", new { confirmation = "received", expectedVersion = s.ProductionVersion }, otherToken, Guid.NewGuid()); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertPending(s);
    }

    [Fact]
    public async Task BlockedAndRevokedDevicesCannotConfirm()
    {
        var blocked = await Seed(); await using (var db = fixture.CreateDbContext()) { var device = await db.Set<Appizza.Modules.Devices.Device>().SingleAsync(x => x.Id == blocked.DeviceId); device.Status = "blocked"; await db.SaveChangesAsync(); }
        var blockedResponse = await Customer(blocked, blocked.ProductionVersion, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Forbidden, blockedResponse.StatusCode); Assert.Equal("DEVICE_BLOCKED", await fixture.ErrorCodeAsync(blockedResponse)); await AssertPending(blocked);
        var revoked = await Seed(); await using (var db = fixture.CreateDbContext()) { var device = await db.Set<Appizza.Modules.Devices.Device>().SingleAsync(x => x.Id == revoked.DeviceId); device.Status = "revoked"; device.CredentialVersion++; await db.SaveChangesAsync(); }
        var revokedResponse = await Customer(revoked, revoked.ProductionVersion, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Forbidden, revokedResponse.StatusCode); Assert.Equal("DEVICE_CREDENTIAL_REVOKED", await fixture.ErrorCodeAsync(revokedResponse)); await AssertPending(revoked);
    }

    [Fact]
    public async Task CrossTenantCustomerAndEmployeeCannotMutateForeignDelivery()
    {
        var foreign = await Seed(); var local = await Seed(); var customer = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{foreign.OrderItemId}/delivery-confirmation", new { confirmation = "received", expectedVersion = foreign.ProductionVersion }, local.DeviceToken, Guid.NewGuid()); Assert.Equal(HttpStatusCode.NotFound, customer.StatusCode);
        var employee = await fixture.CreateUserTokenAsync(local.EstablishmentId, "kitchen.delivery.confirm"); var staff = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-confirmations/{foreign.ConfirmationId}/confirm", new { expectedVersion = foreign.ProductionVersion }, employee, Guid.NewGuid()); Assert.Equal(HttpStatusCode.NotFound, staff.StatusCode); await AssertPending(foreign);
    }

    [Theory]
    [InlineData("ready")][InlineData("delivered")][InlineData("cancelled")]
    public async Task InvalidProductionStatesRemainPending(string state)
    {
        var s = await Seed(); await using (var db = fixture.CreateDbContext()) { var item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == s.ProductionItemId); item.Status = state; await db.SaveChangesAsync(); }
        var response = await Customer(s, s.ProductionVersion, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); await using var verify = fixture.CreateDbContext(); Assert.Equal(state, await verify.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync()); Assert.Equal("pending", await verify.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task ManualConfirmationDoesNotChangeFinancialsOrRevisions()
    {
        var s = await Seed(); await using var before = fixture.CreateDbContext(); var orderBefore = await before.Set<Order>().SingleAsync(x => x.Id == s.OrderId); var itemBefore = await before.Set<OrderItem>().SingleAsync(x => x.Id == s.OrderItemId); var revisionBefore = await before.Set<OrderItemRevision>().CountAsync(x => x.OrderItemId == s.OrderItemId); var response = await Customer(s, s.ProductionVersion, Guid.NewGuid()); response.EnsureSuccessStatusCode(); await using var after = fixture.CreateDbContext(); var orderAfter = await after.Set<Order>().SingleAsync(x => x.Id == s.OrderId); var itemAfter = await after.Set<OrderItem>().SingleAsync(x => x.Id == s.OrderItemId); Assert.Equal(orderBefore.TotalAmount, orderAfter.TotalAmount); Assert.Equal(orderBefore.SubtotalAmount, orderAfter.SubtotalAmount); Assert.Equal(itemBefore.UnitAmount, itemAfter.UnitAmount); Assert.Equal(itemBefore.TotalAmount, itemAfter.TotalAmount); Assert.Equal(itemBefore.CurrentRevisionNumber, itemAfter.CurrentRevisionNumber); Assert.Equal(revisionBefore, await after.Set<OrderItemRevision>().CountAsync(x => x.OrderItemId == s.OrderItemId));
    }

    [Theory]
    [InlineData("confirmed_manual")]
    [InlineData("confirmed_automatic")]
    [InlineData("contested")]
    [InlineData("superseded")]
    public async Task ManualConfirmationRejectsNonPendingDeliveryConfirmation(string state)
    {
        var s = await Seed(); var before = await CaptureDeliveryState(s);
        await using (var db = fixture.CreateDbContext())
        {
            var confirmation = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.ConfirmationId);
            var now = DateTimeOffset.UtcNow;
            confirmation.Status = state; confirmation.Version = 7; confirmation.ConfirmationSource = state == "confirmed_automatic" ? "worker" : "customer";
            confirmation.ConfirmedAt = state is "confirmed_manual" or "confirmed_automatic" ? now : null;
            confirmation.ContestedAt = state == "contested" ? now : null;
            confirmation.SupersededAt = state == "superseded" ? now : null;
            var item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == s.ProductionItemId); item.Status = "delivered";
            await db.SaveChangesAsync();
        }
        var response = await Customer(s, s.ProductionVersion, Guid.NewGuid());
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        await using var after = fixture.CreateDbContext(); var persisted = await after.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.ConfirmationId); var itemAfter = await after.Set<ProductionItem>().SingleAsync(x => x.Id == s.ProductionItemId);
        Assert.Equal(state, persisted.Status); Assert.Equal(7, persisted.Version); Assert.Equal("delivered", itemAfter.Status); Assert.Equal(before.ConfirmationCount, await after.Set<DeliveryConfirmation>().CountAsync(x => x.ProductionItemId == s.ProductionItemId)); Assert.Equal(before.DeliveryEvents, await after.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && (x.EventType == "delivery-confirmed-by-customer.v1" || x.EventType == "delivery-confirmed-by-employee.v1" || x.EventType == "production-item-delivered.v1")));
    }

    [Fact]
    public async Task EmployeeConfirmationRejectsConfirmedManualStateThroughLifecycleGuard()
    {
        var s = await Seed(); await using (var db = fixture.CreateDbContext()) { var confirmation = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.ConfirmationId); confirmation.Status = "confirmed_manual"; confirmation.Version = 9; confirmation.ConfirmedAt = DateTimeOffset.UtcNow; confirmation.ConfirmationSource = "customer"; var item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == s.ProductionItemId); item.Status = "delivered"; await db.SaveChangesAsync(); }
        var employee = await fixture.CreateUserTokenAsync(s.EstablishmentId, "kitchen.delivery.confirm"); var response = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-confirmations/{s.ConfirmationId}/confirm", new { expectedVersion = s.ProductionVersion }, employee, Guid.NewGuid()); Assert.NotEqual(HttpStatusCode.OK, response.StatusCode); await using var dbAfter = fixture.CreateDbContext(); Assert.Equal("confirmed_manual", await dbAfter.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync()); Assert.Equal(0, await dbAfter.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "production-item-delivered.v1"));
    }

    private Task<HttpResponseMessage> Customer(Seeded s, long version, Guid key) => fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-confirmation", new { confirmation = "received", expectedVersion = version }, s.DeviceToken, key);
    private async Task<Seeded> Seed(string initialStatus = "awaiting_delivery_confirmation", int tableCount = 1)
    {
        var tenant = await fixture.CreateTenantAsync(2, tableCount); var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]); var session = await fixture.OpenSessionAsync(device.AccessToken); var now = DateTimeOffset.UtcNow; var orderId = Guid.NewGuid(); var itemId = Guid.NewGuid(); var stationId = Guid.NewGuid(); var productionId = Guid.NewGuid();
        await using var db = fixture.CreateDbContext(); db.Add(new Station { Id = stationId, EstablishmentId = tenant.EstablishmentId, Name = "Confirm", IsDefault = true, CreatedAt = now, UpdatedAt = now }); db.Add(new Order { Id = orderId, EstablishmentId = tenant.EstablishmentId, TableSessionId = session, SourceDeviceId = device.DeviceId, ClientSubmissionId = Guid.NewGuid(), SubtotalAmount = 10, TotalAmount = 10, SubmittedAt = now, CreatedAt = now, UpdatedAt = now }); db.Add(new OrderItem { Id = itemId, OrderId = orderId, LocalCartItemId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductType = "simple", ProductName = "Confirm", Quantity = 1, UnitAmount = 10, TotalAmount = 10, ConfigurationVersion = "v1", CatalogRevisionId = Guid.NewGuid(), CatalogVersion = 1, AvailabilityVersion = 1, Snapshot = "{}", CreatedAt = now, UpdatedAt = now }); db.Add(new ProductionItem { Id = productionId, EstablishmentId = tenant.EstablishmentId, OrderItemId = itemId, StationId = stationId, Status = "ready", ReceivedAt = now, AcceptedAt = now, AcceptedByUserId = tenant.UserId, ReadyAt = now, CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync(); var productionVersion = await db.Set<ProductionItem>().Where(x => x.Id == productionId).Select(x => x.Version).SingleAsync(); var send = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{productionId}/send-to-table", new { expectedVersion = productionVersion }, tenant.AccessToken, Guid.NewGuid()); send.EnsureSuccessStatusCode(); var confirmationId = await db.Set<DeliveryConfirmation>().Where(x => x.ProductionItemId == productionId).Select(x => x.Id).SingleAsync(); var currentVersion = await db.Set<ProductionItem>().Where(x => x.Id == productionId).Select(x => x.Version).SingleAsync(); return new(tenant, orderId, itemId, productionId, confirmationId, currentVersion, device.DeviceId, device.AccessToken, initialStatus);
    }
    private async Task AssertPending(Seeded s) { await using var db = fixture.CreateDbContext(); var item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == s.ProductionItemId); var confirmation = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.ConfirmationId); Assert.Equal(s.InitialStatus, item.Status); Assert.Equal("pending", confirmation.Status); Assert.Empty(await db.OutboxMessages.Where(x => x.EstablishmentId == s.EstablishmentId && (x.EventType == "delivery-confirmed-by-customer.v1" || x.EventType == "delivery-confirmed-by-employee.v1" || x.EventType == "production-item-delivered.v1")).ToListAsync()); }
    private async Task<(int ConfirmationCount, int DeliveryEvents)> CaptureDeliveryState(Seeded s) { await using var db = fixture.CreateDbContext(); return (await db.Set<DeliveryConfirmation>().CountAsync(x => x.ProductionItemId == s.ProductionItemId), await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && (x.EventType == "delivery-confirmed-by-customer.v1" || x.EventType == "delivery-confirmed-by-employee.v1" || x.EventType == "production-item-delivered.v1"))); }
    private sealed record Seeded(Phase1ApiFixture.TenantContext Tenant, Guid OrderId, Guid OrderItemId, Guid ProductionItemId, Guid ConfirmationId, long ProductionVersion, Guid DeviceId, string DeviceToken, string InitialStatus) { public Guid EstablishmentId => Tenant.EstablishmentId; }
}
