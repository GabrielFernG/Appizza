using System.Net;
using System.Text.Json;
using Appizza.Modules.Devices;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5OrderStatusApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task EmptySessionAndMultipleOrdersReturnDeterministicStatusWithoutWrites()
    {
        var context = await CreateContext();
        var empty = await fixture.GetAsync("api/v1/table-device/session/orders/status", context.Device.AccessToken); empty.EnsureSuccessStatusCode();
        using (var json = JsonDocument.Parse(await empty.Content.ReadAsStringAsync())) Assert.Empty(json.RootElement.GetProperty("orders").EnumerateArray());
        var first = await AddOrder(context, ["awaiting_preparation", "ready"]); var second = await AddOrder(context, [null]);
        await using var beforeDb = fixture.CreateDbContext(); var beforeHistory = await beforeDb.Set<ProductionStatusHistory>().CountAsync(); var beforeOutbox = await beforeDb.OutboxMessages.CountAsync();
        var response = await fixture.GetAsync("api/v1/table-device/session/orders/status", context.Device.AccessToken); response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var orders = payload.RootElement.GetProperty("orders").EnumerateArray().ToArray(); Assert.Equal(2, orders.Length);
        Assert.Equal("received", orders.Single(x => x.GetProperty("orderId").GetGuid() == first.OrderId).GetProperty("publicStatus").GetString());
        Assert.Equal("pending_kitchen_intake", orders.Single(x => x.GetProperty("orderId").GetGuid() == second.OrderId).GetProperty("items")[0].GetProperty("publicSubstatus").GetString());
        await using var afterDb = fixture.CreateDbContext(); Assert.Equal(beforeHistory, await afterDb.Set<ProductionStatusHistory>().CountAsync()); Assert.Equal(beforeOutbox, await afterDb.OutboxMessages.CountAsync()); Assert.Empty(afterDb.ChangeTracker.Entries());
    }

    [Fact]
    public async Task LifecycleTransitionsAreReflectedAndHistoricalSnapshotComesFromOrderItem()
    {
        var context = await CreateContext(); var seeded = await AddOrder(context, ["awaiting_preparation"], "Produto Histórico"); var productionId = seeded.ProductionIds.Single();
        await AssertItem(context, seeded.OrderId, "received", "awaiting_preparation", "Produto Histórico");
        await PostLifecycle(context, productionId, "start-preparation", new { }); await AssertItem(context, seeded.OrderId, "preparing", "preparing", "Produto Histórico");
        await PostLifecycle(context, productionId, "pause", new { reasonCode = "WAIT" }); await AssertItem(context, seeded.OrderId, "preparing", "paused", "Produto Histórico");
        await PostLifecycle(context, productionId, "resume", new { }); await AssertItem(context, seeded.OrderId, "preparing", "preparing", "Produto Histórico");
        await PostLifecycle(context, productionId, "ready", new { }); await AssertItem(context, seeded.OrderId, "ready", "ready", "Produto Histórico");
        await using var db = fixture.CreateDbContext(); var item = await db.Set<OrderItem>().SingleAsync(x => x.OrderId == seeded.OrderId); item.ProductName = "Produto Histórico"; item.Snapshot = "{\"Snapshot\":{\"product\":{\"Name\":\"Produto Histórico\",\"Description\":\"Descrição vendida\"}}}"; await db.SaveChangesAsync();
        var detail = await fixture.GetAsync($"api/v1/table-device/orders/{seeded.OrderId}", context.Device.AccessToken); detail.EnsureSuccessStatusCode(); var body = await detail.Content.ReadAsStringAsync(); Assert.Contains("Produto Histórico", body); Assert.Contains("Descrição vendida", body); Assert.DoesNotContain("Catálogo atual", body);
    }

    [Fact]
    public async Task DetailAndListAreSessionAndTenantIsolatedAndRejectInvalidDevice()
    {
        var a = await CreateContext(); var b = await CreateContext(); var orderB = await AddOrder(b, ["ready"]);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.GetAsync($"api/v1/table-device/orders/{orderB.OrderId}", a.Device.AccessToken)).StatusCode);
        var listA = await fixture.GetAsync("api/v1/table-device/session/orders/status", a.Device.AccessToken); listA.EnsureSuccessStatusCode(); Assert.DoesNotContain(orderB.OrderId.ToString(), await listA.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        await fixture.PostAsync($"api/v1/operations/table-devices/{a.Device.DeviceId}/block", null, a.Tenant.AccessToken);
        var blocked = await fixture.GetAsync("api/v1/table-device/session/orders/status", a.Device.AccessToken); Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode); Assert.Equal("DEVICE_BLOCKED", await fixture.ErrorCodeAsync(blocked));
        await fixture.PostAsync($"api/v1/operations/table-devices/{a.Device.DeviceId}/unblock", null, a.Tenant.AccessToken);
        var revoked = await fixture.GetAsync("api/v1/table-device/session/orders/status", a.Device.AccessToken); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode); Assert.Equal("DEVICE_CREDENTIAL_REVOKED", await fixture.ErrorCodeAsync(revoked));
    }

    private async Task AssertItem(Context context, Guid orderId, string status, string substatus, string historicalName)
    {
        var response = await fixture.GetAsync($"api/v1/table-device/orders/{orderId}", context.Device.AccessToken); response.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var item = json.RootElement.GetProperty("items")[0]; Assert.Equal(status, item.GetProperty("publicStatus").GetString()); Assert.Equal(substatus, item.GetProperty("publicSubstatus").GetString()); Assert.Equal(historicalName, item.GetProperty("productName").GetString());
    }

    private async Task PostLifecycle(Context context, Guid id, string action, object body) => (await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{id}/{action}", body, context.Tenant.AccessToken, Guid.NewGuid())).EnsureSuccessStatusCode();

    private async Task<Context> CreateContext()
    {
        var tenant = await fixture.CreateTenantAsync(2, 1); var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]); var session = await fixture.OpenSessionAsync(device.AccessToken); return new(tenant, device, session);
    }

    private async Task<SeededOrder> AddOrder(Context context, string?[] statuses, string productName = "Item")
    {
        var now = DateTimeOffset.UtcNow; var order = new Order { Id = Guid.NewGuid(), EstablishmentId = context.Tenant.EstablishmentId, TableSessionId = context.SessionId, SourceDeviceId = context.Device.DeviceId, ClientSubmissionId = Guid.NewGuid(), SubtotalAmount = statuses.Length * 10, TotalAmount = statuses.Length * 10, SubmittedAt = now, CreatedAt = now, UpdatedAt = now };
        var station = new Station { Id = Guid.NewGuid(), EstablishmentId = context.Tenant.EstablishmentId, Name = $"Status-{Guid.NewGuid():N}", IsDefault = false, CreatedAt = now, UpdatedAt = now }; var productions = new List<Guid>();
        await using var db = fixture.CreateDbContext(); db.AddRange(order, station);
        foreach (var status in statuses) { var item = new OrderItem { Id = Guid.NewGuid(), OrderId = order.Id, LocalCartItemId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductType = "simple", ProductName = productName, Quantity = 1, UnitAmount = 10, TotalAmount = 10, ConfigurationVersion = "hash", CatalogRevisionId = Guid.NewGuid(), CatalogVersion = 1, AvailabilityVersion = 1, Snapshot = $"{{\"Snapshot\":{{\"product\":{{\"Name\":\"{productName}\"}}}}}}", CreatedAt = now, UpdatedAt = now }; db.Add(item); if (status is not null) { var productionId = Guid.NewGuid(); productions.Add(productionId); db.Add(new ProductionItem { Id = productionId, EstablishmentId = context.Tenant.EstablishmentId, OrderItemId = item.Id, StationId = station.Id, Status = status, RequiresProduction = true, ReceivedAt = now, AcceptedAt = status == "awaiting_acceptance" ? null : now, PreparationStartedAt = status is "in_preparation" or "paused" or "ready" ? now : null, ReadyAt = status == "ready" ? now : null, CurrentAttemptNumber = status is "in_preparation" or "paused" or "ready" ? 1 : 0, CreatedAt = now, UpdatedAt = now }); if (status is "in_preparation" or "paused") db.Add(new ProductionAttempt { Id = Guid.NewGuid(), ProductionItemId = productionId, AttemptNumber = 1, Status = "active", StartedAt = now, CreatedByUserId = context.Tenant.UserId, CreatedAt = now }); }
        }
        await db.SaveChangesAsync(); return new(order.Id, productions);
    }

    private sealed record Context(Phase1ApiFixture.TenantContext Tenant, Phase1ApiFixture.BoundDevice Device, Guid SessionId);
    private sealed record SeededOrder(Guid OrderId, IReadOnlyList<Guid> ProductionIds);
}
