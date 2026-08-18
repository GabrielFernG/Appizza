using System.Net;
using Appizza.Modules.Establishments;
using Appizza.Modules.Identity;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Persistence;
using Appizza.Worker;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5DeliveryContestResolutionApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task ConfirmDeliveredResolvesContestAndKeepsConfirmationHistory()
    {
        var s = await Seed(); var user = await ResolveUser(s.EstablishmentId); var response = await Resolve(s, user, "confirm_delivered"); response.EnsureSuccessStatusCode(); await using var db = fixture.CreateDbContext(); Assert.Equal("resolved_delivered", await db.Set<DeliveryContest>().Where(x => x.Id == s.ContestId).Select(x => x.Status).SingleAsync()); Assert.Equal("contested", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync()); Assert.Equal("delivered", await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task RetryDeliverySupersedesAndNextSendCreatesSequenceTwo()
    {
        var s = await Seed(); var user = await ResolveUser(s.EstablishmentId); var response = await Resolve(s, user, "retry_delivery"); response.EnsureSuccessStatusCode(); await using var db = fixture.CreateDbContext(); Assert.Equal("resolved_retry", await db.Set<DeliveryContest>().Where(x => x.Id == s.ContestId).Select(x => x.Status).SingleAsync()); Assert.Equal("superseded", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync()); Assert.Equal("ready", await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync()); var version = await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Version).SingleAsync(); var send = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{s.ProductionItemId}/send-to-table", new { expectedVersion = version }, user, Guid.NewGuid()); send.EnsureSuccessStatusCode(); await using var after = fixture.CreateDbContext(); Assert.Equal(2, await after.Set<DeliveryConfirmation>().Where(x => x.ProductionItemId == s.ProductionItemId).Select(x => x.SequenceNumber).MaxAsync());
    }

    [Fact]
    public async Task ResolveReplayAndPermissionAreSafe()
    {
        var s = await Seed(); var user = await ResolveUser(s.EstablishmentId); var key = Guid.NewGuid(); var body = new { resolution = "confirm_delivered", expectedVersion = s.Version }; var first = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-contests/{s.ContestId}/resolve", body, user, key); first.EnsureSuccessStatusCode(); var replay = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-contests/{s.ContestId}/resolve", body, user, key); replay.EnsureSuccessStatusCode(); var divergent = await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-contests/{s.ContestId}/resolve", new { resolution = "retry_delivery", expectedVersion = s.Version }, user, key); Assert.Equal(HttpStatusCode.Conflict, divergent.StatusCode); Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", await fixture.ErrorCodeAsync(divergent)); await using var db = fixture.CreateDbContext(); Assert.Equal(1, await db.Set<DeliveryContest>().CountAsync(x => x.Id == s.ContestId)); Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-contest-resolved.v1"));
    }

    [Fact]
    public async Task ConfirmDeliveredRaceProducesOneTerminalResolution()
    {
        var s = await Seed();
        var user = await ResolveUser(s.EstablishmentId);
        fixture.DeliveryHook.Reset();
        fixture.DeliveryHook.BlockNext("resolve-before-locks", s.ContestId, "confirm_delivered");
        var a = ResolveWithKey(s, user, "confirm_delivered", Guid.NewGuid());
        await fixture.DeliveryHook.WaitUntilReachedAsync("resolve-before-locks", s.ContestId, "confirm_delivered");
        var b = ResolveWithKey(s, user, "confirm_delivered", Guid.NewGuid());
        await fixture.DeliveryHook.WaitUntilReachedAsync("resolve-before-locks", s.ContestId, "confirm_delivered");
        fixture.DeliveryHook.Release("resolve-before-locks", s.ContestId, "confirm_delivered");
        var responses = await Task.WhenAll(a, b);
        Assert.Contains(responses, x => x.IsSuccessStatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal("resolved_delivered", await db.Set<DeliveryContest>().Where(x => x.Id == s.ContestId).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-contest-resolved.v1"));
    }

    [Fact]
    public async Task RetryDeliveryRaceProducesOneResolutionAndSupersedesConfirmation()
    {
        var s = await Seed(); var user = await ResolveUser(s.EstablishmentId);
        fixture.DeliveryHook.Reset(); fixture.DeliveryHook.BlockNext("resolve-before-locks", s.ContestId, "retry_delivery");
        var a = ResolveWithKey(s, user, "retry_delivery", Guid.NewGuid());
        await fixture.DeliveryHook.WaitUntilReachedAsync("resolve-before-locks", s.ContestId, "retry_delivery");
        var b = ResolveWithKey(s, user, "retry_delivery", Guid.NewGuid());
        await fixture.DeliveryHook.WaitUntilReachedAsync("resolve-before-locks", s.ContestId, "retry_delivery");
        fixture.DeliveryHook.Release("resolve-before-locks", s.ContestId, "retry_delivery"); await Task.WhenAll(a, b);
        await using var db = fixture.CreateDbContext();
        Assert.Equal("resolved_retry", await db.Set<DeliveryContest>().Where(x => x.Id == s.ContestId).Select(x => x.Status).SingleAsync());
        Assert.Equal("superseded", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.ConfirmationId).Select(x => x.Status).SingleAsync());
        Assert.Equal("ready", await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && x.EventType == "delivery-contest-resolved.v1"));
    }

    [Fact]
    public async Task ConfirmWinsRetryOperationAwareRace()
    {
        var s = await Seed(); var user = await ResolveUser(s.EstablishmentId);
        fixture.DeliveryHook.Reset(); fixture.DeliveryHook.BlockNext("resolve-before-locks", s.ContestId, "retry_delivery");
        var retry = ResolveWithKey(s, user, "retry_delivery", Guid.NewGuid());
        await fixture.DeliveryHook.WaitUntilReachedAsync("resolve-before-locks", s.ContestId, "retry_delivery");
        var confirm = await Resolve(s, user, "confirm_delivered"); confirm.EnsureSuccessStatusCode();
        fixture.DeliveryHook.Release("resolve-before-locks", s.ContestId, "retry_delivery");
        Assert.NotEqual(HttpStatusCode.InternalServerError, (await retry).StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal("resolved_delivered", await db.Set<DeliveryContest>().Where(x => x.Id == s.ContestId).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, fixture.DeliveryHook.GetInvocationCount("resolve-before-locks", s.ContestId, "retry_delivery"));
        Assert.True(fixture.DeliveryHook.GetInvocationCount("resolve-before-locks", s.ContestId, "confirm_delivered") >= 1);
    }

    [Fact]
    public async Task RetryWinsConfirmOperationAwareRace()
    {
        var s = await Seed(); var user = await ResolveUser(s.EstablishmentId);
        fixture.DeliveryHook.Reset(); fixture.DeliveryHook.BlockNext("resolve-before-locks", s.ContestId, "confirm_delivered");
        var confirm = ResolveWithKey(s, user, "confirm_delivered", Guid.NewGuid());
        await fixture.DeliveryHook.WaitUntilReachedAsync("resolve-before-locks", s.ContestId, "confirm_delivered");
        var retry = await Resolve(s, user, "retry_delivery"); retry.EnsureSuccessStatusCode();
        fixture.DeliveryHook.Release("resolve-before-locks", s.ContestId, "confirm_delivered");
        Assert.NotEqual(HttpStatusCode.InternalServerError, (await confirm).StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal("resolved_retry", await db.Set<DeliveryContest>().Where(x => x.Id == s.ContestId).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, fixture.DeliveryHook.GetInvocationCount("resolve-before-locks", s.ContestId, "confirm_delivered"));
        Assert.True(fixture.DeliveryHook.GetInvocationCount("resolve-before-locks", s.ContestId, "retry_delivery") >= 1);
    }

    private async Task<string> ResolveUser(Guid establishmentId)
    {
        await using var db = fixture.CreateDbContext();
        var permission = await db.Set<Permission>().SingleOrDefaultAsync(x => x.Code == "kitchen.delivery.resolve");
        if (permission is null)
        {
            permission = new Permission { Id = Guid.NewGuid(), Code = "kitchen.delivery.resolve", Module = "kitchen", Name = "kitchen.delivery.resolve" };
            db.Add(permission);
            await db.SaveChangesAsync();
        }

        return await fixture.CreateUserTokenAsync(establishmentId, "kitchen.delivery.resolve", "kitchen.delivery.send");
    }

    private async Task<HttpResponseMessage> Resolve(Seeded s, string token, string resolution) => await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-contests/{s.ContestId}/resolve", new { resolution, expectedVersion = s.Version }, token, Guid.NewGuid());
    private Task<HttpResponseMessage> ResolveWithKey(Seeded s, string token, string resolution, Guid key) => fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-contests/{s.ContestId}/resolve", new { resolution, expectedVersion = s.Version }, token, key);
    private async Task<Seeded> Seed()
    {
        var scenario = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        return new(scenario.EstablishmentId, scenario.OrderId, scenario.OrderItemId, scenario.ProductionItemId, scenario.DeliveryConfirmationId, scenario.DeliveryContestId, scenario.ProductionItemVersion);
    }
    private sealed record Seeded(Guid EstablishmentId, Guid OrderId, Guid OrderItemId, Guid ProductionItemId, Guid ConfirmationId, Guid ContestId, long Version);
}
