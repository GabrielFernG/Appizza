using Appizza.Modules.Establishments;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

public sealed record Phase5DeliveryContestScenario(
    Guid EstablishmentId,
    string EstablishmentToken,
    string DeviceToken,
    Guid OrderId,
    Guid OrderItemId,
    Guid ProductionItemId,
    long ProductionItemVersion,
    Guid DeliveryConfirmationId,
    long DeliveryConfirmationVersion,
    int DeliveryConfirmationSequence,
    Guid DeliveryContestId,
    long DeliveryContestVersion);
public sealed record Phase5DeliveryAutoScenario(Guid EstablishmentId, string EstablishmentToken, string DeviceToken, Guid OrderId, Guid OrderItemId, Guid ProductionItemId, long ProductionItemVersion, Guid DeliveryConfirmationId, long DeliveryConfirmationVersion, int DeliveryConfirmationSequence);

public sealed class Phase5DeliveryContestScenarioBuilder(Phase1ApiFixture fixture)
{
    public async Task<Phase5DeliveryContestScenario> BuildAsync()
    {
        var automatic = await BuildAutoConfirmedAsync();
        var baseScenario = new BaseScenario(automatic.EstablishmentId, automatic.EstablishmentToken, automatic.DeviceToken, automatic.OrderId, automatic.OrderItemId, automatic.ProductionItemId);
        var confirmationId = automatic.DeliveryConfirmationId;
        return await ContestAsync(baseScenario, confirmationId);
    }

    public async Task<Phase5DeliveryAutoScenario> BuildAutoConfirmedAsync()
    {
        var baseScenario = await SeedBaseAsync();
        var confirmationId = await SendToTableAsync(baseScenario);
        await AutoConfirmAsync(confirmationId);
        await using var db = fixture.CreateDbContext();
        var production = await db.Set<ProductionItem>().SingleAsync(x => x.Id == baseScenario.ProductionItemId);
        var confirmation = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == confirmationId);
        return new(baseScenario.EstablishmentId, baseScenario.EstablishmentToken, baseScenario.DeviceToken, baseScenario.OrderId, baseScenario.OrderItemId, baseScenario.ProductionItemId, production.Version, confirmation.Id, confirmation.Version, confirmation.SequenceNumber);
    }

    private async Task<BaseScenario> SeedBaseAsync()
    {
        var tenant = await fixture.CreateTenantAsync(2, 1);
        var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]);
        var session = await fixture.OpenSessionAsync(device.AccessToken);
        var now = DateTimeOffset.UtcNow;
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var productionId = Guid.NewGuid();
        var stationId = Guid.NewGuid();

        await using var db = fixture.CreateDbContext();
        db.Add(new Station { Id = stationId, EstablishmentId = tenant.EstablishmentId, Name = $"F5-{Guid.NewGuid():N}", IsDefault = true, CreatedAt = now, UpdatedAt = now });
        db.Add(new Order { Id = orderId, EstablishmentId = tenant.EstablishmentId, TableSessionId = session, SourceDeviceId = device.DeviceId, ClientSubmissionId = Guid.NewGuid(), SubtotalAmount = 10, TotalAmount = 10, SubmittedAt = now, CreatedAt = now, UpdatedAt = now });
        db.Add(new OrderItem { Id = itemId, OrderId = orderId, LocalCartItemId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductType = "simple", ProductName = "F5", Quantity = 1, UnitAmount = 10, TotalAmount = 10, ConfigurationVersion = "v1", CatalogRevisionId = Guid.NewGuid(), CatalogVersion = 1, AvailabilityVersion = 1, Snapshot = "{}", CreatedAt = now, UpdatedAt = now });
        db.Add(new ProductionItem { Id = productionId, EstablishmentId = tenant.EstablishmentId, OrderItemId = itemId, StationId = stationId, Status = "ready", ReadyAt = now, AcceptedAt = now, ReceivedAt = now, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        return new(tenant.EstablishmentId, tenant.AccessToken, device.AccessToken, orderId, itemId, productionId);
    }

    private async Task<Guid> SendToTableAsync(BaseScenario scenario)
    {
        await using var db = fixture.CreateDbContext();
        var version = await db.Set<ProductionItem>().Where(x => x.Id == scenario.ProductionItemId).Select(x => x.Version).SingleAsync();
        (await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{scenario.ProductionItemId}/send-to-table", new { expectedVersion = version }, scenario.EstablishmentToken, Guid.NewGuid())).EnsureSuccessStatusCode();
        await using var after = fixture.CreateDbContext();
        return (await after.Set<DeliveryConfirmation>().SingleAsync(x => x.ProductionItemId == scenario.ProductionItemId)).Id;
    }

    private async Task AutoConfirmAsync(Guid confirmationId)
    {
        await using var db = fixture.CreateDbContext();
        var confirmation = await db.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == confirmationId);
        confirmation.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        _ = await fixture.CreateDeliveryAutoConfirmationWorker().ProcessOnceAsync();
    }

    private async Task<Phase5DeliveryContestScenario> ContestAsync(BaseScenario scenario, Guid confirmationId)
    {
        await using var db = fixture.CreateDbContext();
        var piVersion = await db.Set<ProductionItem>().Where(x => x.Id == scenario.ProductionItemId).Select(x => x.Version).SingleAsync();
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{scenario.OrderItemId}/delivery-contestation", new { reasonCode = "NOT_RECEIVED", expectedVersion = piVersion }, scenario.DeviceToken, Guid.NewGuid());
        response.EnsureSuccessStatusCode();

        await using var after = fixture.CreateDbContext();
        var confirmation = await after.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == confirmationId);
        var contest = await after.Set<DeliveryContest>().SingleAsync(x => x.ProductionItemId == scenario.ProductionItemId);
        var production = await after.Set<ProductionItem>().SingleAsync(x => x.Id == scenario.ProductionItemId);
        return new(scenario.EstablishmentId, scenario.EstablishmentToken, scenario.DeviceToken, scenario.OrderId, scenario.OrderItemId, scenario.ProductionItemId, production.Version, confirmation.Id, confirmation.Version, confirmation.SequenceNumber, contest.Id, contest.Version);
    }

    private sealed record BaseScenario(Guid EstablishmentId, string EstablishmentToken, string DeviceToken, Guid OrderId, Guid OrderItemId, Guid ProductionItemId);
}
