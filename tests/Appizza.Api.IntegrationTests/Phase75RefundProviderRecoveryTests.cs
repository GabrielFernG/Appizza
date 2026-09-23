using Appizza.Modules.Payments;

namespace Appizza.Api.IntegrationTests;

public sealed class Phase75RefundProviderRecoveryTests
{
    private static readonly string[] StableKeys = ["refund:stable", "refund:stable"];
    [Fact]
    public async Task UnknownRecoveryNeverInvokesRefundAgain()
    {
        var provider = new DeterministicRefundProvider("unknown", "completed");
        var refund = new RefundProviderRequest(Guid.NewGuid(), "refund:key", PaymentMethod.Pix, 10m);
        await provider.RefundAsync(refund);
        await provider.LookupRefundAsync(new RefundProviderLookupRequest(refund.RefundId, refund.ProviderIdempotencyKey, null, refund.Method, refund.Amount));
        Assert.Equal(1, provider.RefundAsyncCallCount);
        Assert.Equal(1, provider.LookupRefundAsyncCallCount);
    }

    [Fact]
    public async Task LookupUnknownRemainsUnresolved()
    {
        var provider = new DeterministicRefundProvider("unknown", "unknown");
        var id = Guid.NewGuid();
        var result = await provider.LookupRefundAsync(new RefundProviderLookupRequest(id, "refund:key", null, PaymentMethod.Credit, 10m));
        Assert.Equal("unknown", result.Status);
        Assert.Equal(1, provider.LookupRefundAsyncCallCount);
        Assert.Equal(0, provider.RefundAsyncCallCount);
    }

    [Fact]
    public async Task StableProviderKeyIsReusedAcrossRecoveryLookup()
    {
        var provider = new DeterministicRefundProvider("unknown", "failed");
        var id = Guid.NewGuid();
        await provider.RefundAsync(new RefundProviderRequest(id, "refund:stable", PaymentMethod.Debit, 5m));
        await provider.LookupRefundAsync(new RefundProviderLookupRequest(id, "refund:stable", null, PaymentMethod.Debit, 5m));
        Assert.Equal(StableKeys, provider.ObservedKeys.ToArray());
    }
}
