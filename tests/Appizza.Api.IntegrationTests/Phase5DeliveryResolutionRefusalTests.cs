using System.Net;
using Appizza.Modules.Identity;
using Appizza.Modules.Kitchen;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5DeliveryResolutionRefusalTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task CrossTenantCannotResolveForeignContest()
    {
        var foreign = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        var tenantA = await fixture.CreateTenantAsync(1, 1);
        var token = await ResolveToken(tenantA.EstablishmentId);
        var response = await Resolve(foreign, token, "confirm_delivered", foreign.ProductionItemVersion);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        await AssertUnchanged(foreign);
    }

    [Theory]
    [InlineData(false, "kitchen.production.view")]
    [InlineData(false, "kitchen.delivery.send")]
    public async Task MissingOrIrrelevantPermissionCannotResolve(bool _, string permission)
    {
        var s = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        var token = await fixture.CreateUserTokenAsync(s.EstablishmentId, permission);
        var response = await Resolve(s, token, "confirm_delivered", s.ProductionItemVersion);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("INSUFFICIENT_PERMISSION", await fixture.ErrorCodeAsync(response));
        await AssertUnchanged(s);
    }

    [Fact]
    public async Task ResolvedDeliveredContestCannotBeResolvedAgain()
    {
        var s = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        var token = await ResolveToken(s.EstablishmentId);
        (await Resolve(s, token, "confirm_delivered", s.ProductionItemVersion)).EnsureSuccessStatusCode();
        await using var db = fixture.CreateDbContext();
        var version = await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Version).SingleAsync();
        var response = await Resolve(s, token, "confirm_delivered", version);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("resolved_delivered", await db.Set<DeliveryContest>().Where(x => x.Id == s.DeliveryContestId).Select(x => x.Status).SingleAsync());
        Assert.Equal("delivered", await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task ResolvedRetryContestCannotBeResolvedAgain()
    {
        var s = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        var token = await ResolveToken(s.EstablishmentId);
        (await Resolve(s, token, "retry_delivery", s.ProductionItemVersion)).EnsureSuccessStatusCode();
        await using var db = fixture.CreateDbContext();
        var version = await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Version).SingleAsync();
        var response = await Resolve(s, token, "retry_delivery", version);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("resolved_retry", await db.Set<DeliveryContest>().Where(x => x.Id == s.DeliveryContestId).Select(x => x.Status).SingleAsync());
        Assert.Equal("ready", await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task StaleExpectedVersionDoesNotMutateContest()
    {
        var s = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        var token = await ResolveToken(s.EstablishmentId);
        var response = await Resolve(s, token, "confirm_delivered", s.ProductionItemVersion - 1);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("CONCURRENCY_CONFLICT", await fixture.ErrorCodeAsync(response));
        await AssertUnchanged(s);
    }

    [Fact]
    public async Task IncompatibleConfirmationCannotBeResolved()
    {
        var s = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        await using (var mutate = fixture.CreateDbContext())
        {
            var confirmation = await mutate.Set<DeliveryConfirmation>().SingleAsync(x => x.Id == s.DeliveryConfirmationId);
            confirmation.Status = "confirmed_automatic";
            await mutate.SaveChangesAsync();
        }
        var token = await ResolveToken(s.EstablishmentId);
        var response = await Resolve(s, token, "confirm_delivered", s.ProductionItemVersion);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal("open", await db.Set<DeliveryContest>().Where(x => x.Id == s.DeliveryContestId).Select(x => x.Status).SingleAsync());
        Assert.Equal("confirmed_automatic", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.DeliveryConfirmationId).Select(x => x.Status).SingleAsync());
        Assert.Equal("awaiting_delivery_confirmation", await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync());
        Assert.Empty(await db.OutboxMessages.Where(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-contest-resolved.v1").ToListAsync());
    }

    [Fact]
    public async Task IncompatibleProductionItemCannotBeResolved()
    {
        var s = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        await using (var mutate = fixture.CreateDbContext())
        {
            var production = await mutate.Set<ProductionItem>().SingleAsync(x => x.Id == s.ProductionItemId);
            production.Status = "ready";
            await mutate.SaveChangesAsync();
        }
        var token = await ResolveToken(s.EstablishmentId);
        var response = await Resolve(s, token, "confirm_delivered", s.ProductionItemVersion);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal("open", await db.Set<DeliveryContest>().Where(x => x.Id == s.DeliveryContestId).Select(x => x.Status).SingleAsync());
        Assert.Equal("contested", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.DeliveryConfirmationId).Select(x => x.Status).SingleAsync());
        Assert.Equal("ready", await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync());
        Assert.Empty(await db.OutboxMessages.Where(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-contest-resolved.v1").ToListAsync());
    }

    private async Task<string> ResolveToken(Guid establishmentId)
    {
        await using var db = fixture.CreateDbContext();
        var permission = await db.Set<Permission>().SingleOrDefaultAsync(x => x.Code == "kitchen.delivery.resolve");
        if (permission is null) { permission = new Permission { Id = Guid.NewGuid(), Code = "kitchen.delivery.resolve", Module = "kitchen", Name = "kitchen.delivery.resolve" }; db.Add(permission); await db.SaveChangesAsync(); }
        return await fixture.CreateUserTokenAsync(establishmentId, "kitchen.delivery.resolve");
    }

    private Task<HttpResponseMessage> Resolve(Phase5DeliveryContestScenario s, string token, string resolution, long version) => fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-contests/{s.DeliveryContestId}/resolve", new { resolution, expectedVersion = version }, token, Guid.NewGuid());

    private async Task AssertUnchanged(Phase5DeliveryContestScenario s)
    {
        await using var db = fixture.CreateDbContext();
        Assert.Equal("open", await db.Set<DeliveryContest>().Where(x => x.Id == s.DeliveryContestId).Select(x => x.Status).SingleAsync());
        Assert.Equal(s.DeliveryContestVersion, await db.Set<DeliveryContest>().Where(x => x.Id == s.DeliveryContestId).Select(x => x.Version).SingleAsync());
        Assert.Equal("contested", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.DeliveryConfirmationId).Select(x => x.Status).SingleAsync());
        Assert.Equal(s.DeliveryConfirmationVersion, await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.DeliveryConfirmationId).Select(x => x.Version).SingleAsync());
        Assert.Equal("awaiting_delivery_confirmation", await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync());
        Assert.Equal(s.ProductionItemVersion, await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Version).SingleAsync());
    }
}
