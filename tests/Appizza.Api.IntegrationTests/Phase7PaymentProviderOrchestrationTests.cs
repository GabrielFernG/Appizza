using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Appizza.Api;
using Appizza.Payments.Application;
using Appizza.Modules.Payments;
using Appizza.Persistence;
using Appizza.Modules.Tables;
using Appizza.Modules.Auditing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Appizza.Api.IntegrationTests;

/// <summary>Deterministic orchestration/claim probes for Macro 7-B batch 1.</summary>
[Collection(Phase1ApiCollection.Name)]
public sealed class Phase7PaymentProviderOrchestrationTests(Phase1ApiFixture fixture)
{
    [Fact] public async Task DurableIntentIsCommittedBeforeProviderInvocation()
    {
        var x = await CreateAttemptAsync();
        await EnsureExecutionAsync(x);
        var provider = new ControllablePaymentProvider("processing", async request =>
        {
            await using var independent = fixture.CreateDbContext();
            var execution = await independent.PaymentProviderExecutions.AsNoTracking().SingleAsync(e => e.PaymentAttemptId == request.AttemptId);
            Assert.Equal(x.Tenant, execution.EstablishmentId);
            Assert.Equal($"attempt:{request.AttemptId:N}", execution.ProviderIdempotencyKey);
            Assert.NotEqual(PaymentProviderExecutionStatus.Pending, execution.Status);
        });
        await using var db = fixture.CreateDbContext();
        await new PaymentProcessingService(db, provider, new PaymentAttemptLifecycleService(db)).StartAsync(x.Tenant, x.AttemptId);
        Assert.True(provider.TotalInvocationCount >= 1);
    }

    [Fact] public async Task ConcurrentCreateOrGetConvergesToSingleExecution() => await AssertSingleExecutionAsync();

    [Fact] public async Task ConcurrentProcessingInvokesProviderOnlyOnce() => await AssertSingleExecutionAsync();

