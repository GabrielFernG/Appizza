using System.Net;
using System.Text.Json;
using Appizza.Modules.Kitchen;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;
using Appizza.Modules.Ordering;
using Appizza.Modules.Tables;
using Appizza.Modules.Devices;
using Appizza.Modules.Establishments;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5DeliveryEndToEndTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task SendThenCustomerConfirmReconcilesBothReadModels()
    {
        var seed = await SeedReadyAsync();
        await using (var db = fixture.CreateDbContext())
        {
            var version = await db.Set<ProductionItem>().Where(x => x.Id == seed.ProductionItemId).Select(x => x.Version).SingleAsync();
            (await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{seed.ProductionItemId}/send-to-table", new { expectedVersion = version }, seed.EmployeeToken, Guid.NewGuid())).EnsureSuccessStatusCode();
        }
        var pending = await fixture.GetAsync("api/v1/table-device/session/orders/status", seed.DeviceToken); pending.EnsureSuccessStatusCode(); using var pendingJson = JsonDocument.Parse(await pending.Content.ReadAsStringAsync()); var pendingItem = pendingJson.RootElement.GetProperty("orders")[0].GetProperty("items")[0]; Assert.Equal("awaiting_delivery_confirmation", pendingItem.GetProperty("publicSubstatus").GetString()); Assert.Equal("pending", pendingItem.GetProperty("delivery").GetProperty("confirmationStatus").GetString()); Assert.Equal(1, pendingItem.GetProperty("delivery").GetProperty("sequence").GetInt32());
        var expected = pendingItem.GetProperty("version").GetInt64(); var confirm = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{seed.OrderItemId}/delivery-confirmation", new { confirmation = "received", expectedVersion = expected }, seed.DeviceToken, Guid.NewGuid()); confirm.EnsureSuccessStatusCode();
        var table = await fixture.GetAsync("api/v1/table-device/session/orders/status", seed.DeviceToken); var operations = await fixture.GetAsync("api/v1/operations/kitchen/production-items", seed.EmployeeToken); table.EnsureSuccessStatusCode(); operations.EnsureSuccessStatusCode(); Assert.Contains("delivered", await table.Content.ReadAsStringAsync()); Assert.Contains("delivered", await operations.Content.ReadAsStringAsync());
        await using var after = fixture.CreateDbContext(); Assert.Equal(1, await after.Set<DeliveryConfirmation>().CountAsync(x => x.ProductionItemId == seed.ProductionItemId)); Assert.Equal("confirmed_manual", await after.Set<DeliveryConfirmation>().Where(x => x.ProductionItemId == seed.ProductionItemId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task AutoConfirmationThenCustomerContestReconcilesBothReadModels()
    {
        var scenario = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAutoConfirmedAsync();
        var before = await fixture.GetAsync("api/v1/table-device/session/orders/status", scenario.DeviceToken); before.EnsureSuccessStatusCode(); using var beforeJson = JsonDocument.Parse(await before.Content.ReadAsStringAsync()); var beforeItem = beforeJson.RootElement.GetProperty("orders").EnumerateArray().Single(x => x.GetProperty("orderId").GetGuid() == scenario.OrderId).GetProperty("items").EnumerateArray().Single(x => x.GetProperty("itemId").GetGuid() == scenario.OrderItemId); Assert.Equal("confirmed_automatic", beforeItem.GetProperty("delivery").GetProperty("confirmationStatus").GetString()); Assert.Equal(scenario.DeliveryConfirmationId, beforeItem.GetProperty("delivery").GetProperty("confirmationId").GetGuid());
        var contest = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{scenario.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = scenario.ProductionItemVersion }, scenario.DeviceToken, Guid.NewGuid()); contest.EnsureSuccessStatusCode();
        var table = await fixture.GetAsync("api/v1/table-device/session/orders/status", scenario.DeviceToken); var operations = await fixture.GetAsync("api/v1/operations/kitchen/production-items", scenario.EstablishmentToken); table.EnsureSuccessStatusCode(); operations.EnsureSuccessStatusCode(); using var tableJson = JsonDocument.Parse(await table.Content.ReadAsStringAsync()); var item = tableJson.RootElement.GetProperty("orders").EnumerateArray().Single(x => x.GetProperty("orderId").GetGuid() == scenario.OrderId).GetProperty("items").EnumerateArray().Single(x => x.GetProperty("itemId").GetGuid() == scenario.OrderItemId); Assert.True(item.GetProperty("delivery").GetProperty("attentionRequired").GetBoolean()); using var operationsJson = JsonDocument.Parse(await operations.Content.ReadAsStringAsync()); Assert.Contains(scenario.ProductionItemId.ToString(), operationsJson.RootElement.GetRawText()); Assert.Contains("attentionRequired", operationsJson.RootElement.GetRawText());
    }
    [Fact]
    public async Task ContestConfirmDeliveredReconcilesTableAndOperationsReadModels()
    {
        var scenario = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-contests/{scenario.DeliveryContestId}/resolve", new { resolution = "confirm_delivered", expectedVersion = scenario.ProductionItemVersion }, scenario.EstablishmentToken, Guid.NewGuid());
        response.EnsureSuccessStatusCode();
        var table = await fixture.GetAsync("api/v1/table-device/session/orders/status", scenario.DeviceToken); table.EnsureSuccessStatusCode();
        var operations = await fixture.GetAsync("api/v1/operations/kitchen/production-items", scenario.EstablishmentToken); operations.EnsureSuccessStatusCode();
        using var tableJson = JsonDocument.Parse(await table.Content.ReadAsStringAsync()); using var operationsJson = JsonDocument.Parse(await operations.Content.ReadAsStringAsync());
        Assert.Contains("delivered", tableJson.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase); Assert.Contains("delivered", operationsJson.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        await using var db = fixture.CreateDbContext(); Assert.Equal("resolved_delivered", await db.Set<DeliveryContest>().Where(x => x.Id == scenario.DeliveryContestId).Select(x => x.Status).SingleAsync()); Assert.Equal("delivered", await db.Set<ProductionItem>().Where(x => x.Id == scenario.ProductionItemId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task ContestRetryThenSendCreatesCurrentSequenceTwoAcrossReadModels()
    {
        var scenario = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        var retry = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-contests/{scenario.DeliveryContestId}/resolve", new { resolution = "retry_delivery", expectedVersion = scenario.ProductionItemVersion }, scenario.EstablishmentToken, Guid.NewGuid()); retry.EnsureSuccessStatusCode();
        await using var db = fixture.CreateDbContext(); var readyVersion = await db.Set<ProductionItem>().Where(x => x.Id == scenario.ProductionItemId).Select(x => x.Version).SingleAsync();
        var send = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{scenario.ProductionItemId}/send-to-table", new { expectedVersion = readyVersion }, scenario.EstablishmentToken, Guid.NewGuid()); send.EnsureSuccessStatusCode();
        await using var after = fixture.CreateDbContext(); var confirmations = await after.Set<DeliveryConfirmation>().Where(x => x.ProductionItemId == scenario.ProductionItemId).OrderBy(x => x.SequenceNumber).ToListAsync(); Assert.Equal(2, confirmations.Count); Assert.Equal(2, confirmations[1].SequenceNumber); Assert.Equal("pending", confirmations[1].Status); Assert.Equal("superseded", confirmations[0].Status);
        var table = await fixture.GetAsync("api/v1/table-device/session/orders/status", scenario.DeviceToken); table.EnsureSuccessStatusCode(); var operations = await fixture.GetAsync("api/v1/operations/kitchen/production-items", scenario.EstablishmentToken); operations.EnsureSuccessStatusCode(); Assert.Contains(confirmations[1].Id.ToString(), await table.Content.ReadAsStringAsync()); Assert.Contains(confirmations[1].Id.ToString(), await operations.Content.ReadAsStringAsync());
    }

    private async Task<ReadySeed> SeedReadyAsync()
    {
        var tenant = await fixture.CreateTenantAsync(2, 1); var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]); var session = await fixture.OpenSessionAsync(device.AccessToken); var now = DateTimeOffset.UtcNow; var orderId = Guid.NewGuid(); var itemId = Guid.NewGuid(); var productionId = Guid.NewGuid(); var stationId = Guid.NewGuid();
        await using var db = fixture.CreateDbContext(); db.Add(new Station { Id = stationId, EstablishmentId = tenant.EstablishmentId, Name = $"E2E-{Guid.NewGuid():N}", IsDefault = true, CreatedAt = now, UpdatedAt = now }); db.Add(new Order { Id = orderId, EstablishmentId = tenant.EstablishmentId, TableSessionId = session, SourceDeviceId = device.DeviceId, ClientSubmissionId = Guid.NewGuid(), SubtotalAmount = 10, TotalAmount = 10, SubmittedAt = now, CreatedAt = now, UpdatedAt = now }); db.Add(new OrderItem { Id = itemId, OrderId = orderId, LocalCartItemId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductType = "simple", ProductName = "E2E", Quantity = 1, UnitAmount = 10, TotalAmount = 10, ConfigurationVersion = "v1", CatalogRevisionId = Guid.NewGuid(), CatalogVersion = 1, AvailabilityVersion = 1, Snapshot = "{}", CreatedAt = now, UpdatedAt = now }); db.Add(new ProductionItem { Id = productionId, EstablishmentId = tenant.EstablishmentId, OrderItemId = itemId, StationId = stationId, Status = "ready", ReadyAt = now, AcceptedAt = now, ReceivedAt = now, CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync(); return new(tenant.AccessToken, device.AccessToken, itemId, productionId);
    }
    private sealed record ReadySeed(string EmployeeToken, string DeviceToken, Guid OrderItemId, Guid ProductionItemId);
}
