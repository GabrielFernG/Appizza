using System.Net;
using System.Text.Json;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase75RefundCashConfirmationApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task CashCreatedRefundCanBeCompletedExactlyOnce()
    {
        var x = await CreateApprovedCashAttempt();
        var create = await PostRefund(x, Guid.NewGuid());
        create.EnsureSuccessStatusCode();
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var refundId = created.RootElement.GetProperty("refundId").GetGuid();
        var token = await fixture.CreateUserTokenAsync(x.Tenant, "payments.refund");
        var first = await fixture.PostWithIdempotencyAsync($"api/v1/payments/refunds/{refundId}/confirm-cash", new { }, token, Guid.NewGuid());
        var second = await fixture.PostWithIdempotencyAsync($"api/v1/payments/refunds/{refundId}/confirm-cash", new { }, token, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(RefundStatus.Completed, await db.Refunds.Where(r => r.Id == refundId).Select(r => r.Status).SingleAsync());
        Assert.Single(await db.AuditEntries.Where(a => a.AggregateId == refundId && a.Action == "refund.completed").ToListAsync());
        Assert.Single((await db.OutboxMessages.Where(o => o.EventType == "refund-completed.v1").ToListAsync()), o => o.Payload.Contains(refundId.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await db.RefundProviderExecutions.Where(e => e.RefundId == refundId).ToListAsync());
    }

    [Theory]
    [InlineData(PaymentMethod.Pix)]
    [InlineData(PaymentMethod.Credit)]
    [InlineData(PaymentMethod.Debit)]
    public async Task NonCashRefundIsRejected(PaymentMethod method)
    {
        var x = await CreateApprovedAttempt(method); var create = await PostRefund(x, Guid.NewGuid());
        using var body = JsonDocument.Parse(await create.Content.ReadAsStringAsync()); var id = body.RootElement.GetProperty("refundId").GetGuid();
        var token = await fixture.CreateUserTokenAsync(x.Tenant, "payments.refund");
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/payments/refunds/{id}/confirm-cash", new { }, token, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Equal("REFUND_NOT_CASH", await fixture.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task EmployeeWithoutPermissionAndForeignTenantAreRejected()
    {
        var x = await CreateApprovedCashAttempt(); var create = await PostRefund(x, Guid.NewGuid()); using var body = JsonDocument.Parse(await create.Content.ReadAsStringAsync()); var id = body.RootElement.GetProperty("refundId").GetGuid();
        var denied = await fixture.CreateUserTokenAsync(x.Tenant);
        var forbidden = await fixture.PostWithIdempotencyAsync($"api/v1/payments/refunds/{id}/confirm-cash", new { }, denied, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var other = await fixture.CreateOpenSessionAsync(); var foreign = await fixture.CreateUserTokenAsync(await Tenant(other.SessionId), "payments.refund");
        var missing = await fixture.PostWithIdempotencyAsync($"api/v1/payments/refunds/{id}/confirm-cash", new { }, foreign, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task PaymentDetailsExposeAuthoritativeRefundSummary()
    {
        var x = await CreateApprovedCashAttempt(); await PostRefund(x, Guid.NewGuid());
        var token = await fixture.CreateUserTokenAsync(x.Tenant, "payments.view");
        var response = await fixture.GetAsync($"api/v1/operations/sessions/{x.SessionId}/payments", token); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var attempt = json.RootElement.GetProperty("paymentAttempts")[0];
        Assert.Equal(0m, attempt.GetProperty("refundSummary").GetProperty("completedAmount").GetDecimal());
        Assert.Equal(10m, attempt.GetProperty("refundSummary").GetProperty("inFlightAmount").GetDecimal());
        Assert.Equal(0m, attempt.GetProperty("refundSummary").GetProperty("availableAmount").GetDecimal());
        Assert.False(attempt.GetProperty("refunds")[0].GetProperty("reconciliationRequired").GetBoolean());
    }

    private async Task<HttpResponseMessage> PostRefund(AttemptData x, Guid key) => await fixture.PostWithIdempotencyAsync($"api/v1/payments/{x.AttemptId}/refunds", new { amount = 10m, reason = "cash" }, await fixture.CreateUserTokenAsync(x.Tenant, "payments.refund"), key);
    private async Task<AttemptData> CreateApprovedCashAttempt() => await CreateApprovedAttempt(PaymentMethod.Cash);
    private async Task<AttemptData> CreateApprovedAttempt(PaymentMethod method)
    { var c = await fixture.CreateOpenSessionAsync(); await using (var db = fixture.CreateDbContext()) { var s = await db.Set<TableSession>().SingleAsync(x => x.Id == c.SessionId); s.TotalAmount = 10m; s.RemainingAmount = 10m; await db.SaveChangesAsync(); } var plan = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = c.SessionId, mode = "total" }, c.Device.AccessToken, Guid.NewGuid()); using var p = JsonDocument.Parse(await plan.Content.ReadAsStringAsync()); var created = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = c.SessionId, paymentPlanId = p.RootElement.GetProperty("planId").GetGuid(), allocationIds = new[] { p.RootElement.GetProperty("allocations")[0].GetProperty("allocationId").GetGuid() }, paymentMethod = method.ToString().ToLowerInvariant() }, c.Device.AccessToken, Guid.NewGuid()); using var a = JsonDocument.Parse(await created.Content.ReadAsStringAsync()); var tenant = await Tenant(c.SessionId); var id = a.RootElement.GetProperty("attemptId").GetGuid(); await fixture.CreatePaymentLifecycleService().SucceedAsync(tenant, id); return new(tenant, c.SessionId, id); }
    private async Task<Guid> Tenant(Guid sessionId) { await using var db = fixture.CreateDbContext(); return await db.Set<TableSession>().Where(s => s.Id == sessionId).Select(s => s.EstablishmentId).SingleAsync(); }
    private sealed record AttemptData(Guid Tenant, Guid SessionId, Guid AttemptId);
}
