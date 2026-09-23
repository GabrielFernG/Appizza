using System.Text.Json;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Appizza.Payments.Application;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase75RefundProviderCrashRecoveryTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task BoundaryAPersistedExecutionIsClaimedAndConverged()
    {
        var x = await CreateNonCashRefund(); var provider = new DeterministicRefundProvider("completed", "completed");
        await using var db = fixture.CreateDbContext(); var service = new RefundProviderRecoveryService(db, provider);
        Assert.True(await service.ProcessOneAsync(x.Tenant, x.RefundId)); Assert.Equal(1, provider.RefundAsyncCallCount);
        await AssertRefundState(x, RefundStatus.Completed, RefundProviderExecutionStatus.Completed);
    }

    [Fact]
    public async Task BoundaryBExpiredLeaseCanBeTakenOver()
    {
        var x = await CreateNonCashRefund();
        await using (var db = fixture.CreateDbContext()) { var e = await db.RefundProviderExecutions.SingleAsync(e => e.RefundId == x.RefundId); e.ClaimedBy = Guid.NewGuid(); e.ClaimedUntil = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync(); }
        var provider = new DeterministicRefundProvider("completed", "completed"); await using var recovery = fixture.CreateDbContext();
        Assert.True(await new RefundProviderRecoveryService(recovery, provider).ProcessOneAsync(x.Tenant, x.RefundId)); Assert.Equal(1, provider.RefundAsyncCallCount);
    }

    [Fact]
    public async Task BoundaryCRecoveryUsesStableKeyAfterProviderAmbiguity()
    {
        var x = await CreateNonCashRefund(); var provider = new DeterministicRefundProvider("unknown", "completed");
        await using var db = fixture.CreateDbContext(); var service = new RefundProviderRecoveryService(db, provider);
        await service.ProcessOneAsync(x.Tenant, x.RefundId); await using var db2 = fixture.CreateDbContext(); await new RefundProviderRecoveryService(db2, provider).ProcessOneAsync(x.Tenant, x.RefundId);
        Assert.Equal(1, provider.RefundAsyncCallCount); Assert.Equal(1, provider.LookupRefundAsyncCallCount); Assert.Single(provider.ObservedKeys.Distinct());
    }

    [Fact]
    public async Task BoundaryDOutcomeObservedConvergesWithoutProviderCall()
    {
        var x = await CreateNonCashRefund(); await using (var db = fixture.CreateDbContext()) { var e = await db.RefundProviderExecutions.SingleAsync(e => e.RefundId == x.RefundId); e.Status = RefundProviderExecutionStatus.OutcomeObserved; e.NormalizedOutcome = "completed"; await db.SaveChangesAsync(); }
        var provider = new DeterministicRefundProvider("unknown", "unknown"); await using var recovery = fixture.CreateDbContext(); await new RefundProviderRecoveryService(recovery, provider).ProcessOneAsync(x.Tenant, x.RefundId);
        Assert.Equal(0, provider.RefundAsyncCallCount); await AssertRefundState(x, RefundStatus.Completed, RefundProviderExecutionStatus.Completed);
    }

    [Fact]
    public async Task BoundaryETerminalLifecycleWithoutFlagConvergesIdempotently()
    {
        var x = await CreateNonCashRefund(); await using (var db = fixture.CreateDbContext()) { var r = await db.Refunds.SingleAsync(r => r.Id == x.RefundId); r.Status = RefundStatus.Completed; var e = await db.RefundProviderExecutions.SingleAsync(e => e.RefundId == x.RefundId); e.Status = RefundProviderExecutionStatus.Completed; e.LifecycleApplied = false; await db.SaveChangesAsync(); }
        var provider = new DeterministicRefundProvider("unknown", "unknown"); await using var recovery = fixture.CreateDbContext(); await new RefundProviderRecoveryService(recovery, provider).ProcessOneAsync(x.Tenant, x.RefundId);
        Assert.Equal(0, provider.RefundAsyncCallCount); await using var db2 = fixture.CreateDbContext(); Assert.True(await db2.RefundProviderExecutions.Where(e => e.RefundId == x.RefundId).Select(e => e.LifecycleApplied).SingleAsync());
    }

    [Fact]
    public async Task BoundaryGUnknownLookupCompletedConvergesWithoutRefundRetry()
    {
        var x = await CreateNonCashRefund(); var provider = new DeterministicRefundProvider("unknown", "completed"); await using var first = fixture.CreateDbContext(); var service = new RefundProviderRecoveryService(first, provider); await service.ProcessOneAsync(x.Tenant, x.RefundId); await using var second = fixture.CreateDbContext(); await new RefundProviderRecoveryService(second, provider).ProcessOneAsync(x.Tenant, x.RefundId); Assert.Equal(1, provider.RefundAsyncCallCount); Assert.Equal(1, provider.LookupRefundAsyncCallCount); await AssertRefundState(x, RefundStatus.Completed, RefundProviderExecutionStatus.Completed);
    }

    [Fact]
    public async Task BoundaryHUnknownLookupFailedConvergesWithoutRefundRetry()
    {
        var x = await CreateNonCashRefund(); var provider = new DeterministicRefundProvider("unknown", "failed"); await using var first = fixture.CreateDbContext(); var service = new RefundProviderRecoveryService(first, provider); await service.ProcessOneAsync(x.Tenant, x.RefundId); await using var second = fixture.CreateDbContext(); await new RefundProviderRecoveryService(second, provider).ProcessOneAsync(x.Tenant, x.RefundId); Assert.Equal(1, provider.RefundAsyncCallCount); Assert.Equal(1, provider.LookupRefundAsyncCallCount); await AssertRefundState(x, RefundStatus.Failed, RefundProviderExecutionStatus.TerminalFailure);
    }

    [Fact]
    public async Task TwoRecoveryServicesClaimOnlyOneExecution()
    {
        var x = await CreateNonCashRefund(); var provider = new DeterministicRefundProvider("completed", "completed"); await using var a = fixture.CreateDbContext(); await using var b = fixture.CreateDbContext();
        var results = await Task.WhenAll(new RefundProviderRecoveryService(a, provider).ProcessOneAsync(x.Tenant, x.RefundId), new RefundProviderRecoveryService(b, provider).ProcessOneAsync(x.Tenant, x.RefundId));
        Assert.Contains(true, results); Assert.Equal(1, provider.RefundAsyncCallCount);
    }

    [Fact]
    public async Task StaleClaimantCannotOverwriteTakeoverOutcome()
    {
        var x = await CreateNonCashRefund();
        await using (var db = fixture.CreateDbContext()) { var e = await db.RefundProviderExecutions.SingleAsync(e => e.RefundId == x.RefundId); e.ClaimedBy = Guid.NewGuid(); e.ClaimedUntil = DateTimeOffset.UtcNow.AddMinutes(-1); e.Status = RefundProviderExecutionStatus.Processing; await db.SaveChangesAsync(); }
        var provider = new DeterministicRefundProvider("completed", "completed"); await using var db2 = fixture.CreateDbContext(); await new RefundProviderRecoveryService(db2, provider).ProcessOneAsync(x.Tenant, x.RefundId);
        await using var verify = fixture.CreateDbContext(); var execution = await verify.RefundProviderExecutions.SingleAsync(e => e.RefundId == x.RefundId); Assert.Equal(RefundProviderExecutionStatus.Completed, execution.Status); Assert.Null(execution.ClaimedBy); Assert.True(execution.LifecycleApplied);
    }

    [Fact]
    public async Task UnknownConcurrentReconciliationHasNoRefundCall()
    {
        var x = await CreateNonCashRefund(); await using (var db = fixture.CreateDbContext()) { var e = await db.RefundProviderExecutions.SingleAsync(e => e.RefundId == x.RefundId); e.Status = RefundProviderExecutionStatus.Unknown; e.ReconciliationRequired = true; await db.SaveChangesAsync(); }
        var provider = new DeterministicRefundProvider("unknown", "completed"); await using var a = fixture.CreateDbContext(); await using var b = fixture.CreateDbContext(); var results = await Task.WhenAll(new RefundProviderRecoveryService(a, provider).ProcessOneAsync(x.Tenant, x.RefundId), new RefundProviderRecoveryService(b, provider).ProcessOneAsync(x.Tenant, x.RefundId));
        Assert.Contains(true, results); Assert.Equal(0, provider.RefundAsyncCallCount); Assert.Equal(1, provider.LookupRefundAsyncCallCount); await AssertRefundState(x, RefundStatus.Completed, RefundProviderExecutionStatus.Completed);
    }

    private async Task<Seed> CreateNonCashRefund()
    {
        var c = await fixture.CreateOpenSessionAsync(); await using (var db = fixture.CreateDbContext()) { var s = await db.Set<TableSession>().SingleAsync(s => s.Id == c.SessionId); s.TotalAmount = 10m; s.RemainingAmount = 10m; await db.SaveChangesAsync(); }
        var plan = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = c.SessionId, mode = "total" }, c.Device.AccessToken, Guid.NewGuid()); using var p = JsonDocument.Parse(await plan.Content.ReadAsStringAsync());
        var attempt = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = c.SessionId, paymentPlanId = p.RootElement.GetProperty("planId").GetGuid(), allocationIds = new[] { p.RootElement.GetProperty("allocations")[0].GetProperty("allocationId").GetGuid() }, paymentMethod = "pix" }, c.Device.AccessToken, Guid.NewGuid()); using var a = JsonDocument.Parse(await attempt.Content.ReadAsStringAsync()); var tenant = await Tenant(c.SessionId); var attemptId = a.RootElement.GetProperty("attemptId").GetGuid(); await fixture.CreatePaymentLifecycleService().SucceedAsync(tenant, attemptId); var token = await fixture.CreateUserTokenAsync(tenant, "payments.refund"); var refund = await fixture.PostWithIdempotencyAsync($"api/v1/payments/{attemptId}/refunds", new { amount = 5m, reason = "test" }, token, Guid.NewGuid()); using var r = JsonDocument.Parse(await refund.Content.ReadAsStringAsync()); return new(tenant, r.RootElement.GetProperty("refundId").GetGuid());
    }
    private async Task AssertRefundState(Seed x, RefundStatus refund, RefundProviderExecutionStatus execution) { await using var db = fixture.CreateDbContext(); Assert.Equal(refund, await db.Refunds.Where(r => r.Id == x.RefundId).Select(r => r.Status).SingleAsync()); Assert.Equal(execution, await db.RefundProviderExecutions.Where(e => e.RefundId == x.RefundId).Select(e => e.Status).SingleAsync()); }
    private async Task<Guid> Tenant(Guid session) { await using var db = fixture.CreateDbContext(); return await db.Set<TableSession>().Where(s => s.Id == session).Select(s => s.EstablishmentId).SingleAsync(); }
    private sealed record Seed(Guid Tenant, Guid RefundId);
}
