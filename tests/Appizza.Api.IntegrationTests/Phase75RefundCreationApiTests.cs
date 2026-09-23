using System.Net;
using System.Text.Json;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase75RefundCreationApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task ApprovedCashCreatesRefundAndObservability()
    {
        var x = await CreateApprovedAttempt(100m);
        var response = await Refund(x, 30m, "  motivo  ", Guid.NewGuid(), "cash");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var refundId = body.RootElement.GetProperty("refundId").GetGuid();
        Assert.Equal(x.AttemptId, body.RootElement.GetProperty("paymentAttemptId").GetGuid());
        Assert.Equal(30m, body.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal("motivo", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal("created", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(0m, body.RootElement.GetProperty("completedAmount").GetDecimal());
        Assert.Equal(30m, body.RootElement.GetProperty("inFlightAmount").GetDecimal());
        Assert.Equal(70m, body.RootElement.GetProperty("availableAmount").GetDecimal());
        await using var db = fixture.CreateDbContext();
        var refund = await db.Refunds.SingleAsync(r => r.Id == refundId);
        Assert.Equal(x.AttemptId, refund.PaymentAttemptId);
        Assert.Equal("motivo", refund.Reason);
        Assert.Equal(0, await db.RefundProviderExecutions.CountAsync(e => e.RefundId == refundId));
        Assert.Single(await db.AuditEntries.Where(a => a.EstablishmentId == x.Tenant && a.Action == "refund.created" && a.AggregateType == "refund" && a.AggregateId == refundId).ToListAsync());
        var events = await db.OutboxMessages.Where(a => a.EstablishmentId == x.Tenant && a.EventType == "refund-created.v1").ToListAsync();
        Assert.Single(events, e => e.Payload.Contains(refundId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidAmountIsRejected(decimal amount)
    {
        var x = await CreateApprovedAttempt(100m);
        var response = await Refund(x, amount, "reason", Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("REFUND_AMOUNT_INVALID", await fixture.ErrorCodeAsync(response));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvalidReasonIsRejected(string? reason)
    {
        var x = await CreateApprovedAttempt(100m);
        var response = await Refund(x, 10m, reason, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("REFUND_REASON_REQUIRED", await fixture.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task FinancialFormulaCountsOnlyCompletedAndInFlight()
    {
        var x = await CreateApprovedAttempt(100m);
        await SeedRefunds(x, (20m, RefundStatus.Completed), (15m, RefundStatus.Created), (10m, RefundStatus.Processing), (40m, RefundStatus.Failed), (30m, RefundStatus.Cancelled));
        var response = await Refund(x, 55m, "new", Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(20m, body.RootElement.GetProperty("completedAmount").GetDecimal());
        Assert.Equal(80m, body.RootElement.GetProperty("inFlightAmount").GetDecimal());
        Assert.Equal(0m, body.RootElement.GetProperty("availableAmount").GetDecimal());
    }

    [Fact]
    public async Task AmountExceedingAvailableIsRejected()
    {
        var x = await CreateApprovedAttempt(100m);
        await SeedRefunds(x, (30m, RefundStatus.Created));
        var response = await Refund(x, 71m, "too much", Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("REFUND_AMOUNT_EXCEEDS_AVAILABLE", await fixture.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task IdenticalReplayHasNoDuplicateEffectsAndTrimmedReasonIsCanonical()
    {
        var x = await CreateApprovedAttempt(100m); var key = Guid.NewGuid();
        var first = await Refund(x, 20m, "  motivo ", key, "cash");
        var second = await Refund(x, 20m, "motivo", key, "cash");
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        using var a = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var b = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(a.RootElement.GetProperty("refundId").GetGuid(), b.RootElement.GetProperty("refundId").GetGuid());
        await using var db = fixture.CreateDbContext();
        Assert.Single(await db.Refunds.Where(r => r.PaymentAttemptId == x.AttemptId).ToListAsync());
        Assert.Single(await db.AuditEntries.Where(r => r.EstablishmentId == x.Tenant && r.AggregateType == "refund" && r.Action == "refund.created" && r.AggregateId == a.RootElement.GetProperty("refundId").GetGuid()).ToListAsync());
        Assert.Single(await db.OutboxMessages.Where(r => r.EstablishmentId == x.Tenant && r.EventType == "refund-created.v1").ToListAsync(), r => r.Payload.Contains(a.RootElement.GetProperty("refundId").GetGuid().ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IdempotencyMismatchIsRejected()
    {
        var x = await CreateApprovedAttempt(100m); var key = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Accepted, (await Refund(x, 20m, "a", key, "cash")).StatusCode);
        var response = await Refund(x, 21m, "a", key, "cash");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", await fixture.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task ConcurrentDifferentKeysCannotOverRefund()
    {
        var x = await CreateApprovedAttempt(100m);
        var responses = await fixture.ConcurrentAsync(
            () => Refund(x, 70m, "a", Guid.NewGuid(), "cash"),
            () => Refund(x, 70m, "b", Guid.NewGuid(), "cash"));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Accepted);
        var conflict = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("REFUND_AMOUNT_EXCEEDS_AVAILABLE", await fixture.ErrorCodeAsync(conflict));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(70m, await db.Refunds.Where(r => r.PaymentAttemptId == x.AttemptId && r.Status == RefundStatus.Created).SumAsync(r => r.Amount));
    }

    [Fact]
    public async Task ConcurrentIdenticalRequestCreatesOneRefundAndOneObservabilitySet()
    {
        var x = await CreateApprovedAttempt(100m); var key = Guid.NewGuid();
        var responses = await fixture.ConcurrentAsync(
            () => Refund(x, 20m, " same ", key, "cash"),
            () => Refund(x, 20m, "same", key, "cash"));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        using var first = JsonDocument.Parse(await responses[0].Content.ReadAsStringAsync());
        using var second = JsonDocument.Parse(await responses[1].Content.ReadAsStringAsync());
        var refundId = first.RootElement.GetProperty("refundId").GetGuid();
        Assert.Equal(refundId, second.RootElement.GetProperty("refundId").GetGuid());
        await using var db = fixture.CreateDbContext();
        Assert.Single(await db.Refunds.Where(r => r.Id == refundId).ToListAsync());
        Assert.Single(await db.AuditEntries.Where(a => a.AggregateId == refundId && a.Action == "refund.created").ToListAsync());
        Assert.Single(await db.OutboxMessages.Where(o => o.EventType == "refund-created.v1").ToListAsync(), o => o.Payload.Contains(refundId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConcurrentDifferentPayloadSameKeyCreatesOneRefundAndConflict()
    {
        var x = await CreateApprovedAttempt(100m); var key = Guid.NewGuid();
        var responses = await fixture.ConcurrentAsync(
            () => Refund(x, 20m, "first", key, "cash"),
            () => Refund(x, 21m, "second", key, "cash"));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Accepted);
        var conflict = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", await fixture.ErrorCodeAsync(conflict));
        await using var db = fixture.CreateDbContext();
        Assert.Single(await db.Refunds.Where(r => r.PaymentAttemptId == x.AttemptId).ToListAsync());
    }

    [Fact]
    public async Task ClosedSessionCanBeRefundedWithoutSessionOrAttemptMutation()
    {
        var x = await CreateApprovedAttempt(100m);
        await using (var db = fixture.CreateDbContext())
        {
            var session = await db.Set<TableSession>().SingleAsync(s => s.Id == x.SessionId);
            session.Status = "closed";
            await db.SaveChangesAsync();
        }
        var before = await Snapshot(x);
        var response = await Refund(x, 20m, "closed", Guid.NewGuid(), "cash");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var after = await Snapshot(x);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task UnauthenticatedRefundIsRejected()
    {
        var x = await CreateApprovedAttempt(100m);
        var response = await fixture.PostAsync($"api/v1/payments/{x.AttemptId}/refunds", new { amount = 10m, reason = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ForeignTenantIsNotDisclosedAndCreatesNoRows()
    {
        var owner = await CreateApprovedAttempt(100m); var other = await fixture.CreateOpenSessionAsync();
        var tenantB = await fixture.CreateUserTokenAsync(await EstablishmentId(other.SessionId), "payments.refund");
        var before = await Counts(owner.Tenant);
        var response = await fixture.PostWithIdempotencyAsync($"api/v1/payments/{owner.AttemptId}/refunds", new { amount = 10m, reason = "x" }, tenantB, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await Counts(owner.Tenant));
    }

    [Fact]
    public async Task EmployeeWithoutPermissionAndTabletAreForbidden()
    {
        var x = await CreateApprovedAttempt(100m);
        var noPermission = await fixture.CreateUserTokenAsync(x.Tenant);
        var denied = await Refund(x, 10m, "x", Guid.NewGuid(), "cash", noPermission);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("INSUFFICIENT_PERMISSION", await fixture.ErrorCodeAsync(denied));
        var tablet = await fixture.CreateOpenSessionAsync();
        var tabletResponse = await fixture.PostWithIdempotencyAsync($"api/v1/payments/{x.AttemptId}/refunds", new { amount = 10m, reason = "x" }, tablet.Device.AccessToken, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, tabletResponse.StatusCode);
    }

    [Fact]
    public async Task NonCashCreatesExecutionButDoesNotCallProvider()
    {
        var x = await CreateApprovedAttempt(100m, PaymentMethod.Pix);
        var response = await Refund(x, 10m, "pix", Guid.NewGuid(), "pix");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var refundId = body.RootElement.GetProperty("refundId").GetGuid();
        await using var db = fixture.CreateDbContext();
        var execution = await db.RefundProviderExecutions.SingleAsync(e => e.RefundId == refundId);
        Assert.Equal(x.Tenant, execution.EstablishmentId);
        Assert.Equal(RefundProviderExecutionStatus.Pending, execution.Status);
        Assert.StartsWith("refund:", execution.ProviderIdempotencyKey, StringComparison.Ordinal);
    }

    private async Task<HttpResponseMessage> Refund(AttemptData x, decimal amount, string? reason, Guid key, string method = "cash", string? token = null) =>
        await fixture.PostWithIdempotencyAsync($"api/v1/payments/{x.AttemptId}/refunds", new { amount, reason }, token ?? await fixture.CreateUserTokenAsync(x.Tenant, "payments.refund"), key);

    private async Task<AttemptData> CreateApprovedAttempt(decimal amount, PaymentMethod method = PaymentMethod.Cash)
    {
        var c = await fixture.CreateOpenSessionAsync(); await SetTotal(c.SessionId, amount);
        var p = await CreatePlan(c, amount);
        var created = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = c.SessionId, paymentPlanId = p.PlanId, allocationIds = new[] { p.AllocationId }, paymentMethod = method.ToString().ToLowerInvariant() }, c.Device.AccessToken, Guid.NewGuid());
        created.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync()); var id = json.RootElement.GetProperty("attemptId").GetGuid();
        var tenant = await EstablishmentId(c.SessionId); await fixture.CreatePaymentLifecycleService().SucceedAsync(tenant, id);
        return new(tenant, c.SessionId, id);
    }

    private async Task SeedRefunds(AttemptData x, params (decimal Amount, RefundStatus Status)[] values)
    { await using var db = fixture.CreateDbContext(); var now = DateTimeOffset.UtcNow; foreach (var (amount, status) in values) db.Refunds.Add(new Refund { Id = Guid.NewGuid(), EstablishmentId = x.Tenant, PaymentAttemptId = x.AttemptId, Amount = amount, Status = status, Reason = "history", CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync(); }
    private async Task SetTotal(Guid id, decimal amount) { await using var db = fixture.CreateDbContext(); var s = await db.Set<TableSession>().SingleAsync(x => x.Id == id); s.TotalAmount = amount; s.RemainingAmount = amount; await db.SaveChangesAsync(); }
    private async Task<(int Refunds, int Audits, int Outbox)> Counts(Guid tenant) { await using var db = fixture.CreateDbContext(); return (await db.Refunds.CountAsync(r => r.EstablishmentId == tenant), await db.AuditEntries.CountAsync(a => a.EstablishmentId == tenant && a.Action == "refund.created"), await db.OutboxMessages.CountAsync(o => o.EstablishmentId == tenant && o.EventType == "refund-created.v1")); }
    private async Task<SnapshotData> Snapshot(AttemptData x)
    { await using var db = fixture.CreateDbContext(); var session = await db.Set<TableSession>().AsNoTracking().SingleAsync(s => s.Id == x.SessionId); var attempt = await db.Set<PaymentAttempt>().AsNoTracking().SingleAsync(a => a.Id == x.AttemptId); return new(session.PaidAmount, session.ReservedAmount, session.Status, attempt.Amount, attempt.Status); }
    private async Task<(Guid PlanId, Guid AllocationId)> CreatePlan(Phase1ApiFixture.OpenSessionContext c, decimal amount) { var r = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = c.SessionId, mode = "custom_amount", amount }, c.Device.AccessToken, Guid.NewGuid()); r.EnsureSuccessStatusCode(); using var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return (j.RootElement.GetProperty("planId").GetGuid(), j.RootElement.GetProperty("allocations")[0].GetProperty("allocationId").GetGuid()); }
    private async Task<Guid> EstablishmentId(Guid sessionId) { await using var db = fixture.CreateDbContext(); return await db.Set<TableSession>().Where(x => x.Id == sessionId).Select(x => x.EstablishmentId).SingleAsync(); }
    private sealed record AttemptData(Guid Tenant, Guid SessionId, Guid AttemptId);
    private sealed record SnapshotData(decimal Paid, decimal Reserved, string Status, decimal Amount, PaymentAttemptStatus AttemptStatus);
}
