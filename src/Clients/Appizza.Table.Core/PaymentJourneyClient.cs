using System.Net.Http.Json;
using System.Text.Json;

namespace Appizza.Table.Core;

/// Pure HTTP boundary for the Table payment journey; server responses remain authoritative.
public sealed class PaymentJourneyClient(HttpClient http)
{
    public Task<JsonElement> LoadBalanceAsync(CancellationToken ct = default) => SendGetAsync("api/v1/table-device/session/balance", ct);
    public Task<JsonElement> CreatePlanAsync(Guid sessionId, Guid key, CancellationToken ct = default) => SendPostAsync("api/v1/table-device/session/payment-plan", new { sessionId, mode = "total" }, key, ct);
    public Task<JsonElement> CreateAttemptAsync(Guid sessionId, Guid planId, IEnumerable<Guid> allocationIds, string method, Guid key, CancellationToken ct = default)
        => SendPostAsync("api/v1/table-device/payments/attempts", new { tableSessionId = sessionId, paymentPlanId = planId, allocationIds = allocationIds.ToArray(), paymentMethod = method }, key, ct);

    private async Task<JsonElement> SendGetAsync(string uri, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();
        return (await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct)).RootElement.Clone();
    }

    private async Task<JsonElement> SendPostAsync(string uri, object body, Guid key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key.ToString());
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return (await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct)).RootElement.Clone();
    }
}
