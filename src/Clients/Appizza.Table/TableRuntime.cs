using System.Net.Http.Headers;
using System.Text.Json;
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
    public static Guid TenantFromToken(string token) { var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/'); payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '='); using var json = JsonDocument.Parse(Convert.FromBase64String(payload)); return Guid.Parse(json.RootElement.GetProperty("establishment_id").GetString()!); }
}
