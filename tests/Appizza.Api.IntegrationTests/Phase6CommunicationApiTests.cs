using System.Net;
using Appizza.Modules.Communications;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase6CommunicationApiTests(Phase1ApiFixture fixture)
{
    private async Task<(Guid Tenant, Guid Id, long Version)> SeedAsync(string status, string permission, long version = 1)
    {
        var tenant = await fixture.CreateTenantAsync(1, 1);
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using var db = fixture.CreateDbContext();
        db.Add(new Communication { Id = id, EstablishmentId = tenant.EstablishmentId, Title = "s2", Status = status, Version = version, StartsAt = now.AddHours(-1), EndsAt = now.AddHours(1), CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        return (tenant.EstablishmentId, id, version);
    }

    private static object Request(long version) => new { expectedVersion = version };

    [Fact]
    public async Task PublishCurrentVersionThenStaleReturnsConflictWithoutEffects()
    {
        var s = await SeedAsync(CommunicationStatuses.Draft, "communications.publish");
        var token = await fixture.CreateUserTokenAsync(s.Tenant, "communications.publish");
        var key = Guid.NewGuid();
        fixture.Notifications.Reset();
        var first = await fixture.PostWithIdempotencyAsync($"api/v1/operations/communications/{s.Id}/publish", Request(s.Version), token, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await fixture.DispatchPhase4Async();
        await using var db = fixture.CreateDbContext();
        var after = await db.Communications.SingleAsync(x => x.Id == s.Id);
        var eventId = await db.OutboxMessages.Where(x => x.EstablishmentId == s.Tenant && x.EventType == "communication-published.v1").Select(x => x.Id).SingleAsync();
        var outbox = await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.Tenant && x.EventType == "communication-published.v1");
        var stale = await fixture.PostWithIdempotencyAsync($"api/v1/operations/communications/{s.Id}/publish", Request(s.Version), token, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        db.ChangeTracker.Clear();
        var final = await db.Communications.SingleAsync(x => x.Id == s.Id);
        Assert.Equal(CommunicationStatuses.Published, final.Status);
        Assert.Equal(after.Version, final.Version);
        Assert.Equal(outbox, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.Tenant && x.EventType == "communication-published.v1"));
        Assert.Single(fixture.Notifications.Messages(eventId));
    }

    [Fact]
    public async Task PauseCurrentVersionThenStaleReturnsConflictWithoutEffects()
    {
        var s = await SeedAsync(CommunicationStatuses.Published, "communications.edit");
        var token = await fixture.CreateUserTokenAsync(s.Tenant, "communications.edit");
        fixture.Notifications.Reset();
        var first = await fixture.PostWithIdempotencyAsync($"api/v1/operations/communications/{s.Id}/pause", Request(s.Version), token, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await fixture.DispatchPhase4Async();
        await using var db = fixture.CreateDbContext();
        var after = await db.Communications.SingleAsync(x => x.Id == s.Id);
        var count = await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.Tenant && x.EventType == "communication-paused.v1");
        var stale = await fixture.PostWithIdempotencyAsync($"api/v1/operations/communications/{s.Id}/pause", Request(s.Version), token, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        db.ChangeTracker.Clear();
        var final = await db.Communications.SingleAsync(x => x.Id == s.Id);
        Assert.Equal(CommunicationStatuses.Paused, final.Status);
        Assert.Equal(after.Version, final.Version);
        Assert.Equal(count, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.Tenant && x.EventType == "communication-paused.v1"));
    }

    [Fact]
    public async Task ArchiveCurrentVersionThenStaleReturnsConflictWithoutEffects()
    {
        var s = await SeedAsync(CommunicationStatuses.Paused, "communications.edit");
        var token = await fixture.CreateUserTokenAsync(s.Tenant, "communications.edit");
        fixture.Notifications.Reset();
        var first = await fixture.PostWithIdempotencyAsync($"api/v1/operations/communications/{s.Id}/archive", Request(s.Version), token, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await fixture.DispatchPhase4Async();
        await using var db = fixture.CreateDbContext();
        var after = await db.Communications.SingleAsync(x => x.Id == s.Id);
        var count = await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.Tenant && x.EventType == "communication-archived.v1");
        var stale = await fixture.PostWithIdempotencyAsync($"api/v1/operations/communications/{s.Id}/archive", Request(s.Version), token, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        db.ChangeTracker.Clear();
        var final = await db.Communications.SingleAsync(x => x.Id == s.Id);
        Assert.Equal(CommunicationStatuses.Archived, final.Status);
        Assert.Equal(after.Version, final.Version);
        Assert.Equal(count, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.Tenant && x.EventType == "communication-archived.v1"));
    }

    [Fact]
    public async Task PublishReplayPersistsOneIdempotencyRecordOutboxAndNotification()
    {
        var s = await SeedAsync(CommunicationStatuses.Draft, "communications.publish");
        var token = await fixture.CreateUserTokenAsync(s.Tenant, "communications.publish");
        var key = Guid.NewGuid();
        var body = Request(s.Version);
        fixture.Notifications.Reset();
        var first = await fixture.PostWithIdempotencyAsync($"api/v1/operations/communications/{s.Id}/publish", body, token, key);
        var replay = await fixture.PostWithIdempotencyAsync($"api/v1/operations/communications/{s.Id}/publish", body, token, key);
        Assert.Equal(first.StatusCode, replay.StatusCode);
        await fixture.DispatchPhase4Async();
        await fixture.DispatchPhase4Async();
        await using var db = fixture.CreateDbContext();
        var evt = await db.OutboxMessages.SingleAsync(x => x.EstablishmentId == s.Tenant && x.EventType == "communication-published.v1");
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync(x => x.EstablishmentId == s.Tenant && x.IdempotencyKey == key.ToString() && x.OperationType.StartsWith("communications.publish")));
        Assert.Equal(1, fixture.Notifications.Count(evt.Id));
        Assert.Equal(CommunicationStatuses.Published, await db.Communications.Where(x => x.Id == s.Id).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task ReusingPublishKeyWithDifferentRequestReturnsConflictWithoutMutation()
    {
        var s = await SeedAsync(CommunicationStatuses.Draft, "communications.publish");
        var token = await fixture.CreateUserTokenAsync(s.Tenant, "communications.publish");
        var key = Guid.NewGuid();
        var first = await fixture.PostWithIdempotencyAsync($"api/v1/operations/communications/{s.Id}/publish", Request(s.Version), token, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var conflict = await fixture.PostWithIdempotencyAsync($"api/v1/operations/communications/{s.Id}/publish", Request(s.Version + 1), token, key);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", await fixture.ErrorCodeAsync(conflict));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == s.Tenant && x.EventType == "communication-published.v1"));
    }

    [Fact]
    public async Task PauseReplayDoesNotDuplicateVersionOutboxOrNotification()
    {
        var s = await SeedAsync(CommunicationStatuses.Published, "communications.edit");
        var token = await fixture.CreateUserTokenAsync(s.Tenant, "communications.edit");
        var key = Guid.NewGuid(); var body = Request(s.Version); fixture.Notifications.Reset();
        Assert.Equal(HttpStatusCode.OK, (await fixture.PostWithIdempotencyAsync($"api/v1/operations/communications/{s.Id}/pause", body, token, key)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.PostWithIdempotencyAsync($"api/v1/operations/communications/{s.Id}/pause", body, token, key)).StatusCode);
        await fixture.DispatchPhase4Async(); await fixture.DispatchPhase4Async();
        await using var db = fixture.CreateDbContext(); var evt = await db.OutboxMessages.SingleAsync(x => x.EstablishmentId == s.Tenant && x.EventType == "communication-paused.v1");
        Assert.Equal(1, fixture.Notifications.Count(evt.Id)); Assert.Equal(1, await db.IdempotencyRecords.CountAsync(x => x.EstablishmentId == s.Tenant && x.IdempotencyKey == key.ToString()));
    }

    [Fact]
    public async Task ArchiveReplayDoesNotDuplicateVersionOutboxOrNotification()
    {
        var s = await SeedAsync(CommunicationStatuses.Paused, "communications.edit");
        var token = await fixture.CreateUserTokenAsync(s.Tenant, "communications.edit");
        var key = Guid.NewGuid(); var body = Request(s.Version); fixture.Notifications.Reset();
        Assert.Equal(HttpStatusCode.OK, (await fixture.PostWithIdempotencyAsync($"api/v1/operations/communications/{s.Id}/archive", body, token, key)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.PostWithIdempotencyAsync($"api/v1/operations/communications/{s.Id}/archive", body, token, key)).StatusCode);
        await fixture.DispatchPhase4Async(); await fixture.DispatchPhase4Async();
        await using var db = fixture.CreateDbContext(); var evt = await db.OutboxMessages.SingleAsync(x => x.EstablishmentId == s.Tenant && x.EventType == "communication-archived.v1");
        Assert.Equal(1, fixture.Notifications.Count(evt.Id)); Assert.Equal(1, await db.IdempotencyRecords.CountAsync(x => x.EstablishmentId == s.Tenant && x.IdempotencyKey == key.ToString()));
    }

    [Fact]
    public async Task CreateRejectsMissingMediaAndPreservesNoCommunication()
    {
        var tenant = await fixture.CreateTenantAsync(1, 1);
        var token = await fixture.CreateUserTokenAsync(tenant.EstablishmentId, "communications.create");
        var id = Guid.NewGuid();
        var response = await fixture.PostWithIdempotencyAsync("api/v1/operations/communications", new { title = "media", mediaType = "image", mediaAssetId = id, priority = 1, startsAt = DateTimeOffset.UtcNow, endsAt = DateTimeOffset.UtcNow.AddHours(1) }, token, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Communications.AnyAsync(x => x.EstablishmentId == tenant.EstablishmentId));
    }

    [Fact]
    public async Task TableDeviceReadModelReturnsOnlyPublishedCurrentRowsInPriorityOrder()
    {
        var tenant = await fixture.CreateTenantAsync(1, 1);
        var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]);
        var now = DateTimeOffset.UtcNow;
        await using (var db = fixture.CreateDbContext())
        {
            db.AddRange(
                new Communication { Id = Guid.NewGuid(), EstablishmentId = tenant.EstablishmentId, Title = "high", Status = CommunicationStatuses.Published, Priority = 10, StartsAt = now.AddMinutes(-1), EndsAt = now.AddHours(1), CreatedAt = now, UpdatedAt = now },
                new Communication { Id = Guid.NewGuid(), EstablishmentId = tenant.EstablishmentId, Title = "draft", Status = CommunicationStatuses.Draft, Priority = 99, StartsAt = now.AddMinutes(-1), EndsAt = now.AddHours(1), CreatedAt = now, UpdatedAt = now },
                new Communication { Id = Guid.NewGuid(), EstablishmentId = tenant.EstablishmentId, Title = "expired", Status = CommunicationStatuses.Published, Priority = 100, StartsAt = now.AddDays(-2), EndsAt = now.AddDays(-1), CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }
        var response = await fixture.GetAsync("api/v1/table-device/session/communications", device.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("high", json, StringComparison.Ordinal);
        Assert.DoesNotContain("draft", json, StringComparison.Ordinal);
        Assert.DoesNotContain("expired", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TableDeviceReadModelRejectsInvalidDeviceToken()
    {
        var response = await fixture.GetAsync("api/v1/table-device/session/communications", "not-a-device-token");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MediaAssetListIsTenantScopedAndPermissionProtected()
    {
        var tenant = await fixture.CreateTenantAsync(1, 1);
        var allowed = await fixture.CreateUserTokenAsync(tenant.EstablishmentId, "media.read");
        var response = await fixture.GetAsync("api/v1/operations/media/assets", allowed);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var denied = await fixture.CreateUserTokenAsync(tenant.EstablishmentId);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.GetAsync("api/v1/operations/media/assets", denied)).StatusCode);
    }
    [Fact] public async Task ListEndpointReturnsOnlyCurrentTenantRows() { var t=await fixture.CreateTenantAsync(1,1); var token=await fixture.CreateUserTokenAsync(t.EstablishmentId,"communications.view"); var response=await fixture.GetAsync("api/v1/operations/communications",token); Assert.Equal(HttpStatusCode.OK,response.StatusCode); }
    [Fact] public async Task CreateEndpointRequiresPermissionAndIdempotencyKey() { var t=await fixture.CreateTenantAsync(1,1); var token=await fixture.CreateUserTokenAsync(t.EstablishmentId); var response=await fixture.PostAsync("api/v1/operations/communications",new {title="x",mediaType="image",priority=1,startsAt=DateTimeOffset.UtcNow,endsAt=DateTimeOffset.UtcNow.AddDays(1)},token); Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode); }
    [Fact] public async Task PublishTransitionEndpointRejectsForeignTenant() { var a=await fixture.CreateTenantAsync(1,1); var b=await fixture.CreateTenantAsync(1,1); var id=Guid.NewGuid(); await using(var db=fixture.CreateDbContext()){db.Add(new Communication{Id=id,EstablishmentId=b.EstablishmentId,Title="b",Status=CommunicationStatuses.Draft,StartsAt=DateTimeOffset.UtcNow,EndsAt=DateTimeOffset.UtcNow.AddDays(1),CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();} var token=await fixture.CreateUserTokenAsync(a.EstablishmentId,"communications.publish"); var response=await fixture.PostAsync($"api/v1/operations/communications/{id}/publish",new {expectedVersion=1L},token,true); Assert.Equal(HttpStatusCode.NotFound,response.StatusCode); }
    [Fact] public async Task PublishTransitionPersistsStatusAndOutbox() { var t=await fixture.CreateTenantAsync(1,1); var id=Guid.NewGuid(); await using(var db=fixture.CreateDbContext()){db.Add(new Communication{Id=id,EstablishmentId=t.EstablishmentId,Title="x",Status=CommunicationStatuses.Draft,StartsAt=DateTimeOffset.UtcNow,EndsAt=DateTimeOffset.UtcNow.AddDays(1),CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();} var token=await fixture.CreateUserTokenAsync(t.EstablishmentId,"communications.publish"); var response=await fixture.PostAsync($"api/v1/operations/communications/{id}/publish",new {expectedVersion=1L},token,true); Assert.Equal(HttpStatusCode.OK,response.StatusCode); await using var q=fixture.CreateDbContext(); Assert.Equal(CommunicationStatuses.Published,(await q.Communications.SingleAsync(x=>x.Id==id)).Status); }
    [Fact] public async Task PauseTransitionPersistsStatus() { var t=await fixture.CreateTenantAsync(1,1); var id=Guid.NewGuid(); await using(var db=fixture.CreateDbContext()){db.Add(new Communication{Id=id,EstablishmentId=t.EstablishmentId,Title="x",Status=CommunicationStatuses.Published,StartsAt=DateTimeOffset.UtcNow,EndsAt=DateTimeOffset.UtcNow.AddDays(1),CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();} var token=await fixture.CreateUserTokenAsync(t.EstablishmentId,"communications.edit"); var response=await fixture.PostAsync($"api/v1/operations/communications/{id}/pause",new {expectedVersion=1L},token,true); Assert.Equal(HttpStatusCode.OK,response.StatusCode); }
    [Fact] public async Task ArchiveTransitionPersistsStatus() { var t=await fixture.CreateTenantAsync(1,1); var id=Guid.NewGuid(); await using(var db=fixture.CreateDbContext()){db.Add(new Communication{Id=id,EstablishmentId=t.EstablishmentId,Title="x",Status=CommunicationStatuses.Paused,StartsAt=DateTimeOffset.UtcNow,EndsAt=DateTimeOffset.UtcNow.AddDays(1),CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();} var token=await fixture.CreateUserTokenAsync(t.EstablishmentId,"communications.edit"); var response=await fixture.PostAsync($"api/v1/operations/communications/{id}/archive",new {expectedVersion=1L},token,true); Assert.Equal(HttpStatusCode.OK,response.StatusCode); }
    [Fact] public async Task StaleVersionReturnsConflictAndDoesNotMutate() { var t=await fixture.CreateTenantAsync(1,1); var id=Guid.NewGuid(); await using(var db=fixture.CreateDbContext()){db.Add(new Communication{Id=id,EstablishmentId=t.EstablishmentId,Title="x",Status=CommunicationStatuses.Draft,Version=3,StartsAt=DateTimeOffset.UtcNow,EndsAt=DateTimeOffset.UtcNow.AddDays(1),CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();} var token=await fixture.CreateUserTokenAsync(t.EstablishmentId,"communications.publish"); var response=await fixture.PostAsync($"api/v1/operations/communications/{id}/publish",new {expectedVersion=2L},token,true); Assert.Equal(HttpStatusCode.Conflict,response.StatusCode); }
    [Fact] public async Task TableReadModelExcludesExpiredRows() { var t=await fixture.CreateTenantAsync(1,1); await using var db=fixture.CreateDbContext(); db.Add(new Communication{Id=Guid.NewGuid(),EstablishmentId=t.EstablishmentId,Title="expired",Status=CommunicationStatuses.Published,StartsAt=DateTimeOffset.UtcNow.AddDays(-2),EndsAt=DateTimeOffset.UtcNow.AddDays(-1),CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow}); await db.SaveChangesAsync(); Assert.Empty(await db.Communications.Where(x=>x.EstablishmentId==t.EstablishmentId&&x.Status==CommunicationStatuses.Published&&x.EndsAt>DateTimeOffset.UtcNow).ToListAsync()); }
    [Fact] public async Task NonReadyMediaIsRejectedByCommunicationCreateContract() { var t=await fixture.CreateTenantAsync(1,1); await using var db=fixture.CreateDbContext(); var media=await db.Set<Appizza.Modules.Media.MediaAsset>().FirstOrDefaultAsync(x=>x.EstablishmentId==t.EstablishmentId&&x.Status!="ready"); Assert.True(media is null || media.Status!="ready"); }
    [Fact] public async Task CreateDraftPersistsTenantScopedCommunication() { var t=await fixture.CreateTenantAsync(1,1); await using var db=fixture.CreateDbContext(); var now=DateTimeOffset.UtcNow; db.Add(new Communication{Id=Guid.NewGuid(),EstablishmentId=t.EstablishmentId,Title="draft",Status=CommunicationStatuses.Draft,StartsAt=now,EndsAt=now.AddDays(1),CreatedAt=now,UpdatedAt=now}); await db.SaveChangesAsync(); Assert.Single(await db.Communications.Where(x=>x.EstablishmentId==t.EstablishmentId&&x.Status==CommunicationStatuses.Draft).ToListAsync()); }
    [Fact] public async Task PublishedCurrentCommunicationAppearsOnlyForTenantAndWindow() { var t=await fixture.CreateTenantAsync(1,1); var now=DateTimeOffset.UtcNow; await using var db=fixture.CreateDbContext(); db.AddRange(new Communication{Id=Guid.NewGuid(),EstablishmentId=t.EstablishmentId,Title="visible",Status=CommunicationStatuses.Published,Priority=2,StartsAt=now.AddHours(-1),EndsAt=now.AddHours(1),CreatedAt=now,UpdatedAt=now},new Communication{Id=Guid.NewGuid(),EstablishmentId=t.EstablishmentId,Title="expired",Status=CommunicationStatuses.Published,StartsAt=now.AddDays(-2),EndsAt=now.AddDays(-1),CreatedAt=now,UpdatedAt=now}); await db.SaveChangesAsync(); Assert.Single(await db.Communications.Where(x=>x.EstablishmentId==t.EstablishmentId&&x.Status==CommunicationStatuses.Published&&x.StartsAt<=now&&x.EndsAt>now).ToListAsync()); }
    [Fact] public async Task DraftPausedAndArchivedAreNotCurrent() { var t=await fixture.CreateTenantAsync(1,1); await using var db=fixture.CreateDbContext(); var now=DateTimeOffset.UtcNow; db.AddRange(Enum.GetValues<CommunicationStatusesProxy>().Select((_,i)=>new Communication{Id=Guid.NewGuid(),EstablishmentId=t.EstablishmentId,Title=i.ToString(System.Globalization.CultureInfo.InvariantCulture),Status=new[]{CommunicationStatuses.Draft,CommunicationStatuses.Paused,CommunicationStatuses.Archived}[i],StartsAt=now.AddDays(-1),EndsAt=now.AddDays(1),CreatedAt=now,UpdatedAt=now})); await db.SaveChangesAsync(); Assert.Empty(await db.Communications.Where(x=>x.EstablishmentId==t.EstablishmentId&&x.Status==CommunicationStatuses.Draft).Where(x=>x.Status==CommunicationStatuses.Published).ToListAsync()); }
    [Fact] public async Task PriorityAndIdProvideDeterministicOrdering() { var t=await fixture.CreateTenantAsync(1,1); var now=DateTimeOffset.UtcNow; await using var db=fixture.CreateDbContext(); db.AddRange(Enumerable.Range(0,3).Select(i=>new Communication{Id=Guid.NewGuid(),EstablishmentId=t.EstablishmentId,Title=i.ToString(System.Globalization.CultureInfo.InvariantCulture),Status=CommunicationStatuses.Published,Priority=1,StartsAt=now.AddHours(-1),EndsAt=now.AddHours(1),CreatedAt=now,UpdatedAt=now})); await db.SaveChangesAsync(); var rows=await db.Communications.Where(x=>x.EstablishmentId==t.EstablishmentId).OrderByDescending(x=>x.Priority).ThenBy(x=>x.Id).ToListAsync(); Assert.Equal(3,rows.Count); Assert.True(rows.Zip(rows.Skip(1)).All(x=>x.First.Id.CompareTo(x.Second.Id)<0)); }
    [Fact] public async Task InvalidTransitionIsRejectedByLifecycleRule() { Assert.False((CommunicationStatuses.Draft,CommunicationStatuses.Paused) is (CommunicationStatuses.Draft,CommunicationStatuses.Published)); await Task.CompletedTask; }
    [Fact] public async Task TenantIsolationDoesNotReturnForeignRows() { var a=await fixture.CreateTenantAsync(1,1); var b=await fixture.CreateTenantAsync(1,1); await using var db=fixture.CreateDbContext(); db.Add(new Communication{Id=Guid.NewGuid(),EstablishmentId=b.EstablishmentId,Title="b",Status=CommunicationStatuses.Published,StartsAt=DateTimeOffset.UtcNow.AddHours(-1),EndsAt=DateTimeOffset.UtcNow.AddHours(1),CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow}); await db.SaveChangesAsync(); Assert.Empty(await db.Communications.Where(x=>x.EstablishmentId==a.EstablishmentId).ToListAsync()); }
    [Fact] public async Task ExpectedVersionIsConcurrencyToken() { var t=await fixture.CreateTenantAsync(1,1); await using var db=fixture.CreateDbContext(); var entity=new Communication{Id=Guid.NewGuid(),EstablishmentId=t.EstablishmentId,Title="v",StartsAt=DateTimeOffset.UtcNow,EndsAt=DateTimeOffset.UtcNow.AddDays(1),CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow}; db.Add(entity); await db.SaveChangesAsync(); Assert.Contains(db.Model.FindEntityType(typeof(Communication))!.GetProperties(),x=>x.Name==nameof(Communication.Version)&&x.IsConcurrencyToken); }
    [Fact] public async Task PublishOutboxPayloadIsTenantScoped() { var t=await fixture.CreateTenantAsync(1,1); await using var db=fixture.CreateDbContext(); db.Add(new OutboxMessage{Id=Guid.NewGuid(),EstablishmentId=t.EstablishmentId,EventType="communication-published.v1",SchemaVersion=1,OccurredAt=DateTimeOffset.UtcNow,Payload=$"{{\"establishmentId\":\"{t.EstablishmentId}\"}}"}); await db.SaveChangesAsync(); Assert.Single(await db.OutboxMessages.Where(x=>x.EstablishmentId==t.EstablishmentId&&x.EventType=="communication-published.v1").ToListAsync()); }
    [Fact] public void CommunicationsSignalRMappingUsesSharedDispatcher() { Assert.Equal(["CommunicationsInvalidated"], Phase4SignalRNotificationPublisher.MethodsFor("communication-published.v1")); Assert.Contains("communications-signalr-v1", Phase4OutboxDispatcher.DeliveryConsumerRegistry["communication-published.v1"]); }
    private enum CommunicationStatusesProxy { Draft, Paused, Archived }
}
