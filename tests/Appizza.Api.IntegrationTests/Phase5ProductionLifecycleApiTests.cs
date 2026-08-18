using System.Net;
using System.Text.Json;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase5ProductionLifecycleApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task FullLifecyclePersistsAttemptsPausesHistoryOutboxAndExactReplays()
    {
        var seeded = await Seed();
        var startKey = Guid.NewGuid();
        var start = await Post(seeded, "start-preparation", new { expectedVersion = 1 }, startKey); start.EnsureSuccessStatusCode();
        var replay = await Post(seeded, "start-preparation", new { expectedVersion = 1 }, startKey); using var firstJson = JsonDocument.Parse(await start.Content.ReadAsStringAsync()); using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync()); Assert.True(JsonElement.DeepEquals(firstJson.RootElement, replayJson.RootElement));
        await PostOk(seeded, "pause", new { reasonCode = "WAITING_OVEN", description = "Forno ocupado" });
        await PostOk(seeded, "resume", new { });
        await PostOk(seeded, "ready", new { });
        await using var db = fixture.CreateDbContext(); var item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == seeded.ProductionId);
        Assert.Equal("ready", item.Status); Assert.NotNull(item.PreparationStartedAt); Assert.NotNull(item.ReadyAt); Assert.True(item.Version >= 5);
        var attempt = await db.Set<ProductionAttempt>().SingleAsync(x => x.ProductionItemId == item.Id); Assert.Equal("completed", attempt.Status); Assert.NotNull(attempt.FinishedAt);
        var pause = await db.Set<ProductionPause>().SingleAsync(x => x.ProductionItemId == item.Id); Assert.NotNull(pause.ResumedAt); Assert.Equal("WAITING_OVEN", pause.ReasonCode);
        Assert.Equal(4, await db.Set<ProductionStatusHistory>().CountAsync(x => x.ProductionItemId == item.Id));
        Assert.Equal(4, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == seeded.Tenant.EstablishmentId && x.EventType.StartsWith("production-")));
        Assert.Equal(4, await db.IdempotencyRecords.CountAsync(x => x.EstablishmentId == seeded.Tenant.EstablishmentId && x.OperationType.StartsWith("kitchen.") && x.OperationType != "kitchen.accept"));
    }

    [Fact]
    public async Task FailedAttemptIsHistoricalAndConcurrentRestartCreatesOneMonotonicAttempt()
    {
        var seeded = await Seed(); await PostOk(seeded, "start-preparation", new { }); await PostOk(seeded, "fail-attempt", new { reasonCode = "BURNED", description = "Falha controlada" });
        var responses = await fixture.ConcurrentAsync(
            () => Post(seeded, "restart", new { }, Guid.NewGuid()),
            () => Post(seeded, "restart", new { }, Guid.NewGuid()));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var db = fixture.CreateDbContext(); var attempts = await db.Set<ProductionAttempt>().Where(x => x.ProductionItemId == seeded.ProductionId).OrderBy(x => x.AttemptNumber).ToListAsync();
        Assert.Equal(2, attempts.Count); Assert.Equal([1, 2], attempts.Select(x => x.AttemptNumber)); Assert.Equal("failed", attempts[0].Status); Assert.Equal("active", attempts[1].Status);
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == seeded.Tenant.EstablishmentId && x.EventType == "production-attempt-restarted.v1"));
    }

    [Fact]
    public async Task ConcurrentStartPauseResumeAndReadyHaveSinglePhysicalEffects()
    {
        var seeded = await Seed();
        var starts = await Concurrent(seeded, "start-preparation", new { }); AssertWinnerAndConflict(starts);
        var pauses = await Concurrent(seeded, "pause", new { reasonCode = "WAIT" }); AssertWinnerAndConflict(pauses);
        await using (var db = fixture.CreateDbContext()) Assert.Equal(1, await db.Set<ProductionPause>().CountAsync(x => x.ProductionItemId == seeded.ProductionId && x.ResumedAt == null));
        var resumes = await Concurrent(seeded, "resume", new { }); AssertWinnerAndConflict(resumes);
        var ready = await Concurrent(seeded, "ready", new { }); AssertWinnerAndConflict(ready);
        await using var verified = fixture.CreateDbContext(); Assert.Equal("ready", await verified.Set<ProductionItem>().Where(x => x.Id == seeded.ProductionId).Select(x => x.Status).SingleAsync()); Assert.Equal(1, await verified.OutboxMessages.CountAsync(x => x.EstablishmentId == seeded.Tenant.EstablishmentId && x.EventType == "production-item-ready.v1")); Assert.Equal(0, await verified.Set<ProductionPause>().CountAsync(x => x.ProductionItemId == seeded.ProductionId && x.ResumedAt == null));
    }

    [Fact]
    public async Task FailAndReadyRaceCannotFinishAttemptTwice()
    {
        var seeded = await Seed(); await PostOk(seeded, "start-preparation", new { });
        var responses = await fixture.ConcurrentAsync(() => Post(seeded, "fail-attempt", new { reasonCode = "FAIL" }, Guid.NewGuid()), () => Post(seeded, "ready", new { }, Guid.NewGuid()));
        AssertWinnerAndConflict(responses); await using var db = fixture.CreateDbContext(); var attempt = await db.Set<ProductionAttempt>().SingleAsync(x => x.ProductionItemId == seeded.ProductionId); Assert.True(attempt.Status is "failed" or "completed"); Assert.NotNull(attempt.FinishedAt); var item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == seeded.ProductionId); Assert.True((attempt.Status, item.Status) is ("failed", "paused") or ("completed", "ready"));
    }

    [Fact]
    public async Task PauseAndReadyRaceNeverLeavesAnOpenPauseOnReadyItem()
    {
        var seeded = await Seed(); await PostOk(seeded, "start-preparation", new { });
        var responses = await fixture.ConcurrentAsync(() => Post(seeded, "pause", new { reasonCode = "WAIT" }, Guid.NewGuid()), () => Post(seeded, "ready", new { }, Guid.NewGuid()));
        AssertWinnerAndConflict(responses); await using var db = fixture.CreateDbContext(); var item = await db.Set<ProductionItem>().SingleAsync(x => x.Id == seeded.ProductionId); var open = await db.Set<ProductionPause>().CountAsync(x => x.ProductionItemId == item.Id && x.ResumedAt == null); Assert.True((item.Status, open) is ("paused", 1) or ("ready", 0));
    }

    [Fact]
    public async Task LifecycleEnforcesPermissionAndTenantWithoutMutation()
    {
        var a = await fixture.CreateTenantAsync(2, 1); var seeded = await Seed(await fixture.CreateTenantAsync(2, 1)); var noPermission = await fixture.CreateUserTokenAsync(seeded.Tenant.EstablishmentId, "kitchen.production.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{seeded.ProductionId}/start-preparation", new { }, noPermission, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{seeded.ProductionId}/start-preparation", new { }, a.AccessToken, Guid.NewGuid())).StatusCode);
        await using var db = fixture.CreateDbContext(); Assert.Equal("awaiting_preparation", await db.Set<ProductionItem>().Where(x => x.Id == seeded.ProductionId).Select(x => x.Status).SingleAsync()); Assert.Empty(await db.Set<ProductionAttempt>().Where(x => x.ProductionItemId == seeded.ProductionId).ToListAsync());
    }

    [Fact]
    public async Task PayloadValidationAndDistinctKeyConflictsArePersisted()
    {
        var seeded = await Seed(); var missingReason = await Post(seeded, "pause", new { }, Guid.NewGuid()); Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);
        await PostOk(seeded, "start-preparation", new { }); var first = await Post(seeded, "pause", new { reasonCode = "WAIT" }, Guid.NewGuid()); first.EnsureSuccessStatusCode(); var loserKey = Guid.NewGuid(); var loser = await Post(seeded, "pause", new { reasonCode = "WAIT" }, loserKey); Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode); Assert.Equal("PRODUCTION_ITEM_ALREADY_PAUSED", await fixture.ErrorCodeAsync(loser)); var loserReplay = await Post(seeded, "pause", new { reasonCode = "WAIT" }, loserKey); Assert.Equal(HttpStatusCode.Conflict, loserReplay.StatusCode);
        await using var db = fixture.CreateDbContext(); Assert.True(await db.IdempotencyRecords.AnyAsync(x => x.EstablishmentId == seeded.Tenant.EstablishmentId && x.OperationType == "kitchen.pause" && x.IdempotencyKey == loserKey.ToString() && x.ResponseStatus == 409));
    }

    [Fact]
    public async Task LifecycleOutboxUsesInboxPerConsumerAndFinalizesAfterBoth()
    {
        fixture.Notifications.Reset(); var seeded = await Seed(); await PostOk(seeded, "start-preparation", new { }); await fixture.DispatchPhase4Async(); await fixture.DispatchPhase4Async();
        await using var db = fixture.CreateDbContext(); var message = await db.OutboxMessages.SingleAsync(x => x.EstablishmentId == seeded.Tenant.EstablishmentId && x.EventType == "production-item-preparation-started.v1"); var consumers = await db.InboxMessages.Where(x => x.EventId == message.Id).OrderBy(x => x.ConsumerName).Select(x => x.ConsumerName).ToListAsync(); Assert.Equal(["kitchen-signalr-v1", "kitchen-status-projection-v1"], consumers); Assert.NotNull(message.ProcessedAt); Assert.Equal(1, fixture.Notifications.Count(message.Id));
    }

    private async Task<Seeded> Seed(Phase1ApiFixture.TenantContext? tenant = null)
    {
        tenant ??= await fixture.CreateTenantAsync(2, 1); var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]); var session = await fixture.OpenSessionAsync(device.AccessToken); var now = DateTimeOffset.UtcNow; var orderId = Guid.NewGuid(); var orderItemId = Guid.NewGuid(); var stationId = Guid.NewGuid(); var productionId = Guid.NewGuid();
        await using var db = fixture.CreateDbContext(); db.Add(new Station { Id = stationId, EstablishmentId = tenant.EstablishmentId, Name = "Lifecycle", IsDefault = true, CreatedAt = now, UpdatedAt = now }); db.Add(new Order { Id = orderId, EstablishmentId = tenant.EstablishmentId, TableSessionId = session, SourceDeviceId = device.DeviceId, ClientSubmissionId = Guid.NewGuid(), SubtotalAmount = 10, TotalAmount = 10, SubmittedAt = now, CreatedAt = now, UpdatedAt = now }); db.Add(new OrderItem { Id = orderItemId, OrderId = orderId, LocalCartItemId = Guid.NewGuid(), ProductId = Guid.NewGuid(), ProductType = "simple", ProductName = "Lifecycle", Quantity = 1, UnitAmount = 10, TotalAmount = 10, ConfigurationVersion = "v1", CatalogRevisionId = Guid.NewGuid(), CatalogVersion = 1, AvailabilityVersion = 1, Snapshot = "{}", CreatedAt = now, UpdatedAt = now }); db.Add(new ProductionItem { Id = productionId, EstablishmentId = tenant.EstablishmentId, OrderItemId = orderItemId, StationId = stationId, Status = "awaiting_preparation", ReceivedAt = now, AcceptedAt = now, AcceptedByUserId = tenant.UserId, CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync(); return new(tenant, productionId);
    }
    private Task<HttpResponseMessage> Post(Seeded seed, string action, object body, Guid key) => fixture.PostWithIdempotencyAsync($"api/v1/operations/kitchen/production-items/{seed.ProductionId}/{action}", body, seed.Tenant.AccessToken, key);
    private async Task PostOk(Seeded seed, string action, object body) => (await Post(seed, action, body, Guid.NewGuid())).EnsureSuccessStatusCode();
    private Task<HttpResponseMessage[]> Concurrent(Seeded seed, string action, object body) => fixture.ConcurrentAsync(() => Post(seed, action, body, Guid.NewGuid()), () => Post(seed, action, body, Guid.NewGuid()));
    private static void AssertWinnerAndConflict(HttpResponseMessage[] responses) { Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict); Assert.DoesNotContain(responses, x => x.StatusCode == HttpStatusCode.InternalServerError); }
    private sealed record Seeded(Phase1ApiFixture.TenantContext Tenant, Guid ProductionId);
}
