using System.Text.Json;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase75RefundReadCompositionTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task ReadCompositionTwentyFifteenTenExcludesFailedAndCancelled()
    {
        var x = await CreateApprovedAttempt(100m);
        await using (var db = fixture.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            db.Refunds.AddRange(
                Refund(x, 20m, RefundStatus.Completed, now),
                Refund(x, 15m, RefundStatus.Created, now),
                Refund(x, 10m, RefundStatus.Processing, now),
                Refund(x, 7m, RefundStatus.Failed, now),
                Refund(x, 5m, RefundStatus.Cancelled, now));
            await db.SaveChangesAsync();
        }
        var token = await fixture.CreateUserTokenAsync(x.Tenant, "payments.view");
        var response = await fixture.GetAsync($"api/v1/operations/sessions/{x.SessionId}/payments", token);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var attempt = json.RootElement.GetProperty("paymentAttempts").EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == x.AttemptId);
        var summary = attempt.GetProperty("refundSummary");
        Assert.Equal(20m, summary.GetProperty("completedAmount").GetDecimal());
        Assert.Equal(25m, summary.GetProperty("inFlightAmount").GetDecimal());
        Assert.Equal(55m, summary.GetProperty("availableAmount").GetDecimal());
        Assert.Equal(5, attempt.GetProperty("refunds").GetArrayLength());
    }

    [Fact]
    public async Task UnknownExecutionIsVisibleAsReconciliationAndConsumesInflight()
    {
        var x = await CreateApprovedAttempt(100m);
        await using (var db = fixture.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow; var refundId = Guid.NewGuid();
            db.Refunds.Add(Refund(x, 30m, RefundStatus.Processing, now, refundId));
            db.RefundProviderExecutions.Add(new RefundProviderExecution { Id = Guid.NewGuid(), EstablishmentId = x.Tenant, RefundId = refundId, Provider = "fake", ProviderIdempotencyKey = $"refund:{refundId:N}", Status = RefundProviderExecutionStatus.Unknown, ReconciliationRequired = true, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }
        var token = await fixture.CreateUserTokenAsync(x.Tenant, "payments.view");
        var response = await fixture.GetAsync($"api/v1/operations/sessions/{x.SessionId}/payments", token); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var attempt = json.RootElement.GetProperty("paymentAttempts").EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == x.AttemptId); var summary = attempt.GetProperty("refundSummary");
        Assert.Equal(30m, summary.GetProperty("inFlightAmount").GetDecimal()); Assert.Equal(70m, summary.GetProperty("availableAmount").GetDecimal()); Assert.True(attempt.GetProperty("refunds")[0].GetProperty("reconciliationRequired").GetBoolean());
    }

    private static Refund Refund(AttemptData x, decimal amount, RefundStatus status, DateTimeOffset now, Guid? id = null) => new() { Id = id ?? Guid.NewGuid(), EstablishmentId = x.Tenant, PaymentAttemptId = x.AttemptId, Amount = amount, Status = status, Reason = "test", CreatedAt = now, UpdatedAt = now };
    private async Task<AttemptData> CreateApprovedAttempt(decimal amount)
    { var c = await fixture.CreateOpenSessionAsync(); await using (var db = fixture.CreateDbContext()) { var s = await db.Set<TableSession>().SingleAsync(x => x.Id == c.SessionId); s.TotalAmount = amount; s.RemainingAmount = amount; await db.SaveChangesAsync(); } var plan = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = c.SessionId, mode = "custom_amount", amount }, c.Device.AccessToken, Guid.NewGuid()); using var p = JsonDocument.Parse(await plan.Content.ReadAsStringAsync()); var created = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = c.SessionId, paymentPlanId = p.RootElement.GetProperty("planId").GetGuid(), allocationIds = new[] { p.RootElement.GetProperty("allocations")[0].GetProperty("allocationId").GetGuid() }, paymentMethod = "cash" }, c.Device.AccessToken, Guid.NewGuid()); using var a = JsonDocument.Parse(await created.Content.ReadAsStringAsync()); var tenant = await Tenant(c.SessionId); var id = a.RootElement.GetProperty("attemptId").GetGuid(); await fixture.CreatePaymentLifecycleService().SucceedAsync(tenant, id); return new(tenant, c.SessionId, id); }
    private async Task<Guid> Tenant(Guid sessionId) { await using var db = fixture.CreateDbContext(); return await db.Set<TableSession>().Where(s => s.Id == sessionId).Select(s => s.EstablishmentId).SingleAsync(); }
    private sealed record AttemptData(Guid Tenant, Guid SessionId, Guid AttemptId);
}
