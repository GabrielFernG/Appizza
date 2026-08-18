using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Modules.Tables;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5OrderScenarioBuilderTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task BuildsSimpleOrderScenarioThroughPublicApis()
    {
        var scenario = await new Phase5OrderScenarioBuilder(fixture).BuildSimpleAsync();
        await using var db = fixture.CreateDbContext();
        var order = await db.Set<Order>().SingleAsync(x => x.Id == scenario.OrderId && x.EstablishmentId == scenario.EstablishmentId);
        var item = await db.Set<OrderItem>().SingleAsync(x => x.Id == scenario.OrderItemId && x.OrderId == order.Id);
        var session = await db.Set<TableSession>().SingleAsync(x => x.Id == scenario.SessionId && x.EstablishmentId == scenario.EstablishmentId);
        Assert.Equal(scenario.ProductId, item.ProductId); Assert.Equal(scenario.VariantId, item.ProductVariantId); Assert.Equal(scenario.OriginalUnitAmount, item.UnitAmount); Assert.Equal(0, item.CurrentRevisionNumber); Assert.Equal(order.TotalAmount, session.TotalAmount);
        Assert.True(await db.Set<Station>().AnyAsync(x => x.Id == scenario.StationId && x.EstablishmentId == scenario.EstablishmentId));
        Assert.Empty(await db.Set<OrderItemRevision>().Where(x => x.OrderItemId == item.Id).ToListAsync());
        Assert.Empty(await db.Set<OrderItemRequest>().Where(x => x.OrderItemId == item.Id).ToListAsync());
    }
}
