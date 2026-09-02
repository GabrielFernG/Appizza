using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Appizza.Modules.Payments;
using Appizza.Modules.Tables;
using Appizza.Modules.Devices;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase7PaymentAttemptApiTests(Phase1ApiFixture fixture)
{
    private sealed record FinancialSnapshot(decimal Reserved, decimal Paid, int Attempts, int Ownership);
    private async Task<FinancialSnapshot> Snapshot(Guid sessionId)
    {
        await using var db = fixture.CreateDbContext();
        var s = await db.Set<TableSession>().SingleAsync(x => x.Id == sessionId);
        return new(s.ReservedAmount, s.PaidAmount, await db.Set<PaymentAttempt>().CountAsync(x => x.TableSessionId == sessionId), await db.Set<PaymentAttemptAllocation>().CountAsync(x => x.PaymentAttempt.TableSessionId == sessionId));
    }
    [Fact]
    public async Task ValidAttemptCreatesOwnershipAndReservation()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var planResponse = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "total" }, context.Device.AccessToken, Guid.NewGuid());
        planResponse.EnsureSuccessStatusCode();
        using var planJson = JsonDocument.Parse(await planResponse.Content.ReadAsStringAsync());
        var planId = planJson.RootElement.GetProperty("planId").GetGuid();
        var allocationId = planJson.RootElement.GetProperty("allocations")[0].GetProperty("allocationId").GetGuid();
        var key = Guid.NewGuid();
        var response = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = context.SessionId, paymentPlanId = planId, allocationIds = new[] { allocationId }, paymentMethod = "cash" }, context.Device.AccessToken, key);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Expected 201, got {(int)response.StatusCode}. Body: {responseBody}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var attemptId = body.RootElement.GetProperty("attemptId").GetGuid();
        await using var db = fixture.CreateDbContext();
        var attempt = await db.Set<PaymentAttempt>().Include(x => x.Allocations).SingleAsync(x => x.Id == attemptId);
        Assert.Equal(attempt.Amount, attempt.Allocations.Single().Amount);
        Assert.Equal(attempt.Amount, await db.Set<TableSession>().Where(x => x.Id == context.SessionId).Select(x => x.ReservedAmount).SingleAsync());
    }

    [Fact]
    public async Task SameKeySameIntentReplaysWithoutSecondReservation()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var planResponse = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "total" }, context.Device.AccessToken, Guid.NewGuid());
        using var planJson = JsonDocument.Parse(await planResponse.Content.ReadAsStringAsync());
        var planId = planJson.RootElement.GetProperty("planId").GetGuid(); var allocationId = planJson.RootElement.GetProperty("allocations")[0].GetProperty("allocationId").GetGuid(); var key = Guid.NewGuid();
        var payload = new { tableSessionId = context.SessionId, paymentPlanId = planId, allocationIds = new[] { allocationId }, paymentMethod = "pix" };
        var first = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", payload, context.Device.AccessToken, key);
        var second = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", payload, context.Device.AccessToken, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var a = JsonDocument.Parse(await first.Content.ReadAsStringAsync()); using var b = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(a.RootElement.GetProperty("attemptId").GetGuid(), b.RootElement.GetProperty("attemptId").GetGuid());
    }

    [Fact]
    public async Task InvalidPaymentMethodAndMissingKeyDoNotMutateFinancials()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var plan = await CreateTotalPlan(context);
        var invalid = await fixture.PostAsync("api/v1/table-device/payments/attempts", new { tableSessionId = context.SessionId, paymentPlanId = plan.PlanId, allocationIds = new[] { plan.AllocationId }, paymentMethod = "bitcoin" }, context.Device.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var missing = await fixture.PostAsync("api/v1/table-device/payments/attempts", new { tableSessionId = context.SessionId, paymentPlanId = plan.PlanId, allocationIds = new[] { plan.AllocationId }, paymentMethod = "cash" }, context.Device.AccessToken);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(0m, await db.Set<TableSession>().Where(x => x.Id == context.SessionId).Select(x => x.ReservedAmount).SingleAsync());
        Assert.Equal(0, await db.Set<PaymentAttempt>().CountAsync(x => x.TableSessionId == context.SessionId));
    }

    [Theory]
    [InlineData("open", true)]
    [InlineData("closing", true)]
    [InlineData("awaiting_payment", true)]
    [InlineData("partially_paid", true)]
    [InlineData("paid", false)]
    [InlineData("closed", false)]
    [InlineData("suspended", false)]
    [InlineData("cancelled", false)]
    public async Task PaymentAttemptSessionStateMatrix(string status, bool allowed)
    {
        var context = await fixture.CreateOpenSessionAsync();
        await using (var db = fixture.CreateDbContext())
        {
            var session = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId);
            session.TotalAmount = 10m;
            session.RemainingAmount = 10m;
            await db.SaveChangesAsync();
        }
        var plan = await CreateTotalPlan(context);
        await using (var db = fixture.CreateDbContext())
        {
            var session = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId);
            session.Status = status;
            if (status == "partially_paid") { session.TotalAmount = 20m; session.RemainingAmount = 19m; session.PaidAmount = 1m; }
            await db.SaveChangesAsync();
        }
        var before = await Snapshot(context.SessionId);
        var response = await Reserve(context, plan.PlanId, plan.AllocationId, Guid.NewGuid());
        if (allowed)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var attemptId = payload.RootElement.GetProperty("attemptId").GetGuid();
            await using var verify = fixture.CreateDbContext();
            var attempt = await verify.Set<PaymentAttempt>().Include(x => x.Allocations).SingleAsync(x => x.Id == attemptId);
            Assert.Single(attempt.Allocations);
            await using var db = fixture.CreateDbContext();
            var session = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId);
            Assert.Equal(status, session.Status);
            Assert.Equal(before.Paid, session.PaidAmount);
            Assert.Equal(10m, attempt.Amount);
            Assert.Equal(before.Reserved + attempt.Amount, session.ReservedAmount);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(before, await Snapshot(context.SessionId));
            await using var db = fixture.CreateDbContext();
            Assert.Equal(status, (await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId)).Status);
        }
    }

    [Fact]
    public async Task ConcurrentSameKeyCreatesSingleAttempt()
    {
        var context = await fixture.CreateOpenSessionAsync(); var plan = await CreateTotalPlan(context); var key = Guid.NewGuid();
        var payload = new { tableSessionId = context.SessionId, paymentPlanId = plan.PlanId, allocationIds = new[] { plan.AllocationId }, paymentMethod = "cash" };
        var responses = await fixture.ConcurrentAsync(
            () => fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", payload, context.Device.AccessToken, key),
            () => fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", payload, context.Device.AccessToken, key));
        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
        var ids = await Task.WhenAll(responses.Select(async r => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("attemptId").GetGuid()));
        Assert.Equal(ids[0], ids[1]);
        await using var db = fixture.CreateDbContext(); Assert.Equal(1, await db.Set<PaymentAttempt>().CountAsync(x => x.TableSessionId == context.SessionId));
    }

    [Fact]
    public async Task DuplicateSelectorsAreRejectedWithoutMutation()
    {
        var context = await fixture.CreateOpenSessionAsync(); var plan = await CreateTotalPlan(context);
        var response = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = context.SessionId, paymentPlanId = plan.PlanId, allocationIds = new[] { plan.AllocationId, plan.AllocationId }, paymentMethod = "cash" }, context.Device.AccessToken, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = fixture.CreateDbContext(); Assert.Equal(0, await db.Set<PaymentAttempt>().CountAsync(x => x.TableSessionId == context.SessionId));
    }

    [Fact]
    public async Task SameKeyDifferentMethodReturnsConflict()
    {
        var context = await fixture.CreateOpenSessionAsync(); var plan = await CreateTotalPlan(context); var key = Guid.NewGuid();
        var first = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = context.SessionId, paymentPlanId = plan.PlanId, allocationIds = new[] { plan.AllocationId }, paymentMethod = "cash" }, context.Device.AccessToken, key);
        var second = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = context.SessionId, paymentPlanId = plan.PlanId, allocationIds = new[] { plan.AllocationId }, paymentMethod = "pix" }, context.Device.AccessToken, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_REQUEST", await fixture.ErrorCodeAsync(second));
    }

    [Theory]
    [InlineData("cash")]
    [InlineData("pix")]
    [InlineData("credit")]
    [InlineData("debit")]
    public async Task MvpPaymentMethodsCreateReservation(string method)
    {
        var context = await fixture.CreateOpenSessionAsync(); var plan = await CreateTotalPlan(context);
        var response = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = context.SessionId, paymentPlanId = plan.PlanId, allocationIds = new[] { plan.AllocationId }, paymentMethod = method }, context.Device.AccessToken, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.NotEqual("approved", json.RootElement.GetProperty("status").GetString()?.ToLowerInvariant());
    }

    [Fact]
    public async Task EqualSplitAllocationCanBeReservedIndependently()
    {
        var context = await fixture.CreateOpenSessionAsync();
        await using (var db = fixture.CreateDbContext()) { var s = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId); s.TotalAmount = 100m; s.RemainingAmount = 100m; await db.SaveChangesAsync(); }
        var planResponse = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "equal_split", partCount = 3 }, context.Device.AccessToken, Guid.NewGuid());
        planResponse.EnsureSuccessStatusCode(); using var p = JsonDocument.Parse(await planResponse.Content.ReadAsStringAsync()); var planId = p.RootElement.GetProperty("planId").GetGuid(); var allocation = p.RootElement.GetProperty("allocations")[1]; var allocationId = allocation.GetProperty("allocationId").GetGuid(); var amount = allocation.GetProperty("amount").GetDecimal();
        var response = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = context.SessionId, paymentPlanId = planId, allocationIds = new[] { allocationId }, paymentMethod = "pix" }, context.Device.AccessToken, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal(amount, body.RootElement.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task SelectorOrderIsIdempotent()
    {
        var context = await fixture.CreateOpenSessionAsync(); await using (var db = fixture.CreateDbContext()) { var s = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId); s.TotalAmount = 100m; s.RemainingAmount = 100m; await db.SaveChangesAsync(); }
        var planResponse = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "equal_split", partCount = 3 }, context.Device.AccessToken, Guid.NewGuid()); using var p = JsonDocument.Parse(await planResponse.Content.ReadAsStringAsync()); var planId = p.RootElement.GetProperty("planId").GetGuid(); var ids = p.RootElement.GetProperty("allocations").EnumerateArray().Take(2).Select(x => x.GetProperty("allocationId").GetGuid()).ToArray(); var key = Guid.NewGuid();
        var first = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = context.SessionId, paymentPlanId = planId, allocationIds = ids, paymentMethod = "cash" }, context.Device.AccessToken, key);
        var second = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = context.SessionId, paymentPlanId = planId, allocationIds = ids.Reverse().ToArray(), paymentMethod = "cash" }, context.Device.AccessToken, key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.OK, second.StatusCode); using var a = JsonDocument.Parse(await first.Content.ReadAsStringAsync()); using var b = JsonDocument.Parse(await second.Content.ReadAsStringAsync()); Assert.Equal(a.RootElement.GetProperty("attemptId").GetGuid(), b.RootElement.GetProperty("attemptId").GetGuid());
    }

    [Fact]
    public async Task ByItemSelectsMultipleAuthoritativeAllocations()
    {
        var context = await fixture.CreateOpenSessionAsync(); var now = DateTimeOffset.UtcNow; var planId = Guid.NewGuid(); var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        await using (var db = fixture.CreateDbContext())
        {
            var session = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId); session.TotalAmount = 80m; session.RemainingAmount = 80m;
            var plan = new PaymentPlan { Id = planId, LogicalPlanId = Guid.NewGuid(), EstablishmentId = session.EstablishmentId, TableSessionId = session.Id, Version = 1, Mode = PaymentPlanMode.ByItem, CreatedAt = now, UpdatedAt = now };
            plan.Allocations.Add(new PaymentPlanAllocation { Id = ids[0], PaymentPlanId = planId, EstablishmentId = session.EstablishmentId, Amount = 50m, StableOrder = 0, CreatedAt = now });
            plan.Allocations.Add(new PaymentPlanAllocation { Id = ids[1], PaymentPlanId = planId, EstablishmentId = session.EstablishmentId, Amount = 10m, StableOrder = 1, CreatedAt = now });
            plan.Allocations.Add(new PaymentPlanAllocation { Id = ids[2], PaymentPlanId = planId, EstablishmentId = session.EstablishmentId, Amount = 20m, StableOrder = 2, CreatedAt = now }); db.Add(plan); await db.SaveChangesAsync();
        }
        var response = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = context.SessionId, paymentPlanId = planId, allocationIds = ids.Take(2), paymentMethod = "cash" }, context.Device.AccessToken, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal(60m, body.RootElement.GetProperty("amount").GetDecimal());
        await using var verify = fixture.CreateDbContext(); Assert.Equal(60m, await verify.Set<TableSession>().Where(x => x.Id == context.SessionId).Select(x => x.ReservedAmount).SingleAsync());
    }

    [Fact]
    public async Task BlockedDeviceAfterTokenIssuanceIsRejected()
    {
        var context = await fixture.CreateOpenSessionAsync(); var plan = await CreateTotalPlan(context); var before = await Snapshot(context.SessionId);
        await using (var db = fixture.CreateDbContext()) { var d = await db.Set<Device>().SingleAsync(x => x.Id == context.Device.DeviceId); d.Status = "blocked"; await db.SaveChangesAsync(); }
        var response = await Reserve(context, plan.PlanId, plan.AllocationId, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Equal(before, await Snapshot(context.SessionId));
    }

    [Fact]
    public async Task StaleCredentialVersionAfterTokenIssuanceIsRejected()
    {
        var context = await fixture.CreateOpenSessionAsync(); var plan = await CreateTotalPlan(context); var before = await Snapshot(context.SessionId);
        await using (var db = fixture.CreateDbContext()) { var d = await db.Set<Device>().SingleAsync(x => x.Id == context.Device.DeviceId); d.CredentialVersion++; await db.SaveChangesAsync(); }
        var response = await Reserve(context, plan.PlanId, plan.AllocationId, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.Equal(before, await Snapshot(context.SessionId));
    }

    [Fact]
    public async Task MissingActiveBindingAfterTokenIssuanceIsRejected()
    {
        var context = await fixture.CreateOpenSessionAsync(); var plan = await CreateTotalPlan(context); var before = await Snapshot(context.SessionId);
        await using (var db = fixture.CreateDbContext()) { var binding = await db.Set<DeviceTableBinding>().SingleAsync(x => x.DeviceId == context.Device.DeviceId && x.UnboundAt == null); binding.UnboundAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(); }
        var response = await Reserve(context, plan.PlanId, plan.AllocationId, Guid.NewGuid()); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.Equal(before, await Snapshot(context.SessionId));
    }

    [Fact]
    public async Task WrongSessionSameTenantIsRejectedWithoutMutation()
    {
        var a = await fixture.CreateOpenSessionAsync(); var sessionB = await CreateSameTenantSession(a.SessionId); var planB = await CreatePlanForSession(sessionB, a.SessionId);
        var before = await Snapshot(a.SessionId); var response = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = a.SessionId, paymentPlanId = planB.PlanId, allocationIds = new[] { planB.AllocationId }, paymentMethod = "cash" }, a.Device.AccessToken, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.DoesNotContain(planB.PlanId.ToString(), await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase); Assert.Equal(before, await Snapshot(a.SessionId));
    }

    [Fact]
    public async Task ForeignTenantPlanIsRejected()
    {
        var a = await fixture.CreateOpenSessionAsync(); var b = await fixture.CreateOpenSessionAsync(); var plan = await CreateTotalPlan(b); var beforeA = await Snapshot(a.SessionId); var beforeB = await Snapshot(b.SessionId);
        var response = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = a.SessionId, paymentPlanId = plan.PlanId, allocationIds = new[] { plan.AllocationId }, paymentMethod = "cash" }, a.Device.AccessToken, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.DoesNotContain(plan.PlanId.ToString(), await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase); Assert.Equal(beforeA, await Snapshot(a.SessionId)); Assert.Equal(beforeB, await Snapshot(b.SessionId));
    }

    [Fact]
    public async Task ForeignAllocationIsRejected()
    {
        var a = await fixture.CreateOpenSessionAsync(); var b = await fixture.CreateOpenSessionAsync(); var pa = await CreateTotalPlan(a); var pb = await CreateTotalPlan(b); var before = await Snapshot(a.SessionId);
        var response = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = a.SessionId, paymentPlanId = pa.PlanId, allocationIds = new[] { pb.AllocationId }, paymentMethod = "cash" }, a.Device.AccessToken, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.DoesNotContain(pb.AllocationId.ToString(), await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase); Assert.Equal(before, await Snapshot(a.SessionId));
    }

    [Fact]
    public async Task SameIdempotencyKeyIsTenantScoped()
    {
        var a = await fixture.CreateOpenSessionAsync(); var b = await fixture.CreateOpenSessionAsync(); var pa = await CreateTotalPlan(a); var pb = await CreateTotalPlan(b); var key = Guid.NewGuid();
        var ra = await Reserve(a, pa.PlanId, pa.AllocationId, key); var rb = await Reserve(b, pb.PlanId, pb.AllocationId, key); Assert.Equal(HttpStatusCode.Created, ra.StatusCode); Assert.Equal(HttpStatusCode.Created, rb.StatusCode);
        using var ja = JsonDocument.Parse(await ra.Content.ReadAsStringAsync()); using var jb = JsonDocument.Parse(await rb.Content.ReadAsStringAsync()); Assert.NotEqual(ja.RootElement.GetProperty("attemptId").GetGuid(), jb.RootElement.GetProperty("attemptId").GetGuid());
    }

    private async Task<Guid> CreateSameTenantSession(Guid sourceId)
    {
        await using var db = fixture.CreateDbContext(); var source = await db.Set<TableSession>().SingleAsync(x => x.Id == sourceId); var now = DateTimeOffset.UtcNow; var table = new DiningTable { Id = Guid.NewGuid(), EstablishmentId = source.EstablishmentId, Name = Guid.NewGuid().ToString("N"), Status = "available", DisplayOrder = 99, CreatedAt = now, UpdatedAt = now, Version = 1 }; var session = new TableSession { Id = Guid.NewGuid(), EstablishmentId = source.EstablishmentId, DiningTableId = table.Id, SessionNumber = Guid.NewGuid().ToString("N"), Status = "open", CustomerIdentificationStatus = "pending", OpenedAt = now, CreatedAt = now, UpdatedAt = now, Version = 1 }; db.AddRange(table, session); await db.SaveChangesAsync(); return session.Id;
    }

    private async Task<(Guid PlanId, Guid AllocationId)> CreatePlanForSession(Guid sessionId, Guid sourceSessionId)
    {
        await using var db = fixture.CreateDbContext(); var s = await db.Set<TableSession>().SingleAsync(x => x.Id == sessionId); var now = DateTimeOffset.UtcNow; var plan = new PaymentPlan { Id = Guid.NewGuid(), LogicalPlanId = Guid.NewGuid(), EstablishmentId = s.EstablishmentId, TableSessionId = s.Id, Version = 1, Mode = PaymentPlanMode.Total, CreatedAt = now, UpdatedAt = now }; var allocation = new PaymentPlanAllocation { Id = Guid.NewGuid(), PaymentPlanId = plan.Id, EstablishmentId = s.EstablishmentId, Amount = 10m, StableOrder = 0, CreatedAt = now }; plan.Allocations.Add(allocation); s.TotalAmount = 10m; s.RemainingAmount = 10m; db.Add(plan); await db.SaveChangesAsync(); return (plan.Id, allocation.Id);
    }

    [Fact]
    public async Task DifferentAllocationsCanBeReservedIndependently()
    {
        var context = await fixture.CreateOpenSessionAsync(); var plan = await CreateAllocationPlan(context, 20m, 20m);
        var first = await Reserve(context, plan.PlanId, plan.Allocations[0], Guid.NewGuid()); var second = await Reserve(context, plan.PlanId, plan.Allocations[1], Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        await using var db = fixture.CreateDbContext(); Assert.Equal(40m, await db.Set<TableSession>().Where(x => x.Id == context.SessionId).Select(x => x.ReservedAmount).SingleAsync());
    }

    [Fact]
    public async Task ConcurrentSameAllocationOnlyOneReservationSucceeds()
    {
        var context = await fixture.CreateOpenSessionAsync(); var plan = await CreateAllocationPlan(context, 20m, 20m);
        var responses = await fixture.ConcurrentAsync(() => Reserve(context, plan.PlanId, plan.Allocations[0], Guid.NewGuid()), () => Reserve(context, plan.PlanId, plan.Allocations[0], Guid.NewGuid()));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Created));
        await using var db = fixture.CreateDbContext(); Assert.Equal(20m, await db.Set<TableSession>().Where(x => x.Id == context.SessionId).Select(x => x.ReservedAmount).SingleAsync());
    }

    [Fact]
    public async Task ConcurrentOverReservationAllowsOnlyOneWinner()
    {
        var context = await fixture.CreateOpenSessionAsync(); var plan = await CreateAllocationPlan(context, 40m, 40m);
        await using (var db = fixture.CreateDbContext()) { var s = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId); s.TotalAmount = 50m; s.RemainingAmount = 50m; await db.SaveChangesAsync(); }
        var responses = await fixture.ConcurrentAsync(() => Reserve(context, plan.PlanId, plan.Allocations[0], Guid.NewGuid()), () => Reserve(context, plan.PlanId, plan.Allocations[1], Guid.NewGuid()));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Created));
        await using var verify = fixture.CreateDbContext(); Assert.Equal(40m, await verify.Set<TableSession>().Where(x => x.Id == context.SessionId).Select(x => x.ReservedAmount).SingleAsync());
    }

    public static IEnumerable<object[]> HoldingStatuses() => new[] { PaymentAttemptStatus.Created, PaymentAttemptStatus.AwaitingCustomerAction, PaymentAttemptStatus.Processing, PaymentAttemptStatus.Unknown, PaymentAttemptStatus.Approved }.Select(x => new object[] { x });
    public static IEnumerable<object[]> ReleasedStatuses() => new[] { PaymentAttemptStatus.Declined, PaymentAttemptStatus.Cancelled, PaymentAttemptStatus.Expired }.Select(x => new object[] { x });

    [Theory]
    [MemberData(nameof(HoldingStatuses))]
    public async Task HistoricalHoldingOwnershipBlocksReservation(PaymentAttemptStatus status)
    {
        var context = await fixture.CreateOpenSessionAsync(); var plan = await CreateAllocationPlan(context, 20m, 20m); await AddHistoricalOwnership(context.SessionId, plan.PlanId, plan.Allocations[0], status, 20m);
        var response = await Reserve(context, plan.PlanId, plan.Allocations[0], Guid.NewGuid()); Assert.NotEqual(HttpStatusCode.Created, response.StatusCode);
        await using var db = fixture.CreateDbContext(); Assert.Equal(1, await db.Set<PaymentAttempt>().CountAsync(x => x.TableSessionId == context.SessionId)); Assert.Equal(20m, await db.Set<TableSession>().Where(x => x.Id == context.SessionId).Select(x => x.ReservedAmount).SingleAsync());
    }

    [Theory]
    [MemberData(nameof(ReleasedStatuses))]
    public async Task HistoricalReleasedOwnershipIsReusable(PaymentAttemptStatus status)
    {
        var context = await fixture.CreateOpenSessionAsync(); var plan = await CreateAllocationPlan(context, 20m, 20m); await AddHistoricalOwnership(context.SessionId, plan.PlanId, plan.Allocations[0], status, 0m);
        var response = await Reserve(context, plan.PlanId, plan.Allocations[0], Guid.NewGuid()); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = fixture.CreateDbContext(); Assert.Equal(2, await db.Set<PaymentAttempt>().CountAsync(x => x.TableSessionId == context.SessionId)); Assert.Equal(20m, await db.Set<TableSession>().Where(x => x.Id == context.SessionId).Select(x => x.ReservedAmount).SingleAsync());
    }

    private async Task AddHistoricalOwnership(Guid sessionId, Guid planId, Guid allocationId, PaymentAttemptStatus status, decimal reserved)
    {
        await using var db = fixture.CreateDbContext(); var session = await db.Set<TableSession>().SingleAsync(x => x.Id == sessionId); session.ReservedAmount = reserved; var now = DateTimeOffset.UtcNow; var attempt = new PaymentAttempt { Id = Guid.NewGuid(), EstablishmentId = session.EstablishmentId, TableSessionId = sessionId, PaymentPlanId = planId, PaymentPlanVersion = 1, Method = PaymentMethod.Cash, Status = status, Amount = 20m, ReservedAmount = reserved, CreatedAt = now, UpdatedAt = now, Version = 1, IdempotencyKey = Guid.NewGuid().ToString() }; attempt.Allocations.Add(new PaymentAttemptAllocation { Id = Guid.NewGuid(), PaymentAttemptId = attempt.Id, PaymentPlanAllocationId = allocationId, Amount = 20m }); db.Add(attempt); await db.SaveChangesAsync();
    }

    private async Task<(Guid PlanId, Guid[] Allocations)> CreateAllocationPlan(Phase1ApiFixture.OpenSessionContext context, decimal a, decimal b)
    {
        var now = DateTimeOffset.UtcNow; var planId = Guid.NewGuid(); var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await using var db = fixture.CreateDbContext(); var s = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId); s.TotalAmount = a + b; s.RemainingAmount = a + b;
        var plan = new PaymentPlan { Id = planId, LogicalPlanId = Guid.NewGuid(), EstablishmentId = s.EstablishmentId, TableSessionId = s.Id, Version = 1, Mode = PaymentPlanMode.ByItem, CreatedAt = now, UpdatedAt = now };
        plan.Allocations.Add(new PaymentPlanAllocation { Id = ids[0], PaymentPlanId = planId, EstablishmentId = s.EstablishmentId, Amount = a, StableOrder = 0, CreatedAt = now }); plan.Allocations.Add(new PaymentPlanAllocation { Id = ids[1], PaymentPlanId = planId, EstablishmentId = s.EstablishmentId, Amount = b, StableOrder = 1, CreatedAt = now }); db.Add(plan); await db.SaveChangesAsync(); return (planId, ids);
    }

    private Task<HttpResponseMessage> Reserve(Phase1ApiFixture.OpenSessionContext context, Guid planId, Guid allocationId, Guid key) => fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = context.SessionId, paymentPlanId = planId, allocationIds = new[] { allocationId }, paymentMethod = "cash" }, context.Device.AccessToken, key);

    private async Task<(Guid PlanId, Guid AllocationId)> CreateTotalPlan(Phase1ApiFixture.OpenSessionContext context)
    {
        var response = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "total" }, context.Device.AccessToken, Guid.NewGuid());
        response.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (json.RootElement.GetProperty("planId").GetGuid(), json.RootElement.GetProperty("allocations")[0].GetProperty("allocationId").GetGuid());
    }
}
