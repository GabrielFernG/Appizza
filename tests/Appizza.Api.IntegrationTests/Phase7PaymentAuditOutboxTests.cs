using System.Text.Json;
using Appizza.Modules.Auditing;
using Appizza.Modules.Payments;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase7PaymentAuditOutboxTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task PaymentAttemptLifecycleWritesTransactionalObservabilityOnce()
    {
        var c = await fixture.CreateOpenSessionAsync();
        Guid tenant;
        await using (var db = fixture.CreateDbContext())
        {
            var session = await db.Set<Appizza.Modules.Tables.TableSession>().SingleAsync(x => x.Id == c.SessionId);
            tenant = session.EstablishmentId;
            session.TotalAmount = 10m; session.RemainingAmount = 10m;
            await db.SaveChangesAsync();
        }
        var planResponse = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = c.SessionId, mode = "total" }, c.Device.AccessToken, Guid.NewGuid());
        planResponse.EnsureSuccessStatusCode();
        using var plan = JsonDocument.Parse(await planResponse.Content.ReadAsStringAsync());
        var planId = plan.RootElement.GetProperty("planId").GetGuid();
        var allocationId = plan.RootElement.GetProperty("allocations")[0].GetProperty("allocationId").GetGuid();
        var response = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = c.SessionId, paymentPlanId = planId, allocationIds = new[] { allocationId }, paymentMethod = "cash" }, c.Device.AccessToken, Guid.NewGuid());
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var attemptId = body.RootElement.GetProperty("attemptId").GetGuid();
        await using (var created = fixture.CreateDbContext())
        {
            var createdMessages = await created.OutboxMessages.AsNoTracking().Where(x => x.EstablishmentId == tenant && x.EventType == "payment-attempt-created.v1").ToListAsync();
            Assert.Single(createdMessages, x => PayloadTargetsAttempt(x.Payload, attemptId));
            Assert.Single(await created.Set<AuditEntry>().Where(x => x.AggregateId == attemptId && x.Action == "payment_attempt.created").ToListAsync());
        }
        var service = fixture.CreatePaymentLifecycleService();
        await service.SucceedAsync(tenant, attemptId);
        await service.SucceedAsync(tenant, attemptId);
        await using var final = fixture.CreateDbContext();
        var approvedMessages = await final.OutboxMessages.AsNoTracking().Where(x => x.EstablishmentId == tenant && x.EventType == "payment-attempt-approved.v1").ToListAsync();
        Assert.Single(approvedMessages, x => PayloadTargetsAttempt(x.Payload, attemptId));
        Assert.Single(await final.Set<AuditEntry>().Where(x => x.AggregateId == attemptId && x.Action == "payment_attempt.approved").ToListAsync());
        Assert.Equal(1, await final.Set<PaymentAttempt>().CountAsync(x => x.Id == attemptId));
    }

    [Theory]
    [InlineData("declined", "payment-attempt-declined.v1", "payment_attempt.declined")]
    [InlineData("cancelled", "payment-attempt-cancelled.v1", "payment_attempt.cancelled")]
    [InlineData("expired", "payment-attempt-expired.v1", "payment_attempt.expired")]
    [InlineData("unknown", "payment-attempt-unknown.v1", "payment_attempt.unknown")]
    public async Task TerminalTransitionWritesOneObservableRecord(string state, string eventType, string action)
    { var x=await CreateAttempt(); var s=fixture.CreatePaymentLifecycleService(); if(state=="declined") await s.FailAsync(x.Tenant,x.AttemptId); else if(state=="cancelled") await s.CancelAsync(x.Tenant,x.AttemptId); else if(state=="expired") await s.ExpireAsync(x.Tenant,x.AttemptId); else await s.MarkUnknownAsync(x.Tenant,x.AttemptId); await using var db=fixture.CreateDbContext(); var ev=await db.OutboxMessages.AsNoTracking().Where(e=>e.EstablishmentId==x.Tenant&&e.EventType==eventType).ToListAsync(); Assert.Single(ev,e=>PayloadTargetsAttempt(e.Payload,x.AttemptId)); Assert.Single(await db.Set<AuditEntry>().AsNoTracking().Where(e=>e.AggregateId==x.AttemptId&&e.Action==action).ToListAsync()); }
    [Fact] public async Task UnknownResolutionWritesEachTransitionOnce() { var x=await CreateAttempt(); var s=fixture.CreatePaymentLifecycleService(); await s.MarkUnknownAsync(x.Tenant,x.AttemptId); await s.ResolveUnknownSuccessAsync(x.Tenant,x.AttemptId); await using var db=fixture.CreateDbContext(); foreach(var p in new[]{("payment-attempt-unknown.v1","payment_attempt.unknown"),("payment-attempt-approved.v1","payment_attempt.approved")}) { var ev=await db.OutboxMessages.AsNoTracking().Where(e=>e.EstablishmentId==x.Tenant&&e.EventType==p.Item1).ToListAsync(); Assert.Single(ev,e=>PayloadTargetsAttempt(e.Payload,x.AttemptId)); Assert.Single(await db.Set<AuditEntry>().AsNoTracking().Where(e=>e.AggregateId==x.AttemptId&&e.Action==p.Item2).ToListAsync()); } }
    [Fact] public async Task ConcurrentSuccessWritesSingleApprovedObservability() { var x=await CreateAttempt(); await Task.WhenAll(Enumerable.Range(0,2).Select(_=>Task.Run(async()=>{await using var db=fixture.CreateDbContext(); await new PaymentAttemptLifecycleService(db).SucceedAsync(x.Tenant,x.AttemptId);}))); await using var db=fixture.CreateDbContext(); var ev=await db.OutboxMessages.AsNoTracking().Where(e=>e.EstablishmentId==x.Tenant&&e.EventType=="payment-attempt-approved.v1").ToListAsync(); Assert.Single(ev,e=>PayloadTargetsAttempt(e.Payload,x.AttemptId)); Assert.Single(await db.Set<AuditEntry>().AsNoTracking().Where(e=>e.AggregateId==x.AttemptId&&e.Action=="payment_attempt.approved").ToListAsync()); }

    private static bool PayloadTargetsAttempt(string payload, Guid attemptId)
    {
        using var json = JsonDocument.Parse(payload);
        return json.RootElement.TryGetProperty("paymentAttemptId", out var value) && value.GetGuid() == attemptId;
    }
    private async Task<(Guid Tenant, Guid SessionId, Guid AttemptId)> CreateAttempt()
    {
        var c = await fixture.CreateOpenSessionAsync(); Guid tenant;
        await using (var db = fixture.CreateDbContext()) { var s = await db.Set<Appizza.Modules.Tables.TableSession>().SingleAsync(x => x.Id == c.SessionId); tenant = s.EstablishmentId; s.TotalAmount = 10m; s.RemainingAmount = 10m; await db.SaveChangesAsync(); }
        var p = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = c.SessionId, mode = "total" }, c.Device.AccessToken, Guid.NewGuid()); p.EnsureSuccessStatusCode(); using var pj = JsonDocument.Parse(await p.Content.ReadAsStringAsync()); var plan = pj.RootElement.GetProperty("planId").GetGuid(); var allocation = pj.RootElement.GetProperty("allocations")[0].GetProperty("allocationId").GetGuid(); var a = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = c.SessionId, paymentPlanId = plan, allocationIds = new[] { allocation }, paymentMethod = "cash" }, c.Device.AccessToken, Guid.NewGuid()); a.EnsureSuccessStatusCode(); using var aj = JsonDocument.Parse(await a.Content.ReadAsStringAsync()); return (tenant, c.SessionId, aj.RootElement.GetProperty("attemptId").GetGuid());
    }
}
