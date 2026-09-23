using Appizza.Modules.Payments;
using Npgsql;

namespace Appizza.Api;

/// <summary>Process-shared provider control used only by the disposable E2E environment.</summary>
public sealed class E2ERefundProvider(string connectionString) : IPaymentProvider
{
    private readonly FakePaymentProvider fallback = new();
    public Task<PaymentProviderCapabilities> DiscoverCapabilitiesAsync(CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderCapabilities(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pix", "credit", "debit", "soft_pos" }));
    public Task<PaymentProviderStatus> StartPaymentAsync(StartPaymentRequest request, CancellationToken cancellationToken = default) => fallback.StartPaymentAsync(request, cancellationToken);
    public Task<PaymentProviderStatus> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default) => fallback.GetStatusAsync(providerReference, cancellationToken);
    public Task<PaymentProviderStatus> CancelPaymentAsync(string providerReference, CancellationToken cancellationToken = default) => fallback.CancelPaymentAsync(providerReference, cancellationToken);
    public Task<PaymentProviderStatus> ReconcilePaymentAsync(string providerReference, CancellationToken cancellationToken = default) => fallback.ReconcilePaymentAsync(providerReference, cancellationToken);
    public Task<PaymentProviderLookupResult> LookupPaymentAsync(PaymentProviderLookupRequest request, CancellationToken cancellationToken = default) => fallback.LookupPaymentAsync(request, cancellationToken);
    public Task<RefundProviderResult> RefundAsync(RefundProviderRequest request, CancellationToken cancellationToken = default) => Outcome(request.RefundId, false, cancellationToken);
    public Task<RefundProviderResult> LookupRefundAsync(RefundProviderLookupRequest request, CancellationToken cancellationToken = default) => Outcome(request.RefundId, true, cancellationToken);
    private async Task<RefundProviderResult> Outcome(Guid refundId, bool lookup, CancellationToken ct)
    {
        await using var c = new NpgsqlConnection(connectionString); await c.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("select case when not c.released then case when $2 then 'unknown' else 'processing' end else case when $2 then c.lookup_outcome else c.refund_outcome end end from payments.refund_provider_execution e join e2e_control.refund_provider_scenario c on c.provider = e.provider where e.refund_id = $1", c); cmd.Parameters.AddWithValue(refundId); cmd.Parameters.AddWithValue(lookup);
        var value = (string?)await cmd.ExecuteScalarAsync(ct) ?? "processing";
        return new RefundProviderResult(value, $"e2e:{refundId:N}");
    }
}
