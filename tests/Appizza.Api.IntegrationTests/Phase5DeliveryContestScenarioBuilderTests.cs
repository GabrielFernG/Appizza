using Appizza.Modules.Kitchen;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5DeliveryContestScenarioBuilderTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task BuildProducesOpenContestScenarioWithoutGoingBeyondContest()
    {
        var scenario = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();

        await using var db = fixture.CreateDbContext();
        var production = await db.Set<ProductionItem>().SingleAsync(x => x.Id == scenario.ProductionItemId);
        var confirmations = await db.Set<DeliveryConfirmation>().Where(x => x.ProductionItemId == scenario.ProductionItemId).ToListAsync();
        var confirmation = confirmations.Single(x => x.Id == scenario.DeliveryConfirmationId);
        var contests = await db.Set<DeliveryContest>().Where(x => x.ProductionItemId == scenario.ProductionItemId).ToListAsync();
        var contest = contests.Single(x => x.Id == scenario.DeliveryContestId);

        Assert.Equal("awaiting_delivery_confirmation", production.Status);
        Assert.Equal(production.Version, scenario.ProductionItemVersion);
        Assert.Single(confirmations);
        Assert.Equal("contested", confirmation.Status);
        Assert.Equal(1, confirmation.SequenceNumber);
        Assert.Equal(confirmation.Version, scenario.DeliveryConfirmationVersion);
        Assert.Single(contests);
        Assert.Equal("open", contest.Status);
        Assert.Equal(contest.Version, scenario.DeliveryContestVersion);
        Assert.Empty(await db.Set<DeliveryConfirmation>().Where(x => x.ProductionItemId == scenario.ProductionItemId && x.SequenceNumber > 1).ToListAsync());
        Assert.Empty(await db.OutboxMessages.Where(x => x.EstablishmentId == scenario.EstablishmentId && x.EventType == "delivery-contest-resolved.v1").ToListAsync());
    }
}
