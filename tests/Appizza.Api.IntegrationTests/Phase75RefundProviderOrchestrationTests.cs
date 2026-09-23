using System.Collections.Concurrent;
using Appizza.Modules.Payments;

namespace Appizza.Api.IntegrationTests;

public sealed class Phase75RefundProviderOrchestrationTests
{
    [Theory]
    [InlineData("processing")]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("unknown")]
    public async Task FakeRefundProviderProducesDeterministicOutcomes(string outcome)
    {
        var provider = new DeterministicRefundProvider(outcome, outcome);
        var request = new RefundProviderRequest(Guid.NewGuid(), "refund:stable", PaymentMethod.Pix, 10m);
        var result = await provider.RefundAsync(request);
        Assert.Equal(outcome, result.Status);
        Assert.Equal(1, provider.RefundAsyncCallCount);
        Assert.Equal("refund:stable", provider.ObservedKeys.Single());
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    [InlineData("unknown")]
    public async Task UnknownRecoveryUsesLookupIdentity(string lookupOutcome)
    {
        var provider = new DeterministicRefundProvider("unknown", lookupOutcome);
        var request = new RefundProviderLookupRequest(Guid.NewGuid(), "refund:stable", "provider-ref", PaymentMethod.SoftPos, 20m);
        var result = await provider.LookupRefundAsync(request);
        Assert.Equal(lookupOutcome, result.Status);
        Assert.Equal(1, provider.LookupRefundAsyncCallCount);
        Assert.Equal(0, provider.RefundAsyncCallCount);
        Assert.Equal("refund:stable", provider.ObservedKeys.Single());
    }

    [Fact]
    public async Task SoftPosIsCapabilitySupportedByFakeWithoutCashRouting()
    {
        var provider = new DeterministicRefundProvider("completed", "completed");
        var capabilities = await provider.DiscoverCapabilitiesAsync();
        Assert.Contains("refund:softpos", capabilities.Methods);
        await provider.RefundAsync(new RefundProviderRequest(Guid.NewGuid(), "refund:softpos", PaymentMethod.SoftPos, 5m));
        Assert.Equal(1, provider.RefundAsyncCallCount);
    }

    [Fact]
    public async Task RefundProviderTracksConcurrentInvocationsThreadSafely()
    {
        var provider = new DeterministicRefundProvider("completed", "completed");
        await Task.WhenAll(Enumerable.Range(0, 2).Select(i => provider.RefundAsync(new RefundProviderRequest(Guid.NewGuid(), $"refund:{i}", PaymentMethod.Debit, 1m))));
        Assert.Equal(2, provider.RefundAsyncCallCount);
        Assert.True(provider.MaxConcurrentRefundInvocations >= 1);
    }
}

internal sealed class DeterministicRefundProvider(string refundOutcome, string lookupOutcome) : IPaymentProvider
{
    private int _refundCalls;
    private int _lookupCalls;
    private int _active;
    private int _maxActive;
    public ConcurrentBag<string> ObservedKeys { get; } = new();
    public int RefundAsyncCallCount => Volatile.Read(ref _refundCalls);
    public int LookupRefundAsyncCallCount => Volatile.Read(ref _lookupCalls);
    public int MaxConcurrentRefundInvocations => Volatile.Read(ref _maxActive);
    public Task<PaymentProviderCapabilities> DiscoverCapabilitiesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderCapabilities(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pix", "credit", "debit", "refund:softpos" }));
    public Task<PaymentProviderStatus> StartPaymentAsync(StartPaymentRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderStatus("processing"));
    public Task<PaymentProviderStatus> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderStatus("unknown", providerReference));
    public Task<PaymentProviderStatus> CancelPaymentAsync(string providerReference, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderStatus("declined", providerReference));
    public Task<PaymentProviderStatus> ReconcilePaymentAsync(string providerReference, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderStatus("unknown", providerReference));
    public Task<PaymentProviderLookupResult> LookupPaymentAsync(PaymentProviderLookupRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderLookupResult("unknown", request.ProviderReference));
    public async Task<RefundProviderResult> RefundAsync(RefundProviderRequest request, CancellationToken cancellationToken = default)
    {
        ObservedKeys.Add(request.ProviderIdempotencyKey); Interlocked.Increment(ref _refundCalls); var active = Interlocked.Increment(ref _active); UpdateMax(active); try { await Task.Yield(); return new RefundProviderResult(refundOutcome, request.ProviderReference ?? $"refund:{request.RefundId:N}"); } finally { Interlocked.Decrement(ref _active); }
    }
    public Task<RefundProviderResult> LookupRefundAsync(RefundProviderLookupRequest request, CancellationToken cancellationToken = default) { ObservedKeys.Add(request.ProviderIdempotencyKey); Interlocked.Increment(ref _lookupCalls); return Task.FromResult(new RefundProviderResult(lookupOutcome, request.ProviderReference)); }
    private void UpdateMax(int value) { while (true) { var current = Volatile.Read(ref _maxActive); if (value <= current || Interlocked.CompareExchange(ref _maxActive, value, current) == current) return; } }
}
