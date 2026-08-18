using System.Net;
using Appizza.Modules.Identity;
using Appizza.Modules.Kitchen;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5DeliveryRetrySendTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task SendBeforeRetryIsRejectedAndRetryCompletes()
    {
        var s = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        var user = await ResolveUser(s.EstablishmentId);
        fixture.DeliveryHook.Reset();
        fixture.DeliveryHook.BlockNext("resolve-before-locks", s.DeliveryContestId, "retry_delivery");
        try
        {
            var retry = Resolve(s, user, "retry_delivery");
            await fixture.DeliveryHook.WaitUntilReachedAsync("resolve-before-locks", s.DeliveryContestId, "retry_delivery");
            var send = await Send(s, s.ProductionItemVersion, Guid.NewGuid());
            Assert.NotEqual(HttpStatusCode.InternalServerError, send.StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, send.StatusCode);
            await using (var db = fixture.CreateDbContext())
            {
                Assert.Equal("contested", await db.Set<DeliveryConfirmation>().Where(x => x.Id == s.DeliveryConfirmationId).Select(x => x.Status).SingleAsync());
                Assert.Equal(1, await db.Set<DeliveryConfirmation>().CountAsync(x => x.ProductionItemId == s.ProductionItemId));
            }
            fixture.DeliveryHook.Release("resolve-before-locks", s.DeliveryContestId, "retry_delivery");
            (await retry).EnsureSuccessStatusCode();
            await using var after = fixture.CreateDbContext();
            Assert.Equal("resolved_retry", await after.Set<DeliveryContest>().Where(x => x.Id == s.DeliveryContestId).Select(x => x.Status).SingleAsync());
            Assert.Equal("superseded", await after.Set<DeliveryConfirmation>().Where(x => x.Id == s.DeliveryConfirmationId).Select(x => x.Status).SingleAsync());
            Assert.Equal("ready", await after.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync());
            Assert.Equal(1, await after.Set<DeliveryConfirmation>().CountAsync(x => x.ProductionItemId == s.ProductionItemId));
        }
        finally { fixture.DeliveryHook.Release("resolve-before-locks", s.DeliveryContestId, "retry_delivery"); fixture.DeliveryHook.Reset(); }
    }

    [Fact]
    public async Task RetryThenSendCreatesSequenceTwo()
    {
        var s = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        var user = await ResolveUser(s.EstablishmentId);
        await using var attemptsBeforeDb = fixture.CreateDbContext();
        var attemptsBefore = await attemptsBeforeDb.Set<Appizza.Modules.Kitchen.ProductionAttempt>().Where(x => x.ProductionItemId == s.ProductionItemId).Select(x => x.Id).ToListAsync();
        (await Resolve(s, user, "retry_delivery")).EnsureSuccessStatusCode();
        await using (var db = fixture.CreateDbContext()) { Assert.Equal("ready", await db.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Status).SingleAsync()); }
        await using var readyDb = fixture.CreateDbContext();
        var readyVersion = await readyDb.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Version).SingleAsync();
        var send = await Send(s, readyVersion, Guid.NewGuid());
        send.EnsureSuccessStatusCode();
        await using var after = fixture.CreateDbContext();
        var confirmations = await after.Set<DeliveryConfirmation>().Where(x => x.ProductionItemId == s.ProductionItemId).OrderBy(x => x.SequenceNumber).ToListAsync();
        Assert.Equal(2, confirmations.Count); Assert.Equal((1, "superseded"), (confirmations[0].SequenceNumber, confirmations[0].Status)); Assert.Equal((2, "pending"), (confirmations[1].SequenceNumber, confirmations[1].Status)); Assert.NotEqual(confirmations[0].Id, confirmations[1].Id);
        Assert.Equal(attemptsBefore, await after.Set<Appizza.Modules.Kitchen.ProductionAttempt>().Where(x => x.ProductionItemId == s.ProductionItemId).Select(x => x.Id).ToListAsync());
    }

    [Fact]
    public async Task TwoSendsAfterRetryCreateOnlySequenceTwo()
    {
        var s = await new Phase5DeliveryContestScenarioBuilder(fixture).BuildAsync();
        var user = await ResolveUser(s.EstablishmentId);
        (await Resolve(s, user, "retry_delivery")).EnsureSuccessStatusCode();
        await using var before = fixture.CreateDbContext();
        var readyVersion = await before.Set<ProductionItem>().Where(x => x.Id == s.ProductionItemId).Select(x => x.Version).SingleAsync();
        var f1EventsBefore = await before.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && (x.EventType == "production-item-sent-to-table.v1" || x.EventType == "delivery-confirmation-requested.v1"));
        fixture.DeliveryHook.Reset(); fixture.DeliveryHook.BlockNext("send-before-locks", s.ProductionItemId, "send_to_table");
        try
        {
            var first = Send(s, readyVersion, Guid.NewGuid());
            await fixture.DeliveryHook.WaitUntilReachedAsync("send-before-locks", s.ProductionItemId, "send_to_table");
            var second = Send(s, readyVersion, Guid.NewGuid());
            fixture.DeliveryHook.Release("send-before-locks", s.ProductionItemId, "send_to_table");
            var responses = new[] { await first, await second };
            Assert.Single(responses, x => x.IsSuccessStatusCode);
            Assert.DoesNotContain(responses, x => x.StatusCode == HttpStatusCode.InternalServerError);
            await using var db = fixture.CreateDbContext();
            var confirmations = await db.Set<DeliveryConfirmation>().Where(x => x.ProductionItemId == s.ProductionItemId).OrderBy(x => x.SequenceNumber).ToListAsync();
            Assert.Equal(2, confirmations.Count); Assert.Equal(1, confirmations[0].SequenceNumber); Assert.Equal(2, confirmations[1].SequenceNumber); Assert.Equal("superseded", confirmations[0].Status); Assert.Equal("pending", confirmations[1].Status);
            var f1EventsAfter = await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.EstablishmentId && (x.EventType == "production-item-sent-to-table.v1" || x.EventType == "delivery-confirmation-requested.v1"));
            Assert.Equal(2, f1EventsAfter - f1EventsBefore);
        }
        finally { fixture.DeliveryHook.Release("send-before-locks", s.ProductionItemId, "send_to_table"); fixture.DeliveryHook.Reset(); }
    }

    private async Task<string> ResolveUser(Guid establishmentId)
    {
        await using var db = fixture.CreateDbContext();
        var permission = await db.Set<Permission>().SingleOrDefaultAsync(x => x.Code == "kitchen.delivery.resolve");
        if (permission is null) { permission = new Permission { Id = Guid.NewGuid(), Code = "kitchen.delivery.resolve", Module = "kitchen", Name = "kitchen.delivery.resolve" }; db.Add(permission); await db.SaveChangesAsync(); }
        return await fixture.CreateUserTokenAsync(establishmentId, "kitchen.delivery.resolve", "kitchen.delivery.send");
    }

    private Task<HttpResponseMessage> Resolve(Phase5DeliveryContestScenario s, string token, string resolution) => fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/delivery-contests/{s.DeliveryContestId}/resolve", new { resolution, expectedVersion = s.ProductionItemVersion }, token, Guid.NewGuid());
    private Task<HttpResponseMessage> Send(Phase5DeliveryContestScenario s, long version, Guid key) => fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{s.ProductionItemId}/send-to-table", new { expectedVersion = version }, s.EstablishmentToken, key);
}
