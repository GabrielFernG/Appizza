using System.Security.Cryptography;

namespace Appizza.Table.Core;

public sealed record MediaCacheOptions(long MaximumBytes = LocalContract.DefaultMediaCacheBytes, long MinimumFreeBytes = 256L * 1024 * 1024);
public interface IFreeSpaceProvider { long AvailableBytes(string path); }
public sealed class SystemFreeSpaceProvider : IFreeSpaceProvider { public long AvailableBytes(string path) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace; }

public sealed class MediaManifestSynchronizer(HttpClient client, MediaCacheService cache)
{
    public async Task SynchronizeAsync(LocalContext context, string menuPayloadJson, CancellationToken cancellationToken)
    {
        using var document = System.Text.Json.JsonDocument.Parse(menuPayloadJson); if (!document.RootElement.TryGetProperty("mediaManifest", out var manifest) || manifest.ValueKind != System.Text.Json.JsonValueKind.Array) return;
        foreach (var item in manifest.EnumerateArray())
        {
            var assetId = item.GetProperty("assetId").GetGuid(); var checksum = item.GetProperty("checksumSha256").GetString()!; if (await cache.TryGetAsync(context, assetId, checksum) is not null) continue;
            try { using var response = await client.GetAsync($"api/v1/table-device/media-assets/{assetId}/content", System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken); if (!response.IsSuccessStatusCode) continue; await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken); await cache.StoreAsync(context, assetId, checksum, item.GetProperty("mimeType").GetString()!, stream, cancellationToken); } catch (HttpRequestException) { } catch (IOException) { }
        }
    }
}

public sealed class MediaCacheService(LocalStateDatabase database, string rootPath, MediaCacheOptions options, IFreeSpaceProvider freeSpace, Func<DateTime> utcNow)
{
    public async Task<string> StoreAsync(LocalContext context, Guid assetId, string checksumSha256, string mimeType, Stream content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(rootPath); var temporary = Path.Combine(rootPath, $"{Guid.NewGuid():N}.tmp"); var finalName = $"{assetId:N}-{checksumSha256}"; var final = Path.Combine(rootPath, finalName); try { await using (var output = File.Create(temporary)) await content.CopyToAsync(output, cancellationToken); string actual; await using (var input = File.OpenRead(temporary)) actual = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)).ToLowerInvariant(); if (!actual.Equals(checksumSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Media checksum mismatch."); var size = new FileInfo(temporary).Length; await EnsureCapacityAsync(context, size); File.Move(temporary, final, true); var now = utcNow(); await database.Connection.InsertOrReplaceAsync(new MediaCacheRow { Id = CacheId(context, assetId), EstablishmentId = context.EstablishmentId.ToString("N"), DeviceId = context.DeviceId.ToString("N"), AssetId = assetId.ToString("N"), ChecksumSha256 = checksumSha256, MimeType = mimeType, FileSize = size, LocalRelativePath = finalName, DownloadedAtUtc = now, LastAccessedAtUtc = now }); return final; } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<string?> TryGetAsync(LocalContext context, Guid assetId, string checksum)
    { var cacheId = CacheId(context, assetId); var row = await database.Connection.Table<MediaCacheRow>().Where(x => x.Id == cacheId && x.ChecksumSha256 == checksum && x.Status == "ready").FirstOrDefaultAsync(); if (row is null) return null; var path = SafePath(row.LocalRelativePath); if (!File.Exists(path)) { await database.Connection.DeleteAsync(row); return null; } row.LastAccessedAtUtc = utcNow(); await database.Connection.UpdateAsync(row); return path; }

    private async Task EnsureCapacityAsync(LocalContext context, long incomingBytes)
    {
        var establishmentId = context.EstablishmentId.ToString("N"); var deviceId = context.DeviceId.ToString("N");
        if (freeSpace.AvailableBytes(rootPath) - incomingBytes < options.MinimumFreeBytes) await EvictAsync(context, incomingBytes, true);
        var rows = await database.Connection.Table<MediaCacheRow>().Where(x => x.EstablishmentId == establishmentId && x.DeviceId == deviceId).ToListAsync();
        var total = rows.Sum(x => x.FileSize);
        if (total + incomingBytes > options.MaximumBytes) await EvictAsync(context, total + incomingBytes - options.MaximumBytes, false);
        if (freeSpace.AvailableBytes(rootPath) - incomingBytes < options.MinimumFreeBytes) throw new IOException("Critical free space protection prevented media caching.");
    }

    private async Task EvictAsync(LocalContext context, long bytes, bool critical)
    { long removed = 0; var establishmentId = context.EstablishmentId.ToString("N"); var deviceId = context.DeviceId.ToString("N"); var rows = await database.Connection.Table<MediaCacheRow>().Where(x => x.EstablishmentId == establishmentId && x.DeviceId == deviceId).OrderBy(x => x.LastAccessedAtUtc).ToListAsync(); foreach (var row in rows) { var path = SafePath(row.LocalRelativePath); if (File.Exists(path)) File.Delete(path); await database.Connection.DeleteAsync(row); removed += row.FileSize; if (removed >= bytes && (!critical || freeSpace.AvailableBytes(rootPath) >= options.MinimumFreeBytes)) break; } }
    private string SafePath(string relative) { var path = Path.GetFullPath(Path.Combine(rootPath, relative)); var root = Path.GetFullPath(rootPath) + Path.DirectorySeparatorChar; if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe media cache path."); return path; }
    private static string CacheId(LocalContext context, Guid assetId) => $"{context.EstablishmentId:N}:{context.DeviceId:N}:{assetId:N}";
}
