using System.Net;
using System.Text.Json;
using Appizza.Modules.Establishments;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5DeliverySendApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task ReadyItemIsSentExactlyOnceWithPendingConfirmationAndTwoOutboxEvents()
    {
        var seeded = await Seed("ready"); var version = seeded.Version; var key = Guid.NewGuid();
        var response = await Send(seeded, version, key); response.EnsureSuccessStatusCode();
        await using var db = fixture.CreateDbContext();
        var item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == seeded.ProductionId);
        var confirmation = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.ProductionItemId == item.Id);
        Assert.Equal("awaiting_delivery_confirmation", item.Status); Assert.Equal(version + 1, item.Version);
        Assert.Equal(1, confirmation.SequenceNumber); Assert.Equal("pending", confirmation.Status); Assert.Equal(seeded.Tenant.EstablishmentId, confirmation.EstablishmentId);
        Assert.Equal(2, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == seeded.Tenant.EstablishmentId && (x.EventType == "production-item-sent-to-table.v1" || x.EventType == "delivery-confirmation-requested.v1")));
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync(x => x.EstablishmentId == seeded.Tenant.EstablishmentId && x.OperationType == "kitchen.delivery.send"));
    }

    [Fact]
    public async Task ReplayAndDifferentPayloadDoNotCreateAnotherConfirmation()
    {
        var seeded = await Seed("ready"); var key = Guid.NewGuid(); var first = await Send(seeded, seeded.Version, key); first.EnsureSuccessStatusCode();
        var replay = await Send(seeded, seeded.Version, key); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var divergent = await Send(seeded, seeded.Version + 1, key); Assert.Equal(HttpStatusCode.Conflict, divergent.StatusCode); Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", await fixture.ErrorCodeAsync(divergent));
        await using var db = fixture.CreateDbContext(); Assert.Equal(1, await db.Set<DeliveryConfirmation>().CountAsync(x => x.ProductionItemId == seeded.ProductionId)); Assert.Equal(1, await db.Set<ProductionItem>().CountAsync(x => x.Id == seeded.ProductionId && x.Status == "awaiting_delivery_confirmation"));
    }

    [Fact]
    public async Task StaleVersionAndMissingPermissionDoNotMutate()
    {
        var seeded = await Seed("ready"); var stale = await Send(seeded, seeded.Version - 1, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.Equal("CONCURRENCY_CONFLICT", await fixture.ErrorCodeAsync(stale));
        var noPermission = await fixture.CreateUserTokenAsync(seeded.Tenant.EstablishmentId, "kitchen.production.view"); var denied = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{seeded.ProductionId}/send-to-table", new { expectedVersion = seeded.Version }, noPermission, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal("INSUFFICIENT_PERMISSION", await fixture.ErrorCodeAsync(denied));
        await using var db = fixture.CreateDbContext(); Assert.Equal("ready", await db.Set<ProductionItem>().Where(x => x.Id == seeded.ProductionId).Select(x => x.Status).SingleAsync()); Assert.Empty(await db.Set<DeliveryConfirmation>().Where(x => x.ProductionItemId == seeded.ProductionId).ToListAsync());
    }

    [Theory]
    [InlineData("awaiting_acceptance")][InlineData("accepted")][InlineData("awaiting_preparation")][InlineData("in_preparation")][InlineData("paused")][InlineData("awaiting_delivery_confirmation")][InlineData("delivered")][InlineData("cancelled")]
    public async Task NonReadyStatesAreRejected(string status)
    {
        var seeded = await Seed(status); var response = await Send(seeded, seeded.Version, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Equal("PRODUCTION_ITEM_NOT_READY", await fixture.ErrorCodeAsync(response));
        await using var db = fixture.CreateDbContext(); Assert.Equal(status, await db.Set<ProductionItem>().Where(x => x.Id == seeded.ProductionId).Select(x => x.Status).SingleAsync()); Assert.Empty(await db.Set<DeliveryConfirmation>().Where(x => x.ProductionItemId == seeded.ProductionId).ToListAsync());
    }

    [Fact]
    public async Task ConcurrentSendsProduceOneTransitionOneConfirmationAndTwoEvents()
    {
        var seeded = await Seed("ready"); var responses = await fixture.ConcurrentAsync(() => Send(seeded, seeded.Version, Guid.NewGuid()), () => Send(seeded, seeded.Version, Guid.NewGuid()));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict); Assert.DoesNotContain(responses, x => x.StatusCode == HttpStatusCode.InternalServerError);
        await using var db = fixture.CreateDbContext(); Assert.Equal(1, await db.Set<DeliveryConfirmation>().CountAsync(x => x.ProductionItemId == seeded.ProductionId)); Assert.Equal(2, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == seeded.Tenant.EstablishmentId && (x.EventType == "production-item-sent-to-table.v1" || x.EventType == "delivery-confirmation-requested.v1")));
    }

    [Fact]
    public async Task SendBeforeLocksStageIsReachedAndReleaseCompletesRequest()
    {
        var seeded = await Seed("ready"); fixture.DeliveryHook.Reset();
        fixture.DeliveryHook.BlockNext("send-before-locks", seeded.ProductionId, "send_to_table");
        var request = Send(seeded, seeded.Version, Guid.NewGuid());
        await fixture.DeliveryHook.WaitUntilReachedAsync("send-before-locks", seeded.ProductionId, "send_to_table");
        Assert.Equal(1, fixture.DeliveryHook.GetInvocationCount("send-before-locks", seeded.ProductionId, "send_to_table"));
        Assert.False(request.IsCompleted);
        await using (var db = fixture.CreateDbContext()) { Assert.Empty(await db.Set<DeliveryConfirmation>().Where(x => x.ProductionItemId == seeded.ProductionId).ToListAsync()); Assert.Equal("ready", await db.Set<ProductionItem>().Where(x => x.Id == seeded.ProductionId).Select(x => x.Status).SingleAsync()); }
        fixture.DeliveryHook.Release("send-before-locks", seeded.ProductionId, "send_to_table");
        (await request).EnsureSuccessStatusCode(); fixture.DeliveryHook.Reset();
    }

    [Fact]
    public async Task SendAfterProductionLockStageIsReachedAndReleaseCompletesRequest()
    {
        var seeded = await Seed("ready"); fixture.DeliveryHook.Reset();
        fixture.DeliveryHook.BlockNext("send-after-production-item-lock", seeded.ProductionId, "send_to_table");
        var request = Send(seeded, seeded.Version, Guid.NewGuid());
        await fixture.DeliveryHook.WaitUntilReachedAsync("send-after-production-item-lock", seeded.ProductionId, "send_to_table");
        Assert.Equal(1, fixture.DeliveryHook.GetInvocationCount("send-after-production-item-lock", seeded.ProductionId, "send_to_table"));
        await using (var db = fixture.CreateDbContext()) { Assert.Empty(await db.Set<DeliveryConfirmation>().Where(x => x.ProductionItemId == seeded.ProductionId).ToListAsync()); Assert.Equal("ready", await db.Set<ProductionItem>().Where(x => x.Id == seeded.ProductionId).Select(x => x.Status).SingleAsync()); }
        fixture.DeliveryHook.Release("send-after-production-item-lock", seeded.ProductionId, "send_to_table");
        (await request).EnsureSuccessStatusCode(); fixture.DeliveryHook.Reset();
    }

    private async Task<HttpResponseMessage> Send(Seeded seeded, long version, Guid key) => await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{seeded.ProductionId}/send-to-table", new { expectedVersion = version }, seeded.Tenant.AccessToken, key);
    private async Task<Seeded> Seed(string status)
    {
        var tenant = await fixture.CreateTenantAsync(2, 1); var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]); var session = await fixture.OpenSessionAsync(device.AccessToken); var now = DateTimeOffset.UtcNow; var orderId = Guid.NewGuid(); var itemId = Guid.NewGuid(); var stationId = Guid.NewGuid(); var productionId = Guid.NewGuid();
        await using var db = fixture.CreateDbContext(); db.Add(new Station { Id = stationId, EstablishmentId = tenant.EstablishmentId, Name = "Delivery", IsDefault = true, CreatedAt = now, UpdatedAt = now }); db.Add(new Order { Id = orderId, EstablishmentId = tenant.EstablishmentId, TableSessionId = session, SourceDeviceId = device.DeviceId, ClientSubmissionId = Guid.NewGuid(), SubtotalAmount = 10, TotalAmount = 10, SubmittedAt = now, CreatedAt = now, UpdatedAt = now }); db.Add(new OrderItem { Id = itemId, OrderId = orderId, LocalCartItemId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductType = "simple", ProductName = "Delivery", Quantity = 1, UnitAmount = 10, TotalAmount = 10, ConfigurationVersion = "v1", CatalogRevisionId = Guid.NewGuid(), CatalogVersion = 1, AvailabilityVersion = 1, Snapshot = "{}", CreatedAt = now, UpdatedAt = now }); db.Add(new ProductionItem { Id = productionId, EstablishmentId = tenant.EstablishmentId, OrderItemId = itemId, StationId = stationId, Status = status, ReceivedAt = now, AcceptedAt = now, AcceptedByUserId = tenant.UserId, ReadyAt = status == "ready" ? now : null, CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync(); var version = await db.Set<ProductionItem>().Where(x => x.Id == productionId).Select(x => x.Version).SingleAsync(); return new(tenant, productionId, version);
    }
    private sealed record Seeded(Phase1ApiFixture.TenantContext Tenant, Guid ProductionId, long Version);
}
