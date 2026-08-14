using Appizza.Modules.Catalog;
using Appizza.Modules.Ordering;
using Appizza.Modules.Kitchen;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class NestedComboCatalogOnlyTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task NestedComboCatalogRelationSurvivesParentPublication()
    {
        var scenario = await new ComplexOrderFixtureBuilder(fixture).BuildNestedComboCatalogOnlyAsync();
        await using var db = fixture.CreateDbContext();
        var before = await db.Set<ComboGroupItem>().AsNoTracking().SingleAsync(x => x.Id == scenario.NestedGroupItemId);
        Assert.Equal(scenario.ChildComboId, before.ProductId);
        Assert.Null(before.ProductVariantId);
        var child = await db.Set<Product>().AsNoTracking().SingleAsync(x => x.Id == scenario.ChildComboId);
        var parent = await db.Set<Product>().AsNoTracking().SingleAsync(x => x.Id == scenario.ParentComboId);
        Assert.Equal("combo", child.ProductType);
        Assert.Equal("combo", parent.ProductType);
        Assert.Equal(scenario.EstablishmentId, child.EstablishmentId);
        Assert.Equal(scenario.EstablishmentId, parent.EstablishmentId);
        Assert.Equal(before.Id, await db.Set<ComboGroupItem>().Where(x => x.ComboGroupId == scenario.ParentGroupId && x.ProductId == scenario.ChildComboId).Select(x => x.Id).SingleAsync());
        var state = await db.Set<CatalogState>().SingleAsync(x => x.EstablishmentId == scenario.EstablishmentId);
        var revision = await db.Set<CatalogRevision>().SingleAsync(x => x.Id == state.CurrentPublishedRevisionId);
        Assert.Contains(scenario.ChildComboId.ToString(), revision.Snapshot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NestedComboChangeIsRejectedWithoutCommercialMutation()
    {
        var s = await new ComplexOrderFixtureBuilder(fixture).BuildNestedComboCatalogOnlyAsync();
        var device = await fixture.RegisterAndBindAsync(s.UserToken, s.TableId);
        var session = await fixture.OpenSessionAsync(device.AccessToken);
        await using (var stationDb = fixture.CreateDbContext()) { stationDb.Add(new Station { Id = Guid.NewGuid(), EstablishmentId = s.EstablishmentId, Name = "Kitchen", IsDefault = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); await stationDb.SaveChangesAsync(); }
        using var menu = await fixture.GetJsonAsync($"api/v1/table-device/menu/products/{s.ParentComboId}", device.AccessToken);
        var cfgVersion = menu.RootElement.GetProperty("configurationVersion").GetString()!;
        await using var db = fixture.CreateDbContext();
        var state = await db.Set<CatalogState>().SingleAsync(x => x.EstablishmentId == s.EstablishmentId);
        var cart = Guid.NewGuid();
        var originalConfig = new { combo = new { groups = new[] { new { groupId = s.ParentGroupId, selections = new[] { new { comboGroupItemId = s.NormalGroupItemId, quantity = 1, configuration = new { variantId = s.NormalVariantId } } } } } } };
        var sim = await fixture.PostAsync("api/v1/table-device/cart/simulate", new { sessionId = session, localCartId = cart, catalogVersion = state.CatalogVersion, availabilityVersion = state.AvailabilityVersion, items = new[] { new { localCartItemId = Guid.NewGuid(), productId = s.ParentComboId, productVariantId = s.ParentVariantId, quantity = 1, configurationVersion = cfgVersion, estimatedUnitAmount = 26m, configuration = originalConfig } } }, device.AccessToken);
        sim.EnsureSuccessStatusCode(); using var simJson = JsonDocument.Parse(await sim.Content.ReadAsStringAsync());
        var submission = await fixture.PostWithIdempotencyAsync("api/v1/table-device/orders", new { sessionId = session, localCartId = cart, clientSubmissionId = Guid.NewGuid(), simulationId = simJson.RootElement.GetProperty("simulationId").GetGuid(), simulationVersion = simJson.RootElement.GetProperty("simulationVersion").GetString(), acceptedReview = simJson.RootElement.GetProperty("requiresReview").GetBoolean() }, device.AccessToken, Guid.NewGuid());
        var submissionBody = await submission.Content.ReadAsStringAsync(); if (!submission.IsSuccessStatusCode) throw new InvalidOperationException($"Original order submission failed {(int)submission.StatusCode}: {submissionBody}; parent={s.ParentComboId}; normalOption={s.NormalGroupItemId}; normalVariant={s.NormalVariantId}; config={JsonSerializer.Serialize(originalConfig)}"); using var orderJson = JsonDocument.Parse(submissionBody); var orderId = orderJson.RootElement.GetProperty("order").GetProperty("id").GetGuid();
        await using var orderDb = fixture.CreateDbContext(); var itemId = await orderDb.Set<OrderItem>().Where(x => x.OrderId == orderId).Select(x => x.Id).SingleAsync();
        var nestedConfig = new { combo = new { groups = new[] { new { groupId = s.ParentGroupId, selections = new[] { new { comboGroupItemId = s.NestedGroupItemId, quantity = 1 } } } } } };
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/table-device/order-items/{itemId}/change-requests", new { configuration = nestedConfig, reasonCode = "CUSTOMER_CHANGE" }, device.AccessToken, Guid.NewGuid());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var code = body.RootElement.TryGetProperty("errorCode", out var ec) ? ec.GetString() : null;
        Assert.Equal("NESTED_COMBO_NOT_ALLOWED", code);
        await using var after = fixture.CreateDbContext(); Assert.Empty(await after.Set<OrderItemRevision>().Where(x => x.OrderItemId == itemId).ToListAsync());
    }
}
