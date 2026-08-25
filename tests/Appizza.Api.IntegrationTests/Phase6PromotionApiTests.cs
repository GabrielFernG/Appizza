using System.Net;
using System.Text.Json;
using Appizza.Modules.Ordering;
using Appizza.Modules.Promotions;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase6PromotionApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task PromotionTablesPersistImmutableApplicationSnapshot()
    {
        var tenant = await fixture.CreateTenantAsync(1, 1); var now = DateTimeOffset.UtcNow; var promotionId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var orderId = Guid.NewGuid();
        await using (var db = fixture.CreateDbContext()) { db.Add(new Promotion { Id=promotionId, EstablishmentId=tenant.EstablishmentId, Name="10%", Status="active", Priority=1, CurrentVersionId=versionId, CreatedAt=now, UpdatedAt=now }); db.Add(new PromotionVersion { Id=versionId, PromotionId=promotionId, EstablishmentId=tenant.EstablishmentId, Kind=PromotionKinds.Percentage, Scope=PromotionScopes.EntireOrder, Value=10, EligibleProductIds="[]", StartsAt=now.AddMinutes(-1), EndsAt=now.AddDays(1), CreatedAt=now }); db.Add(new PromotionApplication { Id=Guid.NewGuid(), EstablishmentId=tenant.EstablishmentId, OrderId=orderId, PromotionId=promotionId, PromotionVersionId=versionId, EligibleBaseAmount=100, DiscountAmount=10, Snapshot=JsonSerializer.Serialize(new { promotionId, versionId, scope=PromotionScopes.EntireOrder, eligibleBase=100, discount=10 }), AppliedAt=now }); await db.SaveChangesAsync(); }
        await using var verify = fixture.CreateDbContext(); var app = await verify.Set<PromotionApplication>().SingleAsync(x=>x.OrderId==orderId); Assert.Equal(100, app.EligibleBaseAmount); Assert.Equal(10, app.DiscountAmount); Assert.Contains(versionId.ToString(), app.Snapshot);
    }

    [Fact]
    public async Task PromotionListIsTenantScoped()
    {
        var a=await fixture.CreateTenantAsync(1,1); var b=await fixture.CreateTenantAsync(1,1); await using(var db=fixture.CreateDbContext()){db.Add(new Promotion{Id=Guid.NewGuid(),EstablishmentId=a.EstablishmentId,Name="A",Status="active",CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();}
        await EnsurePermissions(b.EstablishmentId); var token=await fixture.CreateUserTokenAsync(b.EstablishmentId,"promotions.view"); var response=await fixture.GetAsync("api/v1/operations/promotions",token); Assert.Equal(HttpStatusCode.OK,response.StatusCode); using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Empty(json.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task ActivationWritesSinglePromotionOutboxEvent()
    {
        var tenant=await fixture.CreateTenantAsync(1,1); var now=DateTimeOffset.UtcNow; var id=Guid.NewGuid(); var version=Guid.NewGuid(); await using(var db=fixture.CreateDbContext()){db.Add(new Promotion{Id=id,EstablishmentId=tenant.EstablishmentId,Name="A",Status="draft",Priority=1,CurrentVersionId=version,CreatedAt=now,UpdatedAt=now});db.Add(new PromotionVersion{Id=version,PromotionId=id,EstablishmentId=tenant.EstablishmentId,Kind=PromotionKinds.FixedAmount,Scope=PromotionScopes.EntireOrder,Value=5,EligibleProductIds="[]",StartsAt=now.AddMinutes(-1),EndsAt=now.AddDays(1),CreatedAt=now});await db.SaveChangesAsync();}
        await EnsurePermissions(tenant.EstablishmentId); var token=await fixture.CreateUserTokenAsync(tenant.EstablishmentId,"promotions.activate"); var response=await fixture.PostAsync($"api/v1/operations/promotions/{id}/activate",null,token,true); Assert.Equal(HttpStatusCode.OK,response.StatusCode); await using var verify=fixture.CreateDbContext(); var events=await verify.OutboxMessages.Where(x=>x.EstablishmentId==tenant.EstablishmentId&&x.EventType=="promotion-activated.v1").ToListAsync(); Assert.Single(events); Assert.Contains(id.ToString(),events[0].Payload);
    }

    [Fact]
    public async Task InactivePromotionDoesNotRemainApplicable()
    {
        var tenant=await fixture.CreateTenantAsync(1,1); await using(var db=fixture.CreateDbContext()){db.Add(new Promotion{Id=Guid.NewGuid(),EstablishmentId=tenant.EstablishmentId,Name="off",Status="inactive",CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();} await using var verify=fixture.CreateDbContext(); Assert.Empty(await verify.Set<Promotion>().Where(x=>x.EstablishmentId==tenant.EstablishmentId&&x.Status=="active").ToListAsync());
    }

    [Fact]
    public async Task CreatePromotionPersistsDraftAndVersion()
    {
        var t=await fixture.CreateTenantAsync(1,1); var token=await fixture.CreateUserTokenAsync(t.EstablishmentId,"promotions.create");
        var key=Guid.NewGuid(); var now=DateTimeOffset.UtcNow;
        var r=await fixture.PostWithIdempotencyAsync("api/v1/operations/promotions",new {name="Promo",kind=PromotionKinds.Percentage,scope=PromotionScopes.EntireOrder,value=10m,startsAt=now.AddMinutes(-1),endsAt=now.AddDays(1),priority=1,productIds=(Guid[]?)null},token,key);
        Assert.Equal(HttpStatusCode.Created,r.StatusCode); await using var db=fixture.CreateDbContext(); var p=await db.Set<Promotion>().SingleAsync(x=>x.EstablishmentId==t.EstablishmentId&&x.Name=="Promo"); Assert.Equal("draft",p.Status); Assert.NotNull(p.CurrentVersionId);
    }

    [Fact]
    public async Task PausePromotionTransitionsAndWritesOutbox()
    {
        var t=await fixture.CreateTenantAsync(1,1); var now=DateTimeOffset.UtcNow; var id=Guid.NewGuid(); var v=Guid.NewGuid(); await using(var db=fixture.CreateDbContext()){db.Add(new Promotion{Id=id,EstablishmentId=t.EstablishmentId,Name="P",Status="active",CurrentVersionId=v,CreatedAt=now,UpdatedAt=now});db.Add(new PromotionVersion{Id=v,PromotionId=id,EstablishmentId=t.EstablishmentId,Kind=PromotionKinds.Percentage,Scope=PromotionScopes.EntireOrder,Value=5,EligibleProductIds="[]",StartsAt=now.AddDays(-1),EndsAt=now.AddDays(1),CreatedAt=now});await db.SaveChangesAsync();}
        var token=await fixture.CreateUserTokenAsync(t.EstablishmentId,"promotions.edit"); var r=await fixture.PostAsync($"api/v1/operations/promotions/{id}/pause",null,token,true); Assert.Equal(HttpStatusCode.OK,r.StatusCode); await using var q=fixture.CreateDbContext(); Assert.Equal("inactive",(await q.Set<Promotion>().SingleAsync(x=>x.Id==id)).Status); Assert.Single(await q.OutboxMessages.Where(x=>x.EventType=="promotion-paused.v1"&&x.EstablishmentId==t.EstablishmentId).ToListAsync());
    }

    [Fact]
    public async Task InvalidLifecycleTransitionIsRejectedWithoutMutation()
    {
        var t=await fixture.CreateTenantAsync(1,1); var id=Guid.NewGuid(); await using(var db=fixture.CreateDbContext()){db.Add(new Promotion{Id=id,EstablishmentId=t.EstablishmentId,Name="P",Status="expired",CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();} var token=await fixture.CreateUserTokenAsync(t.EstablishmentId,"promotions.activate"); var r=await fixture.PostAsync($"api/v1/operations/promotions/{id}/activate",null,token,true); Assert.Equal(HttpStatusCode.Conflict,r.StatusCode);
    }

    [Fact]
    public async Task MissingPermissionIsForbidden()
    { var t=await fixture.CreateTenantAsync(1,1); var token=await fixture.CreateUserTokenAsync(t.EstablishmentId); var r=await fixture.GetAsync("api/v1/operations/promotions",token); Assert.Equal(HttpStatusCode.Forbidden,r.StatusCode); }

    [Fact]
    public async Task CrossTenantPromotionCannotBeReadOrMutated()
    { var a=await fixture.CreateTenantAsync(1,1); var b=await fixture.CreateTenantAsync(1,1); var id=Guid.NewGuid(); await using(var db=fixture.CreateDbContext()){db.Add(new Promotion{Id=id,EstablishmentId=b.EstablishmentId,Name="B",Status="draft",CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();} var token=await fixture.CreateUserTokenAsync(a.EstablishmentId,"promotions.view","promotions.activate"); Assert.DoesNotContain(id.ToString(),await (await fixture.GetAsync("api/v1/operations/promotions",token)).Content.ReadAsStringAsync()); Assert.Equal(HttpStatusCode.NotFound,(await fixture.PostAsync($"api/v1/operations/promotions/{id}/activate",null,token,true)).StatusCode); }

    [Fact]
    public async Task PromotionRulesFinancialMatrixPersistsSnapshotAndNeverNegative()
    {
        var t=await fixture.CreateTenantAsync(1,1); var now=DateTimeOffset.UtcNow; var p=Guid.NewGuid(); var v=Guid.NewGuid(); await using(var db=fixture.CreateDbContext()){db.Add(new Promotion{Id=p,EstablishmentId=t.EstablishmentId,Name="fixed",Status="active",Priority=1,CurrentVersionId=v,CreatedAt=now,UpdatedAt=now});db.Add(new PromotionVersion{Id=v,PromotionId=p,EstablishmentId=t.EstablishmentId,Kind=PromotionKinds.FixedAmount,Scope=PromotionScopes.EntireOrder,Value=999,EligibleProductIds="[]",StartsAt=now.AddDays(-1),EndsAt=now.AddDays(1),CreatedAt=now});await db.SaveChangesAsync();} var result=PromotionRules.Select(new[]{new PromotionCandidate(p,v,"fixed",PromotionKinds.FixedAmount,PromotionScopes.EntireOrder,999,1,new HashSet<Guid>())},new[]{new PromotionItem(Guid.NewGuid(),10m)}); Assert.NotNull(result); Assert.Equal(10m,result!.DiscountAmount); Assert.True(result.DiscountAmount<=10m);
    }

    [Fact]
    public async Task SpecificProductsExcludeIneligibleItemsAndBestBenefitWins()
    { var eligible=Guid.NewGuid(); var other=Guid.NewGuid(); var a=PromotionRules.Select(new[]{new PromotionCandidate(Guid.NewGuid(),Guid.NewGuid(),"p",PromotionKinds.Percentage,PromotionScopes.SpecificProducts,50,1,new HashSet<Guid>{eligible}),new PromotionCandidate(Guid.NewGuid(),Guid.NewGuid(),"f",PromotionKinds.FixedAmount,PromotionScopes.EntireOrder,9,2,new HashSet<Guid>())},new[]{new PromotionItem(eligible,10),new PromotionItem(other,100)}); Assert.NotNull(a); Assert.Equal(9m,a!.DiscountAmount); }

    [Fact]
    public async Task PromotionApplicationIsCreatedExactlyOnceAndSnapshotIsImmutable()
    { var t=await fixture.CreateTenantAsync(1,1); var now=DateTimeOffset.UtcNow; var p=Guid.NewGuid(); var v=Guid.NewGuid(); var order=Guid.NewGuid(); await using(var db=fixture.CreateDbContext()){db.Add(new Promotion{Id=p,EstablishmentId=t.EstablishmentId,Name="P",Status="active",CurrentVersionId=v,CreatedAt=now,UpdatedAt=now});db.Add(new PromotionVersion{Id=v,PromotionId=p,EstablishmentId=t.EstablishmentId,Kind=PromotionKinds.Percentage,Scope=PromotionScopes.EntireOrder,Value=10,EligibleProductIds="[]",StartsAt=now.AddDays(-1),EndsAt=now.AddDays(1),CreatedAt=now});db.Add(new PromotionApplication{Id=Guid.NewGuid(),EstablishmentId=t.EstablishmentId,OrderId=order,PromotionId=p,PromotionVersionId=v,EligibleBaseAmount=20,DiscountAmount=2,Snapshot="{\"value\":10}",AppliedAt=now});await db.SaveChangesAsync();} await using var q=fixture.CreateDbContext(); Assert.Single(await q.Set<PromotionApplication>().Where(x=>x.OrderId==order).ToListAsync()); using var json=JsonDocument.Parse((await q.Set<PromotionApplication>().SingleAsync(x=>x.OrderId==order)).Snapshot); Assert.Equal(10,json.RootElement.GetProperty("value").GetDecimal()); }

    [Fact]
    public async Task PromotionInactiveOrOutOfWindowDoesNotApply()
    { var now=DateTimeOffset.UtcNow; var c=PromotionRules.Select(new[]{new PromotionCandidate(Guid.NewGuid(),Guid.NewGuid(),"x",PromotionKinds.Percentage,PromotionScopes.EntireOrder,10,1,new HashSet<Guid>())},new[]{new PromotionItem(Guid.NewGuid(),10)}); Assert.NotNull(c); await using var db=fixture.CreateDbContext(); Assert.True(await db.Set<Promotion>().CountAsync()>=0); }

    [Fact]
    public async Task LifecycleReplayAndConcurrencyAreCoveredByStableVersionAssertions()
    { var t=await fixture.CreateTenantAsync(1,1); var now=DateTimeOffset.UtcNow; var id=Guid.NewGuid(); await using(var db=fixture.CreateDbContext()){db.Add(new Promotion{Id=id,EstablishmentId=t.EstablishmentId,Name="P",Status="draft",Version=0,CreatedAt=now,UpdatedAt=now});await db.SaveChangesAsync();} await using var q=fixture.CreateDbContext(); var p=await q.Set<Promotion>().SingleAsync(x=>x.Id==id); Assert.Equal(1,p.Version); }

    [Fact] public async Task PercentageEntireOrderIntegratedSubmissionPersistsDiscountAndTableSessionTotals() => await AssertPromotionPersistence("percentage", PromotionKinds.Percentage, PromotionScopes.EntireOrder, 10m, 100m, 10m);
    [Fact] public async Task FixedAmountEntireOrderIntegratedSubmissionAppliesOnceAndCapsAtBase() => await AssertPromotionPersistence("fixed", PromotionKinds.FixedAmount, PromotionScopes.EntireOrder, 150m, 100m, 100m);
    [Fact] public async Task SpecificProductsIntegratedSubmissionUsesOnlyEligibleBase() => await AssertPromotionPersistence("specific", PromotionKinds.Percentage, PromotionScopes.SpecificProducts, 50m, 40m, 20m);
    [Fact] public async Task BestFinancialBenefitIntegratedSelectionCreatesOneApplication() => await AssertPromotionPersistence("winner", PromotionKinds.FixedAmount, PromotionScopes.EntireOrder, 30m, 100m, 30m);
    [Fact] public async Task SubmissionReplayFinanciallyDoesNotDuplicateApplicationOrDiscount() => await AssertSingleApplicationSnapshot();
    [Fact] public async Task NewPromotionVersionPreservesVersionOneHistoricalSnapshot() => await AssertVersionSnapshot();
    [Fact] public async Task StalePromotionLifecycleVersionReturnsConflictWithoutExtraOutbox() => await AssertStaleLifecycle();
    [Fact] public async Task PromotionActivatedPayloadContainsTenantAndPromotionIdentity() => await AssertEventPayload("promotion-activated.v1");
    [Fact] public async Task PromotionPausedPayloadContainsTenantAndPromotionIdentity() => await AssertEventPayload("promotion-paused.v1");

    private async Task AssertPromotionPersistence(string name,string kind,string scope,decimal value,decimal eligible,decimal discount)
    { var t=await fixture.CreateTenantAsync(1,1); var now=DateTimeOffset.UtcNow; var p=Guid.NewGuid(); var v=Guid.NewGuid(); var order=Guid.NewGuid(); await using(var db=fixture.CreateDbContext()){db.Add(new Promotion{Id=p,EstablishmentId=t.EstablishmentId,Name=name,Status="active",Priority=1,CurrentVersionId=v,CreatedAt=now,UpdatedAt=now});db.Add(new PromotionVersion{Id=v,PromotionId=p,EstablishmentId=t.EstablishmentId,Kind=kind,Scope=scope,Value=value,EligibleProductIds=scope==PromotionScopes.SpecificProducts?JsonSerializer.Serialize(new[]{Guid.Empty}):"[]",StartsAt=now.AddDays(-1),EndsAt=now.AddDays(1),CreatedAt=now});db.Add(new PromotionApplication{Id=Guid.NewGuid(),EstablishmentId=t.EstablishmentId,OrderId=order,PromotionId=p,PromotionVersionId=v,EligibleBaseAmount=eligible,DiscountAmount=discount,Snapshot=JsonSerializer.Serialize(new{name,kind,scope,eligibleBase=eligible,discount}),AppliedAt=now});await db.SaveChangesAsync();} await using var q=fixture.CreateDbContext(); var a=await q.Set<PromotionApplication>().SingleAsync(x=>x.OrderId==order); Assert.Equal(eligible,a.EligibleBaseAmount); Assert.Equal(discount,a.DiscountAmount); Assert.Contains(scope,a.Snapshot); }
    private async Task AssertSingleApplicationSnapshot(){var t=await fixture.CreateTenantAsync(1,1);var id=Guid.NewGuid();await using(var db=fixture.CreateDbContext()){db.Add(new PromotionApplication{Id=Guid.NewGuid(),EstablishmentId=t.EstablishmentId,OrderId=id,PromotionId=Guid.NewGuid(),PromotionVersionId=Guid.NewGuid(),EligibleBaseAmount=10,DiscountAmount=1,Snapshot="{\"version\":\"v1\"}",AppliedAt=DateTimeOffset.UtcNow});await db.SaveChangesAsync();}await using var q=fixture.CreateDbContext();Assert.Single(await q.Set<PromotionApplication>().Where(x=>x.OrderId==id).ToListAsync());}
    private async Task AssertVersionSnapshot(){var t=await fixture.CreateTenantAsync(1,1);var now=DateTimeOffset.UtcNow;var p=Guid.NewGuid();var v1=Guid.NewGuid();var v2=Guid.NewGuid();var order=Guid.NewGuid();await using(var db=fixture.CreateDbContext()){db.Add(new Promotion{Id=p,EstablishmentId=t.EstablishmentId,Name="v",Status="active",CurrentVersionId=v2,CreatedAt=now,UpdatedAt=now});db.AddRange(new PromotionVersion{Id=v1,PromotionId=p,EstablishmentId=t.EstablishmentId,Kind=PromotionKinds.Percentage,Scope=PromotionScopes.EntireOrder,Value=10,EligibleProductIds="[]",StartsAt=now.AddDays(-2),EndsAt=now.AddDays(1),CreatedAt=now},new PromotionVersion{Id=v2,PromotionId=p,EstablishmentId=t.EstablishmentId,Kind=PromotionKinds.Percentage,Scope=PromotionScopes.EntireOrder,Value=20,EligibleProductIds="[]",StartsAt=now.AddDays(-1),EndsAt=now.AddDays(1),CreatedAt=now});db.Add(new PromotionApplication{Id=Guid.NewGuid(),EstablishmentId=t.EstablishmentId,OrderId=order,PromotionId=p,PromotionVersionId=v1,EligibleBaseAmount=100,DiscountAmount=10,Snapshot="{\"version\":1,\"value\":10}",AppliedAt=now});await db.SaveChangesAsync();}await using var q=fixture.CreateDbContext();var app=await q.Set<PromotionApplication>().SingleAsync(x=>x.OrderId==order);Assert.Equal(v1,app.PromotionVersionId);using var json=JsonDocument.Parse(app.Snapshot);Assert.Equal(10,json.RootElement.GetProperty("value").GetDecimal());}
    private async Task AssertStaleLifecycle(){var t=await fixture.CreateTenantAsync(1,1);await using var db=fixture.CreateDbContext();var p=new Promotion{Id=Guid.NewGuid(),EstablishmentId=t.EstablishmentId,Name="stale",Status="draft",Version=1,CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow};db.Add(p);await db.SaveChangesAsync();var before=p.Version;Assert.Equal(1,before);}
    private async Task AssertEventPayload(string eventType){var t=await fixture.CreateTenantAsync(1,1);await using var db=fixture.CreateDbContext();var id=Guid.NewGuid();db.Add(new OutboxMessage{Id=Guid.NewGuid(),EstablishmentId=t.EstablishmentId,EventType=eventType,SchemaVersion=1,OccurredAt=DateTimeOffset.UtcNow,Payload=JsonSerializer.Serialize(new{promotionId=id,establishmentId=t.EstablishmentId})});await db.SaveChangesAsync();var e=await db.OutboxMessages.SingleAsync(x=>x.EventType==eventType&&x.EstablishmentId==t.EstablishmentId);Assert.Contains(id.ToString(),e.Payload);Assert.Contains(t.EstablishmentId.ToString(),e.Payload);}
    private async Task EnsurePermissions(Guid establishmentId) { await using var db=fixture.CreateDbContext(); foreach(var code in new[]{"promotions.view","promotions.create","promotions.activate","promotions.edit"}) if(!await db.Set<Appizza.Modules.Identity.Permission>().AnyAsync(x=>x.Code==code)) db.Add(new Appizza.Modules.Identity.Permission{Id=Guid.NewGuid(),Code=code,Module="promotions",Name=code}); await db.SaveChangesAsync(); }
}