    [Fact] public async Task ActiveClaimCannotBeTakenByAnotherClaimant()
    {
        var x = await CreateAttemptAsync();
        await EnsureExecutionAsync(x);
        await using var db = fixture.CreateDbContext();
        var execution = await db.PaymentProviderExecutions.SingleAsync(e => e.PaymentAttemptId == x.AttemptId);
        execution.ClaimedBy = Guid.NewGuid(); execution.ClaimedUntil = DateTimeOffset.UtcNow.AddMinutes(2);
        await db.SaveChangesAsync();
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"update payments.payment_provider_execution set claimed_by = {Guid.NewGuid()}, claimed_until = {DateTimeOffset.UtcNow.AddMinutes(2)} where id = {execution.Id} and (claimed_until is null or claimed_until < now())");
        Assert.Equal(0, changed);
    }

    [Fact] public async Task ExpiredClaimCanBeRecoveredByAnotherClaimant()
    {
        var x = await CreateAttemptAsync();
        await EnsureExecutionAsync(x);
        await using var db = fixture.CreateDbContext();
        var execution = await db.PaymentProviderExecutions.SingleAsync(e => e.PaymentAttemptId == x.AttemptId);
        execution.ClaimedBy = Guid.NewGuid(); execution.ClaimedUntil = DateTimeOffset.UtcNow.AddMinutes(-1); await db.SaveChangesAsync();
        var owner = Guid.NewGuid(); var changed = await db.Database.ExecuteSqlInterpolatedAsync($"update payments.payment_provider_execution set claimed_by = {owner}, claimed_until = {DateTimeOffset.UtcNow.AddMinutes(2)} where id = {execution.Id} and (claimed_until is null or claimed_until < now())");
        Assert.Equal(1, changed); Assert.Equal(owner, (await db.PaymentProviderExecutions.AsNoTracking().SingleAsync(e => e.Id == execution.Id)).ClaimedBy);
    }

    [Fact] public async Task ExpiredClaimantCannotOverwriteNewOwnerOutcome()
    {
        var x = await CreateAttemptAsync();
        await EnsureExecutionAsync(x);
        await using var db = fixture.CreateDbContext();
        var execution = await db.PaymentProviderExecutions.SingleAsync(e => e.PaymentAttemptId == x.AttemptId);
        var oldOwner = Guid.NewGuid(); var newOwner = Guid.NewGuid(); execution.ClaimedBy = newOwner; execution.ClaimedUntil = DateTimeOffset.UtcNow.AddMinutes(2); await db.SaveChangesAsync();
        var stale = await db.Database.ExecuteSqlInterpolatedAsync($"update payments.payment_provider_execution set normalized_outcome = 'declined' where id = {execution.Id} and claimed_by = {oldOwner} and claimed_until > now()");
        Assert.Equal(0, stale); Assert.Equal(newOwner, (await db.PaymentProviderExecutions.AsNoTracking().SingleAsync(e => e.Id == execution.Id)).ClaimedBy);
    }

    [Fact] public async Task DifferentAttemptsCanProcessConcurrently()
    {
        var a = await CreateAttemptAsync(); var b = await CreateAttemptAsync(); var provider = new ControllablePaymentProvider("processing") { BlockBeforeResult = true };
        var tasks = new[] { Process(a, provider), Process(b, provider) };
        await provider.WaitUntilInvocationCountAsync(2);
        Assert.Equal(2, provider.TotalInvocationCount); Assert.True(provider.MaxConcurrentInvocations >= 2);
        provider.Release(); await Task.WhenAll(tasks);
    }

    [Fact] public async Task ApprovedOutcomeIsCommittedBeforeLifecycleSettlement() => await TerminalOutcomeIsCommittedFirst("approved");
    [Fact] public async Task DeclinedOutcomeIsCommittedBeforeLifecycleRelease() => await TerminalOutcomeIsCommittedFirst("declined");

    [Fact] public async Task UnknownOutcomeHoldsReservationAndIsNotBlindlyRetried()
    {
        var x = await CreateAttemptAsync(); var provider = new ControllablePaymentProvider("unknown");
        await Process(x, provider); var first = provider.TotalInvocationCount; await using var db = fixture.CreateDbContext();
        Assert.Equal(PaymentProviderExecutionStatus.Unknown, (await db.PaymentProviderExecutions.AsNoTracking().SingleAsync(e => e.PaymentAttemptId == x.AttemptId)).Status);
        Assert.Equal(first, provider.TotalInvocationCount);
    }

    [Fact] public async Task RepeatedApprovedProcessingDoesNotRecallProviderOrDuplicateEffects() => await RepeatedTerminalOutcome("approved");
    [Fact] public async Task RepeatedDeclinedProcessingDoesNotRecallProviderOrDuplicateEffects() => await RepeatedTerminalOutcome("declined");

    [Theory]
    [InlineData("nonexistent")]
    [InlineData("cash")]
    public async Task SecurityAndCashFailuresDoNotCreateProviderExecutions(string mode)
    {
        var x = await CreateAttemptAsync(mode == "cash" ? "cash" : "pix"); await using var db = fixture.CreateDbContext();
        var before = await db.PaymentProviderExecutions.CountAsync(e => e.PaymentAttemptId == x.AttemptId);
        Assert.Equal(0, before);
    }

    [Fact] public async Task UnauthenticatedSecurityFailureCreatesNoProviderExecution()
    {
        var context = await fixture.CreateOpenSessionAsync();
        var response = await fixture.PostAsync($"api/v1/table-device/payments/attempts/{Guid.NewGuid()}/process", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact] public async Task NonexistentSecurityFailureCreatesNoProviderExecution()
    {
        var context = await fixture.CreateOpenSessionAsync();
        await using var db = fixture.CreateDbContext();
        var before = await db.PaymentProviderExecutions.CountAsync();
        var response = await fixture.PostAsync($"api/v1/table-device/payments/attempts/{Guid.NewGuid()}/process", new { }, context.Device.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await db.PaymentProviderExecutions.CountAsync());
    }

    [Fact] public async Task ProviderExceptionPreservesDurableRecoverableExecution()
    {
        var x = await CreateAttemptAsync(); var provider = new ControllablePaymentProvider("processing") { ThrowOnStart = true };
        await using var db = fixture.CreateDbContext(); await Assert.ThrowsAsync<InvalidOperationException>(() => new PaymentProcessingService(db, provider, new PaymentAttemptLifecycleService(db)).StartAsync(x.Tenant, x.AttemptId));
        await using var read = fixture.CreateDbContext(); var execution = await read.PaymentProviderExecutions.AsNoTracking().SingleAsync(e => e.PaymentAttemptId == x.AttemptId);
        Assert.Equal($"attempt:{x.AttemptId:N}", execution.ProviderIdempotencyKey); Assert.Equal(0m, (await read.Set<TableSession>().AsNoTracking().SingleAsync(s => s.Id == (read.PaymentAttempts.Single(a => a.Id == x.AttemptId)).TableSessionId)).PaidAmount);
    }

    private async Task TerminalOutcomeIsCommittedFirst(string status)
    {
        var x = await CreateAttemptAsync();
        var provider = new ControllablePaymentProvider(status);
        await using var db = fixture.CreateDbContext(); var service = new PaymentProcessingService(db, provider, new PaymentAttemptLifecycleService(db));
        var result = await service.StartAsync(x.Tenant, x.AttemptId);
        await using (var independent = fixture.CreateDbContext())
        {
            var execution = await independent.PaymentProviderExecutions.AsNoTracking().SingleAsync(e => e.PaymentAttemptId == x.AttemptId);
            Assert.Equal(status, execution.NormalizedOutcome); Assert.NotNull(execution.ProviderReference);
            var attempt = await independent.PaymentAttempts.AsNoTracking().SingleAsync(a => a.Id == x.AttemptId);
            var session = await independent.Set<TableSession>().AsNoTracking().SingleAsync(s => s.Id == attempt.TableSessionId);
            Assert.NotEqual(status == "approved" ? PaymentAttemptStatus.Approved : PaymentAttemptStatus.Declined, attempt.Status);
            Assert.Equal(0m, session.PaidAmount); Assert.Equal(attempt.Amount, session.ReservedAmount);
        }
        await service.ApplyResultAsync(x.Tenant, x.AttemptId, result);
        await using var finalDb = fixture.CreateDbContext(); var finalAttempt = await finalDb.PaymentAttempts.AsNoTracking().SingleAsync(a => a.Id == x.AttemptId);
        Assert.Equal(status == "approved" ? PaymentAttemptStatus.Approved : PaymentAttemptStatus.Declined, finalAttempt.Status);
    }

    private async Task RepeatedTerminalOutcome(string status)
    {
        var x = await CreateAttemptAsync(); var provider = new ControllablePaymentProvider(status);
        await using (var db = fixture.CreateDbContext()) { var s = new PaymentProcessingService(db, provider, new PaymentAttemptLifecycleService(db)); var result = await s.StartAsync(x.Tenant, x.AttemptId); await s.ApplyResultAsync(x.Tenant, x.AttemptId, result); }
        var count = provider.TotalInvocationCount;
        await using var repeatDb = fixture.CreateDbContext(); var repeat = new PaymentProcessingService(repeatDb, provider, new PaymentAttemptLifecycleService(repeatDb)); await repeat.StartAsync(x.Tenant, x.AttemptId);
        Assert.Equal(count, provider.TotalInvocationCount);
        var action = status == "approved" ? "payment_attempt.approved" : "payment_attempt.declined";
        var eventType = status == "approved" ? "payment-attempt-approved.v1" : "payment-attempt-declined.v1";
        Assert.Equal(1, await repeatDb.Set<AuditEntry>().CountAsync(a => a.AggregateId == x.AttemptId && a.Action == action));
        Assert.Equal(1, await repeatDb.OutboxMessages.CountAsync(e => e.EventType == eventType && e.EstablishmentId == x.Tenant));
    }

    private async Task AssertSingleExecutionAsync()
    {
        var x = await CreateAttemptAsync(); var provider = new ControllablePaymentProvider("processing");
        await Task.WhenAll(Process(x, provider), Process(x, provider));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.PaymentProviderExecutions.CountAsync(e => e.PaymentAttemptId == x.AttemptId));
        Assert.Single(provider.ObservedProviderIdempotencyKeys.Distinct());
    }

    private async Task Process((Guid Tenant, Guid AttemptId) x, IPaymentProvider provider)
    { await using var db = fixture.CreateDbContext(); await new PaymentProcessingService(db, provider, new PaymentAttemptLifecycleService(db)).StartAsync(x.Tenant, x.AttemptId); }

    private async Task<(Guid Tenant, Guid AttemptId)> CreateAttemptAsync(string method = "pix")
    {
        var context = await fixture.CreateOpenSessionAsync();
        await using (var db = fixture.CreateDbContext()) { var s = await db.Set<TableSession>().SingleAsync(s => s.Id == context.SessionId); s.TotalAmount = 10m; await db.SaveChangesAsync(); }
        var plan = await fixture.PostWithIdempotencyAsync("api/v1/table-device/session/payment-plan", new { sessionId = context.SessionId, mode = "total" }, context.Device.AccessToken, Guid.NewGuid()); plan.EnsureSuccessStatusCode();
        using var p = JsonDocument.Parse(await plan.Content.ReadAsStringAsync()); var planId = p.RootElement.GetProperty("planId").GetGuid(); var allocation = p.RootElement.GetProperty("allocations")[0].GetProperty("allocationId").GetGuid();
        var attempt = await fixture.PostWithIdempotencyAsync("api/v1/table-device/payments/attempts", new { tableSessionId = context.SessionId, paymentPlanId = planId, allocationIds = new[] { allocation }, paymentMethod = method }, context.Device.AccessToken, Guid.NewGuid()); attempt.EnsureSuccessStatusCode(); using var a = JsonDocument.Parse(await attempt.Content.ReadAsStringAsync());
        await using var lookup = fixture.CreateDbContext(); var tenant = await lookup.Set<TableSession>().Where(s => s.Id == context.SessionId).Select(s => s.EstablishmentId).SingleAsync(); return (tenant, a.RootElement.GetProperty("attemptId").GetGuid());
    }

    private async Task EnsureExecutionAsync((Guid Tenant, Guid AttemptId) x)
    {
        await using var db = fixture.CreateDbContext();
        if (!await db.PaymentProviderExecutions.AnyAsync(e => e.PaymentAttemptId == x.AttemptId))
        {
            db.PaymentProviderExecutions.Add(new PaymentProviderExecution
            {
                Id = Guid.NewGuid(), EstablishmentId = x.Tenant, PaymentAttemptId = x.AttemptId,
                Provider = "ControllablePaymentProvider", ProviderIdempotencyKey = $"attempt:{x.AttemptId:N}",
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }
}

internal sealed class ControllablePaymentProvider(string status, Func<StartPaymentRequest, Task>? onInvocation = null, string? lookupStatus = null) : IPaymentProvider
{
    public sealed class InvocationGate
    {
        private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Entered => _entered.Task;
        internal void SignalEntered() => _entered.TrySetResult(true);
        internal Task WaitReleaseAsync(CancellationToken ct) => _release.Task.WaitAsync(ct);
        public void Release() => _release.TrySetResult(true);
    }
    public Guid InstanceId { get; } = Guid.NewGuid();
    public bool StartEntered { get; private set; }
    public Guid? LastStartAttemptId { get; private set; }
    private int _total;
    private int _activeInvocations;
    private int _lookup;
    private int _failures;
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<InvocationGate>> _startGates = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<InvocationGate>> _lookupGates = new();
    private readonly ConcurrentDictionary<Guid, int> _startByAttempt = new();
    private readonly ConcurrentDictionary<Guid, int> _lookupByAttempt = new();
    private readonly ConcurrentDictionary<Guid, string> _startResults = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<string>> _orderedStartResults = new();
    private readonly ConcurrentDictionary<Guid, string> _lookupResults = new();
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _second = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool BlockBeforeResult { get; init; }
    public bool ThrowOnStart { get; init; }
    public PaymentRecoveryClassification? StartFailureClassification { get; init; }
    public bool FailOnlyFirstStart { get; init; }
    public int TotalInvocationCount => _total;
    public int StartPaymentInvocationCount => _total;
    public int LookupPaymentInvocationCount => _lookup;
    private int _maxConcurrentInvocations;
    public int MaxConcurrentInvocations => Volatile.Read(ref _maxConcurrentInvocations);
    public ConcurrentBag<string> ObservedProviderIdempotencyKeys { get; } = new();
    public ConcurrentBag<string> ObservedLookupProviderIdempotencyKeys { get; } = new();
    public ConcurrentQueue<string> CallTrace { get; } = new();
    public InvocationGate BlockStart(Guid attemptId) { var gate = new InvocationGate(); _startGates.GetOrAdd(attemptId, _ => new ConcurrentQueue<InvocationGate>()).Enqueue(gate); return gate; }
    public InvocationGate BlockLookup(Guid attemptId) { var gate = new InvocationGate(); _lookupGates.GetOrAdd(attemptId, _ => new ConcurrentQueue<InvocationGate>()).Enqueue(gate); return gate; }
    public void ConfigureStartResult(Guid attemptId, string result) => _startResults[attemptId] = result;
    public void ConfigureOrderedStartResults(Guid attemptId, params string[] results) => _orderedStartResults[attemptId] = new ConcurrentQueue<string>(results);
    public void ConfigureLookupResult(Guid attemptId, string result) => _lookupResults[attemptId] = result;
    public int StartCount(Guid attemptId) => _startByAttempt.TryGetValue(attemptId, out var n) ? n : 0;
    public int LookupCount(Guid attemptId) => _lookupByAttempt.TryGetValue(attemptId, out var n) ? n : 0;
    public Task WaitUntilInvocationCountAsync(int count, CancellationToken cancellationToken = default) => count == 2 ? _second.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken) : Task.CompletedTask;
    public void Release() => _release.TrySetResult();
    public Task<PaymentProviderCapabilities> DiscoverCapabilitiesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderCapabilities(new HashSet<string> { "pix", "credit", "debit" }));
    public async Task<PaymentProviderStatus> StartPaymentAsync(StartPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var active = Interlocked.Increment(ref _activeInvocations);
        UpdateMaximum(active);
        try
        {
            StartEntered = true; LastStartAttemptId = request.AttemptId;
            var count = Interlocked.Increment(ref _total);
            _startByAttempt.AddOrUpdate(request.AttemptId, 1, (_, n) => n + 1);
            ObservedProviderIdempotencyKeys.Add(request.ProviderReference ?? "");
            var configured = _orderedStartResults.TryGetValue(request.AttemptId, out var orderedQueue) && orderedQueue.TryDequeue(out var orderedAtEntry) ? orderedAtEntry : (_startResults.TryGetValue(request.AttemptId, out var srAtEntry) ? srAtEntry : status);
            if (_startGates.TryGetValue(request.AttemptId, out var gates) && gates.TryDequeue(out var gate)) { gate.SignalEntered(); await gate.WaitReleaseAsync(cancellationToken); }
            if (StartFailureClassification is not null && (!FailOnlyFirstStart || Interlocked.Increment(ref _failures) == 1)) { CallTrace.Enqueue($"#{count} Start attempt={request.AttemptId:N} key={request.ProviderReference} -> THROW {StartFailureClassification.Value}"); throw new PaymentProviderOperationException(StartFailureClassification.Value, "CONTROLLED_PROVIDER_FAILURE"); }
            if (ThrowOnStart) throw new InvalidOperationException("PROVIDER_TRANSPORT_FAILURE");
            if (count >= 2) _second.TrySetResult();
            if (BlockBeforeResult) await _release.Task.WaitAsync(cancellationToken);
            if (onInvocation is not null) await onInvocation(request);
            var result = new PaymentProviderStatus(configured, $"provider-ref-{request.AttemptId:N}");
            CallTrace.Enqueue($"#{count} Start attempt={request.AttemptId:N} key={request.ProviderReference} -> {configured}");
            return result;
        }
        finally
        {
            Interlocked.Decrement(ref _activeInvocations);
        }
    }
    private void UpdateMaximum(int active)
    {
        while (true)
        {
            var observed = Volatile.Read(ref _maxConcurrentInvocations);
            if (active <= observed || Interlocked.CompareExchange(ref _maxConcurrentInvocations, active, observed) == observed) return;
        }
    }
    public Task<PaymentProviderStatus> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderStatus("unknown", providerReference));
    public Task<PaymentProviderStatus> CancelPaymentAsync(string providerReference, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderStatus("declined", providerReference));
    public Task<PaymentProviderStatus> ReconcilePaymentAsync(string providerReference, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderStatus("unknown", providerReference));
    public async Task<PaymentProviderLookupResult> LookupPaymentAsync(PaymentProviderLookupRequest request, CancellationToken cancellationToken = default) { var count = Interlocked.Increment(ref _lookup); _lookupByAttempt.AddOrUpdate(request.AttemptId, 1, (_, n) => n + 1); ObservedLookupProviderIdempotencyKeys.Add(request.ProviderIdempotencyKey); if (_lookupGates.TryGetValue(request.AttemptId, out var gates) && gates.TryDequeue(out var gate)) { gate.SignalEntered(); await gate.WaitReleaseAsync(cancellationToken); } var configured = _lookupResults.TryGetValue(request.AttemptId, out var lr) ? lr : lookupStatus ?? "unknown"; CallTrace.Enqueue($"#{count} Lookup attempt={request.AttemptId:N} key={request.ProviderIdempotencyKey} -> {configured}"); return new PaymentProviderLookupResult(configured, request.ProviderReference); }
}
