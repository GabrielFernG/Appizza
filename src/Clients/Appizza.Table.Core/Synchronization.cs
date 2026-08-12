using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace Appizza.Table.Core;

public sealed record MenuDownload(HttpStatusCode StatusCode, string? PayloadJson, string? ETag);

public interface ITableMenuApi
{
    Task<MenuDownload> GetMenuAsync(string? etag, CancellationToken cancellationToken);
    Task<MenuDownload> GetAvailabilityAsync(long catalogVersion, string? etag, CancellationToken cancellationToken);
}

public sealed class TableMenuApi(HttpClient client) : ITableMenuApi
{
    public Task<MenuDownload> GetMenuAsync(string? etag, CancellationToken cancellationToken) => GetAsync("api/v1/table-device/menu", etag, cancellationToken);
    public Task<MenuDownload> GetAvailabilityAsync(long catalogVersion, string? etag, CancellationToken cancellationToken) => GetAsync($"api/v1/table-device/menu/availability?catalogVersion={catalogVersion}", etag, cancellationToken);

    private async Task<MenuDownload> GetAsync(string path, string? etag, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path); if (!string.IsNullOrWhiteSpace(etag)) request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag)); using var response = await client.SendAsync(request, cancellationToken); if (response.StatusCode == HttpStatusCode.NotModified) return new(response.StatusCode, null, etag); var content = await response.Content.ReadAsStringAsync(cancellationToken); response.EnsureSuccessStatusCode(); return new(response.StatusCode, content, response.Headers.ETag?.Tag);
    }
}

public enum SynchronizationStatus { Current, Refreshed, StaleOffline, Unavailable, Incompatible }
public sealed record SynchronizationResult(SynchronizationStatus Status, CachedCatalog? Catalog, string? Message = null);

public sealed class MenuSynchronizationService(LocalStateDatabase database, ITableMenuApi api, Func<bool> hasInternet, Func<DateTime> utcNow)
{
    public async Task<SynchronizationResult> InitializeAsync(LocalContext context, CancellationToken cancellationToken)
    {
        var cached = await database.GetActiveCatalogAsync(context); if (!hasInternet()) return cached is null ? new(SynchronizationStatus.Unavailable, null, "offline_without_compatible_cache") : new(SynchronizationStatus.StaleOffline, cached);
        try
        {
            if (cached is not null)
            {
                var currentAvailability = await database.GetAvailabilityAsync(context); var overlay = await api.GetAvailabilityAsync(cached.CatalogVersion, currentAvailability?.ETag, cancellationToken);
                if (overlay.StatusCode == HttpStatusCode.OK && overlay.PayloadJson is not null && overlay.ETag is not null)
                { var availabilityMetadata = ParseAvailabilityMetadata(overlay.PayloadJson); if (availabilityMetadata.SchemaVersion == LocalContract.MenuSchemaVersion) { await database.InstallAvailabilityAsync(context, availabilityMetadata.CatalogVersion, availabilityMetadata.AvailabilityVersion, availabilityMetadata.SchemaVersion, overlay.ETag, overlay.PayloadJson, utcNow()); cached = await database.ApplyAvailabilityAsync(context, cached, (await database.GetAvailabilityAsync(context))!, utcNow()); } }
            }
            var response = await api.GetMenuAsync(cached?.ETag, cancellationToken); if (response.StatusCode == HttpStatusCode.NotModified) return new(SynchronizationStatus.Current, cached);
            if (response.PayloadJson is null || response.ETag is null) return new(SynchronizationStatus.Unavailable, cached, "empty_menu_response");
            var metadata = ParseMenuMetadata(response.PayloadJson); if (metadata.SchemaVersion != LocalContract.MenuSchemaVersion) return cached is null ? new(SynchronizationStatus.Incompatible, null, "unsupported_schema") : new(SynchronizationStatus.Current, cached, "future_schema_ignored");
            await database.InstallCatalogAsync(context, metadata.RevisionId, metadata.CatalogVersion, metadata.AvailabilityVersion, metadata.SchemaVersion, response.ETag, response.PayloadJson, utcNow());
            using (var full = JsonDocument.Parse(response.PayloadJson)) { var availability = full.RootElement.GetProperty("availability").GetRawText(); var availabilityEtag = $"\"availability-{metadata.AvailabilityVersion}-schema-{metadata.SchemaVersion}\""; await database.InstallAvailabilityAsync(context, metadata.CatalogVersion, metadata.AvailabilityVersion, metadata.SchemaVersion, availabilityEtag, availability, utcNow()); }
            return new(SynchronizationStatus.Refreshed, await database.GetActiveCatalogAsync(context));
        }
        catch (HttpRequestException) when (cached is not null) { return new(SynchronizationStatus.StaleOffline, cached); }
    }

