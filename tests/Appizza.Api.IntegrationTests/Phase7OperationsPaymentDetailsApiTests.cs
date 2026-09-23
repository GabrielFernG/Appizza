using System.Net;
using System.Text.Json;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase7OperationsPaymentDetailsApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task OperationsPaymentDetailsRequiresPaymentsView()
    {
        var session = await fixture.CreateOpenSessionAsync();
        var token = await fixture.CreateUserTokenAsync(await EstablishmentId(session.SessionId));
        var response = await fixture.GetAsync($"api/v1/operations/sessions/{session.SessionId}/payments", token);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OperationsPaymentDetailsRejectsForeignTenantSession()
    {
        var own = await fixture.CreateOpenSessionAsync();
        var foreign = await fixture.CreateOpenSessionAsync();
        var token = await fixture.CreateUserTokenAsync(await EstablishmentId(own.SessionId), "payments.view");
        var response = await fixture.GetAsync($"api/v1/operations/sessions/{foreign.SessionId}/payments", token);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(foreign.SessionId.ToString(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OperationsPaymentDetailsReturnsEmptyStateForValidSession()
    {
        var session = await fixture.CreateOpenSessionAsync();
        var token = await fixture.CreateUserTokenAsync(await EstablishmentId(session.SessionId), "payments.view");
        var response = await fixture.GetAsync($"api/v1/operations/sessions/{session.SessionId}/payments", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(session.SessionId, json.RootElement.GetProperty("sessionId").GetGuid());
        Assert.Empty(json.RootElement.GetProperty("paymentPlans").EnumerateArray());
        Assert.Empty(json.RootElement.GetProperty("paymentAttempts").EnumerateArray());
    }

    [Fact]
    public async Task OperationsPaymentDetailsReturnsAuthoritativePlanAllocationsAndAttempts()
    {
        var session = await fixture.CreateOpenSessionAsync();
        var tenant = await EstablishmentId(session.SessionId);
        var planId = Guid.NewGuid(); var logicalId = Guid.NewGuid();
        var a1 = Guid.NewGuid(); var a2 = Guid.NewGuid(); var at1 = Guid.NewGuid(); var at2 = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var db = fixture.CreateDbContext())
        {
            var plan = new PaymentPlan { Id = planId, LogicalPlanId = logicalId, EstablishmentId = tenant, TableSessionId = session.SessionId, Version = 7, Mode = PaymentPlanMode.EqualSplit, CreatedAt = now, UpdatedAt = now };
            plan.Allocations.Add(new PaymentPlanAllocation { Id = a1, EstablishmentId = tenant, PaymentPlanId = planId, StableOrder = 1, Amount = 31.17m, CreatedAt = now });
            plan.Allocations.Add(new PaymentPlanAllocation { Id = a2, EstablishmentId = tenant, PaymentPlanId = planId, StableOrder = 2, Amount = 48.83m, CreatedAt = now });
            db.Add(plan);
            db.AddRange(new PaymentAttempt { Id = at1, EstablishmentId = tenant, TableSessionId = session.SessionId, PaymentPlanId = planId, PaymentPlanVersion = 7, Method = PaymentMethod.Pix, Status = PaymentAttemptStatus.Processing, Amount = 31.17m, ReservedAmount = 30m, Provider = "p1", ProviderReference = "ref-1", Version = 3, CreatedAt = now, UpdatedAt = now }, new PaymentAttempt { Id = at2, EstablishmentId = tenant, TableSessionId = session.SessionId, PaymentPlanId = planId, PaymentPlanVersion = 7, Method = PaymentMethod.Credit, Status = PaymentAttemptStatus.Declined, Amount = 48.83m, ReservedAmount = 0m, Provider = "p2", ProviderReference = "ref-2", Version = 4, CreatedAt = now.AddSeconds(1), UpdatedAt = now.AddSeconds(1) });
            await db.SaveChangesAsync();
        }
        var response = await fixture.GetAsync($"api/v1/operations/sessions/{session.SessionId}/payments", await fixture.CreateUserTokenAsync(tenant, "payments.view"));
        response.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var root = json.RootElement;
        Assert.Equal(session.SessionId, root.GetProperty("sessionId").GetGuid()); var planJson = Assert.Single(root.GetProperty("paymentPlans").EnumerateArray());
        Assert.Equal(planId, planJson.GetProperty("id").GetGuid()); Assert.Equal(logicalId, planJson.GetProperty("logicalPlanId").GetGuid()); Assert.Equal(7, planJson.GetProperty("version").GetInt64()); Assert.Equal("EqualSplit", planJson.GetProperty("mode").GetString());
        var allocations = planJson.GetProperty("allocations").EnumerateArray().ToArray(); Assert.Equal(2, allocations.Length); Assert.Equal(a1, allocations[0].GetProperty("id").GetGuid()); Assert.Equal(31.17m, allocations[0].GetProperty("amount").GetDecimal()); Assert.Equal(a2, allocations[1].GetProperty("id").GetGuid()); Assert.Equal(48.83m, allocations[1].GetProperty("amount").GetDecimal());
        var attempts = root.GetProperty("paymentAttempts").EnumerateArray().ToArray(); Assert.Equal(2, attempts.Length); Assert.Equal(at1, attempts[0].GetProperty("id").GetGuid()); Assert.Equal("Pix", attempts[0].GetProperty("method").GetString()); Assert.Equal("Processing", attempts[0].GetProperty("status").GetString()); Assert.Equal(31.17m, attempts[0].GetProperty("amount").GetDecimal()); Assert.Equal(30m, attempts[0].GetProperty("reservedAmount").GetDecimal()); Assert.Equal("p1", attempts[0].GetProperty("provider").GetString()); Assert.Equal("ref-1", attempts[0].GetProperty("providerReference").GetString()); Assert.Equal(at2, attempts[1].GetProperty("id").GetGuid()); Assert.Equal("Declined", attempts[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task OperationsPaymentDetailsDoesNotLeakAnotherSessionPayments()
    {
        var s1 = await fixture.CreateOpenSessionAsync(); var s2 = await fixture.CreateOpenSessionAsync(); var tenant = await EstablishmentId(s1.SessionId); var planId = Guid.NewGuid(); var attemptId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        await using (var db = fixture.CreateDbContext()) { db.Add(new PaymentPlan { Id = planId, LogicalPlanId = Guid.NewGuid(), EstablishmentId = tenant, TableSessionId = s2.SessionId, Version = 1, Mode = PaymentPlanMode.Total, CreatedAt = now, UpdatedAt = now }); db.Add(new PaymentAttempt { Id = attemptId, EstablishmentId = tenant, TableSessionId = s2.SessionId, PaymentPlanId = planId, Method = PaymentMethod.Cash, Status = PaymentAttemptStatus.Created, Amount = 9m, Version = 1, CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync(); }
        var response = await fixture.GetAsync($"api/v1/operations/sessions/{s1.SessionId}/payments", await fixture.CreateUserTokenAsync(tenant, "payments.view")); response.EnsureSuccessStatusCode(); var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(planId.ToString(), body); Assert.DoesNotContain(attemptId.ToString(), body);
    }

    [Fact]
    public async Task OperationsPaymentDetailsIsStrictlyReadOnly()
    {
        var session = await fixture.CreateOpenSessionAsync(); var tenant = await EstablishmentId(session.SessionId); var planId = Guid.NewGuid(); var attemptId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        await using (var db = fixture.CreateDbContext()) { db.Add(new PaymentPlan { Id = planId, LogicalPlanId = Guid.NewGuid(), EstablishmentId = tenant, TableSessionId = session.SessionId, Version = 2, Mode = PaymentPlanMode.Total, CreatedAt = now, UpdatedAt = now }); db.Add(new PaymentAttempt { Id = attemptId, EstablishmentId = tenant, TableSessionId = session.SessionId, PaymentPlanId = planId, Method = PaymentMethod.Debit, Status = PaymentAttemptStatus.Created, Amount = 12.34m, ReservedAmount = 2m, Provider = "p", ProviderReference = "r", Version = 5, CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync(); }
        async Task<(int plans, int attempts, long version, PaymentAttemptStatus status, decimal amount, decimal reserved)> Snapshot() { await using var db = fixture.CreateDbContext(); var p = await db.PaymentPlans.CountAsync(x => x.Id == planId && x.EstablishmentId == tenant); var a = await db.PaymentAttempts.AsNoTracking().SingleAsync(x => x.Id == attemptId); return (p, 1, a.Version, a.Status, a.Amount, a.ReservedAmount); }
        var before = await Snapshot(); var response = await fixture.GetAsync($"api/v1/operations/sessions/{session.SessionId}/payments", await fixture.CreateUserTokenAsync(tenant, "payments.view")); response.EnsureSuccessStatusCode(); var after = await Snapshot(); Assert.Equal(before, after);
    }

    private async Task<Guid> EstablishmentId(Guid sessionId)
    { await using var db = fixture.CreateDbContext(); return await db.Set<TableSession>().Where(x => x.Id == sessionId).Select(x => x.EstablishmentId).SingleAsync(); }
}
