using Appizza.Modules.Payments;

namespace Appizza.Api.IntegrationTests;

public sealed class Phase7PaymentProviderLookupTests
{
    [Theory]
    [InlineData("approved")]
    [InlineData("declined")]
    [InlineData("processing")]
    [InlineData("pending_customer_action")]
    [InlineData("unknown")]
    public async Task LookupReturnsNormalizedResultWithoutStartingPayment(string status)
    {
        var provider = new LookupProbeProvider(status);
        var request = new PaymentProviderLookupRequest(Guid.NewGuid(), "attempt:key", "provider-ref", PaymentMethod.Pix, 10m);
        var result = await provider.LookupPaymentAsync(request);
        Assert.Equal(status, result.Status);
        Assert.Equal(0, provider.StartCount);
        Assert.Equal(1, provider.LookupCount);
    }

    [Fact]
    public async Task LookupUsesExistingStableProviderIdentity()
    {
        var provider = new LookupProbeProvider("unknown");
        var request = new PaymentProviderLookupRequest(Guid.NewGuid(), "attempt:stable", "provider-ref", PaymentMethod.Pix, 10m);
        await provider.LookupPaymentAsync(request);
        Assert.Equal(request.ProviderIdempotencyKey, provider.LastRequest!.ProviderIdempotencyKey);
        Assert.Equal(request.ProviderReference, provider.LastRequest.ProviderReference);
    }

    [Fact]
    public async Task LookupDoesNotCreateNewExternalIntent()
    {
        var provider = new LookupProbeProvider("unknown");
        await provider.LookupPaymentAsync(new PaymentProviderLookupRequest(Guid.NewGuid(), "attempt:existing", null, PaymentMethod.Pix, 10m));
        Assert.Equal(0, provider.StartCount);
    }
}

internal sealed class LookupProbeProvider(string lookupStatus) : IPaymentProvider
{
    public int StartCount { get; private set; }
    public int LookupCount { get; private set; }
    public PaymentProviderLookupRequest? LastRequest { get; private set; }
    public Task<PaymentProviderCapabilities> DiscoverCapabilitiesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderCapabilities(new HashSet<string> { "pix", "credit", "debit" }));
    public Task<PaymentProviderStatus> StartPaymentAsync(StartPaymentRequest request, CancellationToken cancellationToken = default) { StartCount++; return Task.FromResult(new PaymentProviderStatus("processing")); }
    public Task<PaymentProviderStatus> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderStatus("unknown", providerReference));
    public Task<PaymentProviderStatus> CancelPaymentAsync(string providerReference, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderStatus("declined", providerReference));
    public Task<PaymentProviderStatus> ReconcilePaymentAsync(string providerReference, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderStatus("unknown", providerReference));
    public Task<PaymentProviderLookupResult> LookupPaymentAsync(PaymentProviderLookupRequest request, CancellationToken cancellationToken = default) { LookupCount++; LastRequest = request; return Task.FromResult(new PaymentProviderLookupResult(lookupStatus, request.ProviderReference)); }
}