    public async Task<SynchronizationResult> ReconcileAvailabilityAsync(LocalContext context, CachedCatalog catalog, string? availabilityEtag, CancellationToken cancellationToken)
    {
        if (!hasInternet()) return new(SynchronizationStatus.StaleOffline, catalog); try { var response = await api.GetAvailabilityAsync(catalog.CatalogVersion, availabilityEtag, cancellationToken); if (response.StatusCode == HttpStatusCode.NotModified) return new(SynchronizationStatus.Current, catalog); if (response.PayloadJson is null || response.ETag is null) return new(SynchronizationStatus.Unavailable, catalog); using var document = JsonDocument.Parse(response.PayloadJson); var root = document.RootElement; var schema = root.GetProperty("schemaVersion").GetInt32(); if (schema != LocalContract.MenuSchemaVersion) return new(SynchronizationStatus.Incompatible, catalog); var catalogVersion = root.GetProperty("catalogVersion").GetInt64(); var availabilityVersion = root.GetProperty("availabilityVersion").GetInt64(); await database.InstallAvailabilityAsync(context, catalogVersion, availabilityVersion, schema, response.ETag, response.PayloadJson, utcNow()); return new(SynchronizationStatus.Refreshed, catalog with { AvailabilityVersion = availabilityVersion }); } catch (HttpRequestException) { return new(SynchronizationStatus.StaleOffline, catalog); }
    }

    private static (Guid RevisionId, long CatalogVersion, long AvailabilityVersion, int SchemaVersion) ParseMenuMetadata(string json)
    { using var document = JsonDocument.Parse(json); var root = document.RootElement; var schema = root.GetProperty("schemaVersion").GetInt32(); var menu = root.GetProperty("menu"); return (menu.GetProperty("catalogRevisionId").GetGuid(), menu.GetProperty("catalogVersion").GetInt64(), menu.GetProperty("availabilityVersion").GetInt64(), schema); }
    private static (long CatalogVersion, long AvailabilityVersion, int SchemaVersion) ParseAvailabilityMetadata(string json) { using var document = JsonDocument.Parse(json); var root = document.RootElement; return (root.GetProperty("catalogVersion").GetInt64(), root.GetProperty("availabilityVersion").GetInt64(), root.GetProperty("schemaVersion").GetInt32()); }
}

public sealed record CatalogInvalidation(string Type, long? CatalogVersion, long? AvailabilityVersion);
public enum ReconciliationTrigger { Startup, SignalRInvalidation, SignalRReconnected, Foreground, Resume, Periodic }

public sealed class ReconciliationCoordinator(MenuSynchronizationService synchronization) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<SynchronizationResult> ReconcileAsync(LocalContext context, ReconciliationTrigger trigger, CancellationToken cancellationToken)
    { await _gate.WaitAsync(cancellationToken); try { return await synchronization.InitializeAsync(context, cancellationToken); } finally { _gate.Release(); } }
    public void Dispose() => _gate.Dispose();
}

public sealed class CatalogRealtimeClient(Uri hubUri, Func<Task<string?>> accessTokenProvider) : IAsyncDisposable
{
    private readonly HubConnection _connection = new HubConnectionBuilder().WithUrl(hubUri, options => options.AccessTokenProvider = accessTokenProvider).WithAutomaticReconnect().Build();
    public event Func<CatalogInvalidation, Task>? Invalidated;
    public event Func<Task>? Reconnected;
    public HubConnectionState State => _connection.State;
    public async Task StartAsync(CancellationToken cancellationToken) { _connection.On<CatalogInvalidation>("CatalogInvalidated", notification => Invalidated?.Invoke(notification) ?? Task.CompletedTask); _connection.Reconnected += _ => Reconnected?.Invoke() ?? Task.CompletedTask; await _connection.StartAsync(cancellationToken); }
    public Task StopAsync(CancellationToken cancellationToken) => _connection.StopAsync(cancellationToken);
    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
