using System.Net;
using System.Text.Json;
using Appizza.Modules.Payments;
using Appizza.Modules.Devices;
using Appizza.Modules.Tables;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;
using Appizza.Api;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase7PaymentProcessingApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task ConcurrentProcessingCreatesOneExecutionAndStableKey()
    {
        var (context, attemptId, tenant) = await CreateAttemptAsync("pix");
        var first = fixture.PostAsync($"api/v1/table-device/payments/attempts/{attemptId}/process", new { }, context.Device.AccessToken);
        var second = fixture.PostAsync($"api/v1/table-device/payments/attempts/{attemptId}/process", new { }, context.Device.AccessToken);
        var responses = await Task.WhenAll(first, second);
        Assert.All(responses, response => Assert.True(response.IsSuccessStatusCode));
        await using var db = fixture.CreateDbContext();
        var executions = await db.PaymentProviderExecutions.AsNoTracking().Where(x => x.EstablishmentId == tenant && x.PaymentAttemptId == attemptId).ToListAsync();
        var execution = Assert.Single(executions);
        Assert.Equal($"attempt:{attemptId:N}", execution.ProviderIdempotencyKey);
    }

    [Fact]
    public async Task ProcessingPersistsDurableExecutionIntentBeforeProviderResult()
    {
        var (context, attemptId, tenant) = await CreateAttemptAsync("pix");
        var response = await fixture.PostAsync($"api/v1/table-device/payments/attempts/{attemptId}/process", new { }, context.Device.AccessToken);
        response.EnsureSuccessStatusCode();
        await using var db = fixture.CreateDbContext();
        var execution = await db.PaymentProviderExecutions.AsNoTracking().SingleAsync(x => x.PaymentAttemptId == attemptId && x.EstablishmentId == tenant);
        Assert.Equal($"attempt:{attemptId:N}", execution.ProviderIdempotencyKey);
        Assert.Equal(PaymentProviderExecutionStatus.Processing, execution.Status);
        Assert.False(execution.LifecycleApplied);
    }

    [Fact]
    public async Task RepeatedProcessingReusesSameDurableExecution()
    {
        var (context, attemptId, tenant) = await CreateAttemptAsync("pix");
        (await fixture.PostAsync($"api/v1/table-device/payments/attempts/{attemptId}/process", new { }, context.Device.AccessToken)).EnsureSuccessStatusCode();
        (await fixture.PostAsync($"api/v1/table-device/payments/attempts/{attemptId}/process", new { }, context.Device.AccessToken)).EnsureSuccessStatusCode();
        await using var db = fixture.CreateDbContext();
        var executions = await db.PaymentProviderExecutions.AsNoTracking().Where(x => x.PaymentAttemptId == attemptId && x.EstablishmentId == tenant).ToListAsync();
        var execution = Assert.Single(executions);
        Assert.Equal($"attempt:{attemptId:N}", execution.ProviderIdempotencyKey);
    }

    [Fact]
    public async Task PixProcessingUsesProviderAndMovesAttemptToProcessing()
    {
        var (context, attemptId, tenant) = await CreateAttemptAsync("pix");
        var response = await fixture.PostAsync($"api/v1/table-device/payments/attempts/{attemptId}/process", new { }, context.Device.AccessToken);
        response.EnsureSuccessStatusCode();
        await using var db = fixture.CreateDbContext();
        var attempt = await db.Set<PaymentAttempt>().AsNoTracking().SingleAsync(x => x.Id == attemptId);
        Assert.Equal(PaymentAttemptStatus.Processing, attempt.Status);
        Assert.Equal("FakePaymentProvider", attempt.Provider);
        Assert.Equal(tenant, attempt.EstablishmentId);
    }

    [Fact]
    public async Task CashProcessingIsRejectedWithoutFinancialMutation()
    {
        var (context, attemptId, _) = await CreateAttemptAsync("cash");
        await using var beforeDb = fixture.CreateDbContext();
        var before = await beforeDb.Set<TableSession>().AsNoTracking().SingleAsync(x => x.Id == context.SessionId);
        var response = await fixture.PostAsync($"api/v1/table-device/payments/attempts/{attemptId}/process", new { }, context.Device.AccessToken);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var afterDb = fixture.CreateDbContext();
        var after = await afterDb.Set<TableSession>().AsNoTracking().SingleAsync(x => x.Id == context.SessionId);
        Assert.Equal(before.PaidAmount, after.PaidAmount);
        Assert.Equal(before.ReservedAmount, after.ReservedAmount);
    }

    [Fact]
    public async Task UnauthenticatedProcessingIsRejected()
    {
        var (context, attemptId, _) = await CreateAttemptAsync("pix");
        var response = await fixture.PostAsync($"api/v1/table-device/payments/attempts/{attemptId}/process", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("credit")]
    [InlineData("debit")]
    public async Task CardProcessingMethodsUseProvider(string method)
    {
        var (context, attemptId, _) = await CreateAttemptAsync(method);
        var response = await fixture.PostAsync($"api/v1/table-device/payments/attempts/{attemptId}/process", new { }, context.Device.AccessToken);
        response.EnsureSuccessStatusCode();
        await using var db = fixture.CreateDbContext();
        Assert.Equal(PaymentAttemptStatus.Processing, await db.Set<PaymentAttempt>().Where(x => x.Id == attemptId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task NonexistentAttemptIsNotDisclosed()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var response = await fixture.PostAsync($"api/v1/table-device/payments/attempts/{Guid.NewGuid()}/process", new { }, context.Device.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BlockedDeviceCannotProcessAttempt()
    {
        var (context, attemptId, _) = await CreateAttemptAsync("pix");
        await using (var db = fixture.CreateDbContext())
        {
            var device = await db.Set<Device>().SingleAsync(x => x.Id == context.Device.DeviceId);
            device.Status = "blocked";
            await db.SaveChangesAsync();
        }
        var response = await fixture.PostAsync($"api/v1/table-device/payments/attempts/{attemptId}/process", new { }, context.Device.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ForeignTenantAttemptIsNotDisclosed()
    {
        var foreign = await CreateAttemptAsync("pix");
        var local = await fixture.CreateOpenSessionAsync();
        var response = await fixture.PostAsync($"api/v1/table-device/payments/attempts/{foreign.AttemptId}/process", new { }, local.Device.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("pending_customer_action", PaymentAttemptStatus.AwaitingCustomerAction)]
    [InlineData("processing", PaymentAttemptStatus.Processing)]
    [InlineData("unknown", PaymentAttemptStatus.Unknown)]
    public async Task NormalizedNonTerminalResultPreservesReservation(string providerStatus, PaymentAttemptStatus expected)
    {
        var (_, attemptId, tenant) = await CreateAttemptAsync("pix");
        await using var db = fixture.CreateDbContext();
        var fake = new FakePaymentProvider(_ => new PaymentProviderStatus(providerStatus, "deterministic"));
        var processing = new PaymentProcessingService(db, fake, new PaymentAttemptLifecycleService(db));
        await processing.ApplyResultAsync(tenant, attemptId, new PaymentProviderStatus(providerStatus, "deterministic"));
        var attempt = await db.Set<PaymentAttempt>().AsNoTracking().SingleAsync(x => x.Id == attemptId);
        var session = await db.Set<TableSession>().AsNoTracking().SingleAsync(x => x.Id == attempt.TableSessionId);
        Assert.Equal(expected, attempt.Status);
        Assert.Equal(0m, session.PaidAmount);
        Assert.Equal(attempt.Amount, session.ReservedAmount);
    }

    [Fact]
    public async Task PaymentAttemptStatusesRoundTripThroughPostgres()
    {
        var (_, attemptId, _) = await CreateAttemptAsync("pix");
        foreach (var expected in Enum.GetValues<PaymentAttemptStatus>())
        {
            await using (var write = fixture.CreateDbContext())
            {
                var attempt = await write.Set<PaymentAttempt>().SingleAsync(x => x.Id == attemptId);
                attempt.Status = expected;
                await write.SaveChangesAsync();
            }
            await using var read = fixture.CreateDbContext();
            var actual = await read.Set<PaymentAttempt>().AsNoTracking().Where(x => x.Id == attemptId).Select(x => x.Status).SingleAsync();
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task FakeProviderCapabilitiesExcludeCash()
    {
        var provider = new FakePaymentProvider();
        var capabilities = await provider.DiscoverCapabilitiesAsync();
        Assert.DoesNotContain("cash", capabilities.Methods, StringComparer.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.StartPaymentAsync(new StartPaymentRequest(Guid.NewGuid(), 1m, PaymentMethod.Cash)));
    }

    [Theory]
    [InlineData("pending_customer_action")]
    [InlineData("processing")]
    [InlineData("approved")]
    [InlineData("declined")]
    [InlineData("unknown")]
    public async Task FakeProviderCanProduceDeterministicNormalizedResults(string status)
    {
        var provider = new FakePaymentProvider(_ => new PaymentProviderStatus(status, "fake-reference"));
        var result = await provider.StartPaymentAsync(new StartPaymentRequest(Guid.NewGuid(), 1m, PaymentMethod.Pix));
        Assert.Equal(status, result.Status);
        Assert.Equal("fake-reference", result.ProviderReference);
    }

    private async Task<(Phase1ApiFixture.OpenSessionContext Context, Guid AttemptId, Guid Tenant)> CreateAttemptAsync(string method)
    {
        var context = await fixture.CreateOpenSessionAsync();
        await using (var db = fixture.CreateDbContext())
        {
            var session = await db.Set<TableSession>().SingleAsync(x => x.Id == context.SessionId);
            session.TotalAmount = 10m;
            await db.SaveChangesAsync();
        }
        var planResponse = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "total" }, context.Device.AccessToken, Guid.NewGuid());
        planResponse.EnsureSuccessStatusCode();
        using var plan = JsonDocument.Parse(await planResponse.Content.ReadAsStringAsync());
        var planId = plan.RootElement.GetProperty("planId").GetGuid();
        var allocationId = plan.RootElement.GetProperty("allocations")[0].GetProperty("allocationId").GetGuid();
        var attemptResponse = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = context.SessionId, paymentPlanId = planId, allocationIds = new[] { allocationId }, paymentMethod = method }, context.Device.AccessToken, Guid.NewGuid());
        attemptResponse.EnsureSuccessStatusCode();
        using var attempt = JsonDocument.Parse(await attemptResponse.Content.ReadAsStringAsync());
        await using var lookup = fixture.CreateDbContext();
        var tenant = await lookup.Set<TableSession>().Where(x => x.Id == context.SessionId).Select(x => x.EstablishmentId).SingleAsync();
        return (context, attempt.RootElement.GetProperty("attemptId").GetGuid(), tenant);
    }
}
