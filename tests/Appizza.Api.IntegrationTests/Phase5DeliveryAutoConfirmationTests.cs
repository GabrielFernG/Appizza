using System.Text.Json;
using Appizza.Modules.Establishments;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Persistence;
using Appizza.Worker;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5DeliveryAutoConfirmationTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task ExpiredEnabledConfirmationIsAutomaticallyDelivered()
    {
        var s = await Seed(); await Expire(s);
        var processed = await fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync();
        Assert.Equal(1, processed); await AssertAutomatic(s);
    }

    [Fact]
    public async Task UnexpiredConfirmationIsIgnored()
    {
        var s = await Seed();
        var processed = await fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync();
        Assert.Equal(0, processed); await AssertPending(s);
    }

    [Fact]
    public async Task DisabledSettingAndHistoricalDeadlineDoNotAutoConfirm()
    {
        var s = await Seed(); await Expire(s); await SetEnabled(s, false);
        Assert.Equal(0, await fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync()); await AssertPending(s);
    }

    [Fact]
    public async Task TwoWorkersAndReplayProduceOneAutomaticTransition()
    {
        var s = await Seed(); await Expire(s);
        var a = fixture.CreateDeliveryAutoConfirmationWorker(); var b = fixture.CreateDeliveryAutoConfirmationWorker();
        var results = await Task.WhenAll(a.ProcessOnceAsync(), b.ProcessOnceAsync());
        Assert.Equal(1, results.Sum()); await AssertAutomatic(s);
        Assert.Equal(0, await fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync());
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-auto-confirmed.v1"));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "production-item-delivered.v1"));
    }

    [Fact]
    public async Task RestartedWorkerUsesPersistedExpiredDeadline()
    {
        var s = await Seed(); await Expire(s);
        Assert.Equal(1, await fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync());
        await AssertAutomatic(s);
    }

    [Fact]
    public async Task SettingsAreEvaluatedPerEstablishment()
    {
        var enabled = await Seed(); var disabled = await Seed(); await Expire(enabled); await Expire(disabled); await SetEnabled(disabled, false);
        var count = await fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync(); Assert.Equal(1, count); await AssertAutomatic(enabled); await AssertPending(disabled);
    }

    [Fact]
    public async Task ChangingMinutesDoesNotRecalculatePersistedDeadline()
    {
        var s = await Seed(); DateTimeOffset original; await using (var db = fixture.CreateDbContext()) original = await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.ExpiresAt).SingleAsync();
        await using (var db = fixture.CreateDbContext()) { db.Add(new EstablishmentSetting { Id = Guid.NewGuid(), EstablishmentId = s.EstablishmentId, SettingKey = Phase1SettingKeys.DeliveryAutoConfirmationMinutes, SettingValue = "999", ValueType = "integer", UpdatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); }
        await Expire(s); Assert.Equal(1, await fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync()); await AssertAutomatic(s); await using var verify = fixture.CreateDbContext(); Assert.NotEqual(original, await verify.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.ExpiresAt).SingleAsync());
    }

    [Theory]
    [InlineData("confirmed_manual")]
    [InlineData("confirmed_automatic")]
    [InlineData("contested")]
    [InlineData("superseded")]
    public async Task NonEligibleConfirmationStatesAreIgnored(string state)
    {
        var s = await Seed(); await using (var db = fixture.CreateDbContext()) { var c = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.ConfirmationId); c.Status = state; c.ConfirmationSource = state == "confirmed_automatic" ? "automatic" : "customer"; c.ConfirmedAt = state is "confirmed_manual" or "confirmed_automatic" ? DateTimeOffset.UtcNow : null; c.ContestedAt = state == "contested" ? DateTimeOffset.UtcNow : null; c.SupersededAt = state == "superseded" ? DateTimeOffset.UtcNow : null; var item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == s.ProductionItemId); item.Status = "delivered"; await db.SaveChangesAsync(); }
        Assert.Equal(0, await fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync()); await using var verify = fixture.CreateDbContext(); Assert.Equal(state, await verify.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync()); Assert.Empty(await verify.OutboxMessages.Where(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-auto-confirmed.v1").ToListAsync());
    }

    [Fact]
    public async Task AutoConfirmationDoesNotChangeFinancialHistory()
    {
        var s = await Seed(); await Expire(s);
        await using var before = fixture.CreateDbContext(); var order = await before.Set<Order>().SingleAsync(x => x.Id == s.OrderId); var item = await before.Set<OrderItem>().SingleAsync(x => x.Id == s.OrderItemId); var revisions = await before.Set<OrderItemRevision>().Where(x => x.OrderItemId == s.OrderItemId).Select(x => x.Id).ToListAsync();
        Assert.Equal(1, await fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync());
        await using var after = fixture.CreateDbContext(); var orderAfter = await after.Set<Order>().SingleAsync(x => x.Id == s.OrderId); var itemAfter = await after.Set<OrderItem>().SingleAsync(x => x.Id == s.OrderItemId); Assert.Equal(order.TotalAmount, orderAfter.TotalAmount); Assert.Equal(order.SubtotalAmount, orderAfter.SubtotalAmount); Assert.Equal(item.UnitAmount, itemAfter.UnitAmount); Assert.Equal(item.TotalAmount, itemAfter.TotalAmount); Assert.Equal(item.CurrentRevisionNumber, itemAfter.CurrentRevisionNumber); Assert.Equal(revisions, await after.Set<OrderItemRevision>().Where(x => x.OrderItemId == s.OrderItemId).Select(x => x.Id).ToListAsync());
    }

    [Fact]
    public async Task CustomerWinsAutoConfirmationWithDeterministicHook()
    {
        var s = await Seed(); await Expire(s); fixture.DeliveryHook.Reset(); fixture.DeliveryHook.BlockNext("worker-before-locks", s.ConfirmationId);
        var workerTask = fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync(); await fixture.DeliveryHook.WaitUntilReachedAsync("worker-before-locks", s.ConfirmationId);
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-confirmation", new { confirmation = "received", expectedVersion = s.ProductionVersion }, s.DeviceToken, Guid.NewGuid()); response.EnsureSuccessStatusCode(); fixture.DeliveryHook.Release("worker-before-locks", s.ConfirmationId); await workerTask;
        await using var db = fixture.CreateDbContext(); Assert.Equal("confirmed_manual", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync()); Assert.Equal(0, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-auto-confirmed.v1")); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-confirmed-by-customer.v1"));
    }

    [Fact]
    public async Task AutoConfirmationWinsCustomerWithDeterministicHook()
    {
        var s = await Seed(); await Expire(s); fixture.DeliveryHook.Reset(); fixture.DeliveryHook.BlockNext("customer-before-locks", s.OrderItemId);
        var customerTask = fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{s.OrderItemId}/delivery-confirmation", new { confirmation = "received", expectedVersion = s.ProductionVersion }, s.DeviceToken, Guid.NewGuid()); await fixture.DeliveryHook.WaitUntilReachedAsync("customer-before-locks", s.OrderItemId);
        Assert.Equal(1, await fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync()); fixture.DeliveryHook.Release("customer-before-locks", s.OrderItemId); var loser = await customerTask; Assert.Contains(loser.StatusCode, new[] { System.Net.HttpStatusCode.NotFound, System.Net.HttpStatusCode.Conflict });
        await using var db = fixture.CreateDbContext(); Assert.Equal("confirmed_automatic", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync()); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-auto-confirmed.v1")); Assert.Equal(0, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-confirmed-by-customer.v1"));
    }

    [Fact]
    public async Task EmployeeWinsAutoConfirmationWithDeterministicHook()
    {
        var s = await Seed(); await Expire(s); var employee = await fixture.CreateUserTokenAsync(s.EstablishmentId, "kitchen.delivery.confirm"); fixture.DeliveryHook.Reset(); fixture.DeliveryHook.BlockNext("worker-before-locks", s.ConfirmationId);
        var workerTask = fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync(); await fixture.DeliveryHook.WaitUntilReachedAsync("worker-before-locks", s.ConfirmationId);
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-confirmations/{s.ConfirmationId}/confirm", new { expectedVersion = s.ProductionVersion }, employee, Guid.NewGuid()); response.EnsureSuccessStatusCode(); fixture.DeliveryHook.Release("worker-before-locks", s.ConfirmationId); await workerTask;
        await using var db = fixture.CreateDbContext(); Assert.Equal("confirmed_manual", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync()); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-confirmed-by-employee.v1")); Assert.Equal(0, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-auto-confirmed.v1"));
    }

    [Fact]
    public async Task AutoConfirmationWinsEmployeeWithDeterministicHook()
    {
        var s = await Seed(); await Expire(s); var employee = await fixture.CreateUserTokenAsync(s.EstablishmentId, "kitchen.delivery.confirm"); fixture.DeliveryHook.Reset(); fixture.DeliveryHook.BlockNext("employee-before-locks", s.ConfirmationId);
        var employeeTask = fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-confirmations/{s.ConfirmationId}/confirm", new { expectedVersion = s.ProductionVersion }, employee, Guid.NewGuid()); await fixture.DeliveryHook.WaitUntilReachedAsync("employee-before-locks", s.ConfirmationId).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync()); fixture.DeliveryHook.Release("employee-before-locks", s.ConfirmationId); var loser = await employeeTask; Assert.Contains(loser.StatusCode, new[] { System.Net.HttpStatusCode.NotFound, System.Net.HttpStatusCode.Conflict });
        await using var db = fixture.CreateDbContext(); Assert.Equal("confirmed_automatic", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync()); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-auto-confirmed.v1")); Assert.Equal(0, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-confirmed-by-employee.v1"));
    }

    [Fact]
    public async Task AutoConfirmationIsReflectedAsDeliveredByPublicOrderStatus()
    {
        var s = await Seed(); await Expire(s);
        Assert.Equal(1, await fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync());

        var status = await fixture.GetAsync("api/v1/table-device/session/orders/status", s.DeviceToken);
        status.EnsureSuccessStatusCode();
        using var statusJson = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        var order = statusJson.RootElement.GetProperty("orders").EnumerateArray().Single(x => x.GetProperty("orderId").GetGuid() == s.OrderId);
        Assert.Equal("delivered", order.GetProperty("publicStatus").GetString());
        Assert.Equal("delivered", order.GetProperty("items")[0].GetProperty("publicStatus").GetString());

        var detail = await fixture.GetAsync($"api/v1/table-device/orders/{s.OrderId}", s.DeviceToken);
        detail.EnsureSuccessStatusCode();
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal("delivered", detailJson.RootElement.GetProperty("items")[0].GetProperty("publicStatus").GetString());
    }

    private async Task<Seeded> Seed()
    {
        var tenant = await fixture.CreateTenantAsync(2, 1); var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]); var session = await fixture.OpenSessionAsync(device.AccessToken); var now = DateTimeOffset.UtcNow; var orderId = Guid.NewGuid(); var orderItemId = Guid.NewGuid(); var stationId = Guid.NewGuid(); var productionId = Guid.NewGuid();
        await using (var db = fixture.CreateDbContext())
        {
            db.Add(new Station { Id = stationId, EstablishmentId = tenant.EstablishmentId, Name = "Auto", IsDefault = true, CreatedAt = now, UpdatedAt = now });
            db.Add(new Order { Id = orderId, EstablishmentId = tenant.EstablishmentId, TableSessionId = session, SourceDeviceId = device.DeviceId, ClientSubmissionId = Guid.NewGuid(), SubtotalAmount = 10, TotalAmount = 10, SubmittedAt = now, CreatedAt = now, UpdatedAt = now });
            db.Add(new OrderItem { Id = orderItemId, OrderId = orderId, LocalCartItemId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductType = "simple", ProductName = "Auto", Quantity = 1, UnitAmount = 10, TotalAmount = 10, ConfigurationVersion = "v1", CatalogRevisionId = Guid.NewGuid(), CatalogVersion = 1, AvailabilityVersion = 1, Snapshot = "{}", CreatedAt = now, UpdatedAt = now });
            db.Add(new ProductionItem { Id = productionId, EstablishmentId = tenant.EstablishmentId, OrderItemId = orderItemId, StationId = stationId, Status = "ready", ReceivedAt = now, AcceptedAt = now, AcceptedByUserId = tenant.UserId, ReadyAt = now, CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync();
            var version = await db.Set<ProductionItem>().Where(x => x.Id == productionId).Select(x => x.Version).SingleAsync();
            var response = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{productionId}/send-to-table", new { expectedVersion = version }, tenant.AccessToken, Guid.NewGuid()); response.EnsureSuccessStatusCode();
        }
        await using var verify = fixture.CreateDbContext(); var confirmation = await verify.Set<DeliveryConfirmation>().SingleAsync(x => x.ProductionItemId == productionId); var current = await verify.Set<ProductionItem>().Where(x => x.Id == productionId).Select(x => x.Version).SingleAsync(); return new(tenant.EstablishmentId, orderId, orderItemId, productionId, confirmation.Id, current, device.AccessToken);
    }

    private async Task Expire(Seeded s) { await using var db = fixture.CreateDbContext(); var c = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.ConfirmationId); c.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync(); }
    private async Task SetEnabled(Seeded s, bool enabled) { await using var db = fixture.CreateDbContext(); db.Add(new EstablishmentSetting { Id = Guid.NewGuid(), EstablishmentId = s.EstablishmentId, SettingKey = Phase1SettingKeys.DeliveryAutoConfirmationEnabled, SettingValue = enabled ? "true" : "false", ValueType = "boolean", UpdatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); }
    private async Task AssertPending(Seeded s) { await using var db = fixture.CreateDbContext(); Assert.Equal("awaiting_delivery_confirmation", await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync()); Assert.Equal("pending", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync()); Assert.Empty(await db.OutboxMessages.Where(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-auto-confirmed.v1").ToListAsync()); }
    private async Task AssertAutomatic(Seeded s) { await using var db = fixture.CreateDbContext(); var item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == s.ProductionItemId); var c = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.ConfirmationId); Assert.Equal("delivered", item.Status); Assert.Equal(s.ProductionVersion + 1, item.Version); Assert.Equal("confirmed_automatic", c.Status); Assert.Equal("automatic", c.ConfirmationSource); Assert.NotNull(c.ConfirmedAt); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-auto-confirmed.v1")); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "production-item-delivered.v1")); }
    private sealed record Seeded(Guid EstablishmentId, Guid OrderId, Guid OrderItemId, Guid ProductionItemId, Guid ConfirmationId, long ProductionVersion, string DeviceToken);
}
