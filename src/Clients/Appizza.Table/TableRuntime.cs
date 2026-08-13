using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using Appizza.Table.Core;

namespace Appizza.Table;

public static class TableRuntime
{
    public static HttpClient Http { get; } = new();
    public static LocalStateDatabase Database { get; } = new(Path.Combine(FileSystem.AppDataDirectory, "appizza-table-v1.db3"));
    public static MediaCacheService MediaCache { get; } = new(Database, Path.Combine(FileSystem.CacheDirectory, "menu-media"), new MediaCacheOptions(), new SystemFreeSpaceProvider(), () => DateTime.UtcNow);
    public static LocalContext? Context { get; set; }
    public static MenuPresentation? Menu { get; set; }
    public static MenuProduct? SelectedProduct { get; set; }
    public static bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
    private static CatalogRealtimeClient? _realtime;
    public static async Task ActivateAsync(Uri apiBase, string accessToken, Guid establishmentId, Guid deviceId, Guid sessionId) { Http.BaseAddress = apiBase; Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken); Context = new(establishmentId, deviceId, sessionId); await Database.InitializeAsync(); _realtime = new(new Uri(apiBase, "hubs/v1/updates"), () => Task.FromResult<string?>(accessToken)); _realtime.Invalidated += _ => ReconcileAsync(ReconciliationTrigger.SignalRInvalidation); _realtime.Reconnected += () => ReconcileAsync(ReconciliationTrigger.SignalRReconnected); try { await _realtime.StartAsync(CancellationToken.None); } catch (HttpRequestException) { } }
    public static async Task ReconcileAsync(ReconciliationTrigger trigger) { if (Context is not LocalContext context || Http.BaseAddress is null) return; var sync = new MenuSynchronizationService(Database, new TableMenuApi(Http), () => IsOnline, () => DateTime.UtcNow); using var coordinator = new ReconciliationCoordinator(sync); await coordinator.ReconcileAsync(context, trigger, CancellationToken.None); }
    public static async Task<JsonDocument> SimulateCartAsync(LocalCartRow cart, IReadOnlyList<LocalCartItemRow> items)
    {
        if (Context is not LocalContext context || context.SessionId is not Guid sessionId) throw new InvalidOperationException("Sessão ativa obrigatória."); var body = new { sessionId, localCartId = Guid.ParseExact(cart.Id, "N"), catalogVersion = cart.CatalogVersion, availabilityVersion = cart.AvailabilityVersion, items = items.Select(x => new { localCartItemId = Guid.ParseExact(x.Id, "N"), productId = Guid.ParseExact(x.ProductId, "N"), productVariantId = x.ProductVariantId is null ? (Guid?)null : Guid.ParseExact(x.ProductVariantId, "N"), quantity = (int)x.Quantity, x.ConfigurationVersion, x.EstimatedUnitAmount, configuration = JsonSerializer.Deserialize<JsonElement>(x.ConfigurationJson) }) }; using var response = await Http.PostAsync("api/v1/table-device/cart/simulate", new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")); var json = await response.Content.ReadAsStringAsync(); response.EnsureSuccessStatusCode(); var document = JsonDocument.Parse(json); var root = document.RootElement; await Database.RecordSimulationAsync(Guid.ParseExact(cart.Id, "N"), root.GetProperty("simulationId").GetGuid(), root.GetProperty("simulationVersion").GetString()!, root.GetProperty("validUntil").GetDateTime(), root.GetProperty("requiresReview").GetBoolean(), json, DateTime.UtcNow); return document;
    }
    public static async Task<JsonDocument> SubmitCartAsync(LocalCartRow cart, bool acceptedReview)
    {
        if (Context is not LocalContext context || context.SessionId is not Guid sessionId || cart.SimulationId is null || cart.SimulationVersion is null) throw new InvalidOperationException("Simulação válida obrigatória."); var cartId = Guid.ParseExact(cart.Id, "N"); var identities = await Database.BeginSubmissionAsync(cartId, cart.ClientSubmissionId is null ? Guid.NewGuid() : Guid.ParseExact(cart.ClientSubmissionId, "N"), cart.IdempotencyKey is null ? Guid.NewGuid() : Guid.ParseExact(cart.IdempotencyKey, "N"), DateTime.UtcNow); var body = new { sessionId, localCartId = cartId, clientSubmissionId = identities.ClientSubmissionId, simulationId = Guid.ParseExact(cart.SimulationId, "N"), cart.SimulationVersion, acceptedReview }; using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/table-device/orders") { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") }; request.Headers.Add("Idempotency-Key", identities.IdempotencyKey.ToString()); try { using var response = await Http.SendAsync(request); var json = await response.Content.ReadAsStringAsync(); response.EnsureSuccessStatusCode(); var document = JsonDocument.Parse(json); await Database.MarkSubmittedAsync(cartId, document.RootElement.GetProperty("order").GetProperty("id").GetGuid(), json, DateTime.UtcNow); return document; } catch (HttpRequestException) { await Database.MarkSubmissionUnknownAsync(cartId, DateTime.UtcNow); throw; }
    }
    public static async Task<JsonDocument> ReconcileSubmissionAsync(Guid cartId)
    { var state = await Database.GetSubmissionStateAsync(cartId); if (state.IdempotencyKey is not Guid key) throw new InvalidOperationException("Submissão desconhecida sem chave."); using var response = await Http.GetAsync($"api/v1/table-device/orders/submissions/{key}"); var json = await response.Content.ReadAsStringAsync(); response.EnsureSuccessStatusCode(); var document = JsonDocument.Parse(json); await Database.MarkSubmittedAsync(cartId, document.RootElement.GetProperty("order").GetProperty("id").GetGuid(), json, DateTime.UtcNow); return document; }
    public static Guid TenantFromToken(string token) { var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/'); payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '='); using var json = JsonDocument.Parse(Convert.FromBase64String(payload)); return Guid.Parse(json.RootElement.GetProperty("establishment_id").GetString()!); }
}
