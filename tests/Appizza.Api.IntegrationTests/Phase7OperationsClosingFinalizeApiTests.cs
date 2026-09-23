using System.Net;
using System.Text.Json;
using Appizza.Modules.Auditing;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase7OperationsClosingFinalizeApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task FinalizePaidSessionReturnsClosedAndWritesExactlyOneObservabilitySet()
    {
        var s = await PaidSession();
        var token = await fixture.CreateUserTokenAsync(s.Tenant, "closing.finalize");
        var before = await Counts(s.SessionId, s.Tenant);
        var response = await Finalize(s, token, s.Version, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("closed", json.RootElement.GetProperty("status").GetString());
        await using var db = fixture.CreateDbContext();
        var session = await db.Set<TableSession>().AsNoTracking().SingleAsync(x => x.Id == s.SessionId);
        Assert.Equal("closed", session.Status);
        Assert.Equal(s.Version + 1, session.Version);
        var after = await Counts(s.SessionId, s.Tenant);
        Assert.Equal(1, after.Audit - before.Audit);
        Assert.Equal(1, after.Outbox - before.Outbox);
        var finalized = await db.OutboxMessages.Where(x => x.EstablishmentId == s.Tenant && x.EventType == "session-closing-finalized.v1").ToListAsync();
        Assert.Single(finalized, x => PayloadTargetsSession(x.Payload, s.SessionId));
    }

    [Fact]
    public async Task FinalizeForeignTenantIsNotDisclosed() { var foreign = await PaidSession(); var local = await PaidSession(); var token = await fixture.CreateUserTokenAsync(local.Tenant, "closing.finalize"); var r = await Finalize(foreign, token, foreign.Version, Guid.NewGuid()); Assert.Equal(HttpStatusCode.NotFound, r.StatusCode); Assert.Equal("paid", await Status(foreign.SessionId)); }

    [Fact]
    public async Task FinalizeRequiresPermission() { var s = await PaidSession(); var token = await fixture.CreateUserTokenAsync(s.Tenant); var r = await Finalize(s, token, s.Version, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode); Assert.Equal("INSUFFICIENT_PERMISSION", await fixture.ErrorCodeAsync(r)); }

    [Fact]
    public async Task FinalizeUnknownSessionReturnsNotFound() { var s = await PaidSession(); var token = await fixture.CreateUserTokenAsync(s.Tenant, "closing.finalize"); var r = await fixture.PostWithIdempotencyAsync($"api/v1/operations/sessions/{Guid.NewGuid()}/closing/finalize", new { expectedVersion = s.Version }, token, Guid.NewGuid()); Assert.Equal(HttpStatusCode.NotFound, r.StatusCode); }

    [Fact]
    public async Task FinalizeStaleVersionReturnsConcurrencyConflict() { var s = await PaidSession(); var token = await fixture.CreateUserTokenAsync(s.Tenant, "closing.finalize"); var r = await Finalize(s, token, s.Version - 1, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Equal("CONCURRENCY_CONFLICT", await fixture.ErrorCodeAsync(r)); Assert.Equal("paid", await Status(s.SessionId)); }

    [Theory]
    [InlineData("closing")]
    [InlineData("awaiting_payment")]
    [InlineData("partially_paid")]
    [InlineData("closed")]
    public async Task FinalizeRejectsNonPaidSourceStates(string status) { var s = await PaidSession(status); var token = await fixture.CreateUserTokenAsync(s.Tenant, "closing.finalize"); var r = await Finalize(s, token, s.Version, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Equal("SESSION_INVALID_STATE", await fixture.ErrorCodeAsync(r)); }

    [Fact]
    public async Task FinalizeRejectsOutstandingAmount() { var s = await PaidSession(); var version = await SetFinancial(s.SessionId, 1m, 0m); var token = await fixture.CreateUserTokenAsync(s.Tenant, "closing.finalize"); var r = await Finalize(s, token, version, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Equal("CLOSING_FINANCIAL_STATE_INCOMPLETE", await fixture.ErrorCodeAsync(r)); }

    [Fact]
    public async Task FinalizeRejectsReservedAmount() { var s = await PaidSession(); var version = await SetFinancial(s.SessionId, 0m, 1m); var token = await fixture.CreateUserTokenAsync(s.Tenant, "closing.finalize"); var r = await Finalize(s, token, version, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Equal("CLOSING_FINANCIAL_STATE_INCOMPLETE", await fixture.ErrorCodeAsync(r)); }

    [Theory]
    [InlineData(PaymentAttemptStatus.Created)]
    [InlineData(PaymentAttemptStatus.AwaitingCustomerAction)]
    [InlineData(PaymentAttemptStatus.Processing)]
    [InlineData(PaymentAttemptStatus.Unknown)]
    public async Task FinalizeRejectsBlockingAttempt(PaymentAttemptStatus status) { var s = await PaidSession(); await AddAttempt(s, status); var token = await fixture.CreateUserTokenAsync(s.Tenant, "closing.finalize"); var r = await Finalize(s, token, s.Version, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Equal("CLOSING_PAYMENT_PROCESSING_INCOMPLETE", await fixture.ErrorCodeAsync(r)); }

    [Theory]
    [InlineData(PaymentProviderExecutionStatus.Pending)]
    [InlineData(PaymentProviderExecutionStatus.Processing)]
    [InlineData(PaymentProviderExecutionStatus.AwaitingCustomerAction)]
    [InlineData(PaymentProviderExecutionStatus.Unknown)]
    public async Task FinalizeRejectsBlockingExecution(PaymentProviderExecutionStatus status) { var s = await PaidSession(); var attempt = await AddAttempt(s, PaymentAttemptStatus.Approved); await AddExecution(s, attempt, status, false); var token = await fixture.CreateUserTokenAsync(s.Tenant, "closing.finalize"); var r = await Finalize(s, token, s.Version, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Equal("CLOSING_PAYMENT_PROCESSING_INCOMPLETE", await fixture.ErrorCodeAsync(r)); }

    [Fact]
    public async Task FinalizeRejectsReconciliationRequiredExecution() { var s = await PaidSession(); var attempt = await AddAttempt(s, PaymentAttemptStatus.Approved); await AddExecution(s, attempt, PaymentProviderExecutionStatus.TerminalFailure, true); var token = await fixture.CreateUserTokenAsync(s.Tenant, "closing.finalize"); var r = await Finalize(s, token, s.Version, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Equal("CLOSING_PAYMENT_PROCESSING_INCOMPLETE", await fixture.ErrorCodeAsync(r)); }

    [Fact]
    public async Task FinalizeIdenticalReplayDoesNotDuplicateEffects() { var s = await PaidSession(); var token = await fixture.CreateUserTokenAsync(s.Tenant, "closing.finalize"); var key = Guid.NewGuid(); var first = await Finalize(s, token, s.Version, key); var second = await Finalize(s, token, s.Version, key); Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.Equal(HttpStatusCode.OK, second.StatusCode); await using var db = fixture.CreateDbContext(); Assert.Equal(1, await db.Set<AuditEntry>().CountAsync(x => x.AggregateId == s.SessionId && x.Action == "closing.finalize")); var finalized = await db.OutboxMessages.Where(x => x.EstablishmentId == s.Tenant && x.EventType == "session-closing-finalized.v1").ToListAsync(); Assert.Single(finalized, x => PayloadTargetsSession(x.Payload, s.SessionId)); Assert.Equal(s.Version + 1, await db.Set<TableSession>().Where(x => x.Id == s.SessionId).Select(x => x.Version).SingleAsync()); }

    [Fact]
    public async Task FinalizeIdempotencyMismatchReturnsConflict() { var s = await PaidSession(); var token = await fixture.CreateUserTokenAsync(s.Tenant, "closing.finalize"); var key = Guid.NewGuid(); _ = await Finalize(s, token, s.Version, key); var r = await Finalize(s, token, s.Version + 1, key); Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", await fixture.ErrorCodeAsync(r)); }

    [Fact]
    public async Task FinalizeDoesNotMutatePaymentHistoryOrDiningTable() { var s = await PaidSession(); var token = await fixture.CreateUserTokenAsync(s.Tenant, "closing.finalize"); await using var before = fixture.CreateDbContext(); var tableBefore = await before.Set<DiningTable>().AsNoTracking().SingleAsync(x => x.Id == s.TableId); var plans = await before.Set<PaymentPlan>().AsNoTracking().CountAsync(x => x.TableSessionId == s.SessionId); var attempts = await before.Set<PaymentAttempt>().AsNoTracking().CountAsync(x => x.TableSessionId == s.SessionId); var r = await Finalize(s, token, s.Version, Guid.NewGuid()); Assert.Equal(HttpStatusCode.OK, r.StatusCode); await using var after = fixture.CreateDbContext(); var tableAfter = await after.Set<DiningTable>().AsNoTracking().SingleAsync(x => x.Id == s.TableId); Assert.Equal(tableBefore.Status, tableAfter.Status); Assert.Equal(plans, await after.Set<PaymentPlan>().CountAsync(x => x.TableSessionId == s.SessionId)); Assert.Equal(attempts, await after.Set<PaymentAttempt>().CountAsync(x => x.TableSessionId == s.SessionId)); }

    private async Task<Seed> PaidSession(string status = "paid") { var c = await fixture.CreateOpenSessionAsync(); await using var db = fixture.CreateDbContext(); var s = await db.Set<TableSession>().SingleAsync(x => x.Id == c.SessionId); s.Status = status; s.TotalAmount = 10m; s.PaidAmount = 10m; s.RemainingAmount = 0m; s.ReservedAmount = 0m; s.PaidAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); return new(c.SessionId, s.EstablishmentId, s.DiningTableId, s.Version); }
    private async Task<long> SetFinancial(Guid id, decimal outstanding, decimal reserved) { await using var db = fixture.CreateDbContext(); var s = await db.Set<TableSession>().SingleAsync(x => x.Id == id); s.RemainingAmount = outstanding; s.ReservedAmount = reserved; await db.SaveChangesAsync(); return s.Version; }
    private async Task<PaymentAttempt> AddAttempt(Seed s, PaymentAttemptStatus status) { await using var db = fixture.CreateDbContext(); var a = new PaymentAttempt { Id = Guid.NewGuid(), EstablishmentId = s.Tenant, TableSessionId = s.SessionId, Method = PaymentMethod.Pix, Status = status, Amount = 10m, ReservedAmount = 0m, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }; db.Add(a); await db.SaveChangesAsync(); return a; }
    private async Task AddExecution(Seed s, PaymentAttempt a, PaymentProviderExecutionStatus status, bool reconciliation) { await using var db = fixture.CreateDbContext(); db.Add(new PaymentProviderExecution { Id = Guid.NewGuid(), EstablishmentId = s.Tenant, PaymentAttemptId = a.Id, Provider = "test", ProviderIdempotencyKey = Guid.NewGuid().ToString(), Status = status, ReconciliationRequired = reconciliation, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(); }
    private async Task<string> Status(Guid id) { await using var db = fixture.CreateDbContext(); return await db.Set<TableSession>().Where(x => x.Id == id).Select(x => x.Status).SingleAsync(); }
    private Task<HttpResponseMessage> Finalize(Seed s, string token, long version, Guid key) => fixture.PostWithIdempotencyAsync($"api/v1/operations/sessions/{s.SessionId}/closing/finalize", new { expectedVersion = version }, token, key);
    private async Task<CountsSnapshot> Counts(Guid id, Guid tenant) { await using var db = fixture.CreateDbContext(); var messages = await db.OutboxMessages.Where(x => x.EstablishmentId == tenant).ToListAsync(); return new(await db.Set<AuditEntry>().CountAsync(x => x.AggregateId == id), messages.Count(x => PayloadTargetsSession(x.Payload, id))); }
    private static bool PayloadTargetsSession(string payload, Guid sessionId) { using var json = JsonDocument.Parse(payload); return json.RootElement.TryGetProperty("aggregateId", out var value) && value.GetGuid() == sessionId; }
    private sealed record Seed(Guid SessionId, Guid Tenant, Guid TableId, long Version);
    private sealed record CountsSnapshot(int Audit, int Outbox);
}
