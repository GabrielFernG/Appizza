using Appizza.Modules.Ordering;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5ChangeScenarioBuilderTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task BuildsPendingOrderItemChangedScenario()
    {
        var order = await new Phase5OrderScenarioBuilder(fixture).BuildSimpleAsync(); var change = await new Phase5ChangeScenarioBuilder(fixture).BuildAsync(order);
        await using var db = fixture.CreateDbContext(); var evt = await db.OutboxMessages.SingleAsync(x => x.Id == change.OrderItemChangedEventId); Assert.Null(evt.ProcessedAt); Assert.Equal(1, change.RevisionNumber); Assert.NotEqual(0m, change.PriceDifference); Assert.Empty(await db.InboxMessages.Where(x => x.EventId == change.OrderItemChangedEventId && x.ConsumerName == "kitchen-item-change-v1").ToListAsync()); Assert.Empty(await db.InboxMessages.Where(x => x.EventId == change.OrderItemChangedEventId && x.ConsumerName == "ordering-signalr-v1").ToListAsync()); Assert.Equal(1, await db.Set<OrderItemRevision>().CountAsync(x => x.OrderItemId == order.OrderItemId && x.SourceRequestId == change.ChangeRequestId));
    }
}
