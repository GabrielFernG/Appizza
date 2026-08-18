using System.Text.Json;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

internal sealed class Phase5OrderScenarioBuilder(Phase1ApiFixture fixture)
{
    internal async Task<Phase5OrderScenario> BuildSimpleAsync()
    {
        var tenant = await fixture.CreateTenantAsync(2, 1);
        var productResponse = await fixture.PostAsync("api/v1/operations/catalog/products", new { productType = "simple", name = "Phase5 simple", internalCode = Guid.NewGuid().ToString("N"), displayOrder = 1, requiresProduction = false, allowsNotes = false }, tenant.AccessToken);
        productResponse.EnsureSuccessStatusCode(); using var productJson = JsonDocument.Parse(await productResponse.Content.ReadAsStringAsync()); var productId = productJson.RootElement.GetProperty("id").GetGuid();
        var variantResponse = await fixture.PostAsync($"api/v1/operations/catalog/products/{productId}/variants", new { name = "Standard", internalCode = Guid.NewGuid().ToString("N"), basePrice = 12.50m, displayOrder = 1 }, tenant.AccessToken);
        variantResponse.EnsureSuccessStatusCode(); using var variantJson = JsonDocument.Parse(await variantResponse.Content.ReadAsStringAsync()); var variantId = variantJson.RootElement.GetProperty("id").GetGuid(); var cheaperResponse = await fixture.PostAsync($"api/v1/operations/catalog/products/{productId}/variants", new { name = "Economica", internalCode = Guid.NewGuid().ToString("N"), basePrice = 10m, displayOrder = 2 }, tenant.AccessToken); cheaperResponse.EnsureSuccessStatusCode(); using var cheaperJson = JsonDocument.Parse(await cheaperResponse.Content.ReadAsStringAsync()); var secondVariantId = cheaperJson.RootElement.GetProperty("id").GetGuid();
        (await fixture.PostAsync("api/v1/operations/catalog/publish", null, tenant.AccessToken, true)).EnsureSuccessStatusCode();
        Guid stationId; await using (var db = fixture.CreateDbContext()) { stationId = Guid.NewGuid(); db.Add(new Station { Id = stationId, EstablishmentId = tenant.EstablishmentId, Name = "Phase5 Kitchen", IsDefault = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); }
        var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]); var sessionId = await fixture.OpenSessionAsync(device.AccessToken);
        using var detail = await fixture.GetJsonAsync($"api/v1/table-device/menu/products/{productId}", device.AccessToken); var configurationVersion = detail.RootElement.GetProperty("configurationVersion").GetString()!;
        var configuration = new { productVariantId = variantId }; var cart = Guid.NewGuid(); var simulation = await fixture.PostAsync("api/v1/table-device/cart/simulate", new { sessionId, localCartId = cart, catalogVersion = 1, availabilityVersion = 0, items = new[] { new { localCartItemId = Guid.NewGuid(), productId, productVariantId = variantId, quantity = 1, configurationVersion, estimatedUnitAmount = 12.50m, configuration } } }, device.AccessToken); simulation.EnsureSuccessStatusCode(); using var sim = JsonDocument.Parse(await simulation.Content.ReadAsStringAsync());
        var submit = await fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", new { sessionId, localCartId = cart, clientSubmissionId = Guid.NewGuid(), simulationId = sim.RootElement.GetProperty("simulationId").GetGuid(), simulationVersion = sim.RootElement.GetProperty("simulationVersion").GetString(), acceptedReview = sim.RootElement.GetProperty("requiresReview").GetBoolean() }, device.AccessToken, Guid.NewGuid()); submit.EnsureSuccessStatusCode(); using var submitted = JsonDocument.Parse(await submit.Content.ReadAsStringAsync()); var orderId = submitted.RootElement.GetProperty("order").GetProperty("id").GetGuid();
        await using var orderDb = fixture.CreateDbContext(); var item = await orderDb.Set<OrderItem>().SingleAsync(x => x.OrderId == orderId);
        return new(tenant.EstablishmentId, device.DeviceId, device.AccessToken, sessionId, productId, variantId, secondVariantId, orderId, item.Id, stationId, null, null, 12.50m, configuration);
    }
}

internal sealed record Phase5OrderScenario(Guid EstablishmentId, Guid DeviceId, string DeviceToken, Guid SessionId, Guid ProductId, Guid VariantId, Guid SecondVariantId, Guid OrderId, Guid OrderItemId, Guid StationId, Guid? ProductionItemId, Guid? ProductionAttemptId, decimal OriginalUnitAmount, object OriginalConfiguration);
