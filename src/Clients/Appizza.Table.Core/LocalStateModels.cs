using SQLite;

namespace Appizza.Table.Core;

public static class LocalContract
{
    public const int DatabaseVersion = 2;
    public const int MenuSchemaVersion = 1;
    public const long DefaultMediaCacheBytes = 512L * 1024 * 1024;
    public const int OldCartRetentionDays = 7;
}

public sealed record LocalContext(Guid EstablishmentId, Guid DeviceId, Guid? SessionId);

[Table("catalog_cache")]
public sealed class CatalogCacheRow
{
    [PrimaryKey] public string Id { get; set; } = null!;
    [Indexed] public string EstablishmentId { get; set; } = null!;
    [Indexed] public string DeviceId { get; set; } = null!;
    public string CatalogRevisionId { get; set; } = null!;
    public long CatalogVersion { get; set; }
    public long AvailabilityVersion { get; set; }
    public int SchemaVersion { get; set; }
    public string ETag { get; set; } = null!;
    public string PayloadJson { get; set; } = null!;
    public bool IsActive { get; set; }
    public DateTime DownloadedAtUtc { get; set; }
}

[Table("availability_cache")]
public sealed class AvailabilityCacheRow
{
    [PrimaryKey] public string Id { get; set; } = null!;
    [Indexed] public string EstablishmentId { get; set; } = null!;
    [Indexed] public string DeviceId { get; set; } = null!;
    public long CatalogVersion { get; set; }
    public long AvailabilityVersion { get; set; }
    public int SchemaVersion { get; set; }
    public string ETag { get; set; } = null!;
    public string PayloadJson { get; set; } = null!;
    public DateTime DownloadedAtUtc { get; set; }
}

[Table("local_cart")]
public sealed class LocalCartRow
{
    [PrimaryKey] public string Id { get; set; } = null!;
    [Indexed] public string EstablishmentId { get; set; } = null!;
    [Indexed] public string DeviceId { get; set; } = null!;
    [Indexed] public string SessionId { get; set; } = null!;
    public long CatalogVersion { get; set; }
    public long AvailabilityVersion { get; set; }
    public string Status { get; set; } = "active";
    public string? SimulationId { get; set; }
    public string? SimulationVersion { get; set; }
    public DateTime? SimulationValidUntilUtc { get; set; }
    public bool RequiresReview { get; set; }
    public string? ClientSubmissionId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? AuthoritativeResultJson { get; set; }
    public string? SubmittedOrderId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

[Table("local_cart_item")]
public sealed class LocalCartItemRow
{
    [PrimaryKey] public string Id { get; set; } = null!;
    [Indexed] public string CartId { get; set; } = null!;
    public string ProductId { get; set; } = null!;
    public string? ProductVariantId { get; set; }
    public string ProductType { get; set; } = null!;
    public decimal Quantity { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
    public string ConfigurationVersion { get; set; } = null!;
    public long SourceCatalogVersion { get; set; }
    public long SourceAvailabilityVersion { get; set; }
    public decimal EstimatedUnitAmount { get; set; }
    public decimal EstimatedTotalAmount { get; set; }
    public string ValidationState { get; set; } = "valid_estimate";
    public string ValidationMessagesJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

[Table("media_cache_entry")]
public sealed class MediaCacheRow
{
    [PrimaryKey] public string Id { get; set; } = null!;
    [Indexed] public string EstablishmentId { get; set; } = null!;
    [Indexed] public string DeviceId { get; set; } = null!;
    public string AssetId { get; set; } = null!;
    public string ChecksumSha256 { get; set; } = null!;
    public string MimeType { get; set; } = null!;
    public long FileSize { get; set; }
    public string LocalRelativePath { get; set; } = null!;
    public string Status { get; set; } = "ready";
    public DateTime LastAccessedAtUtc { get; set; }
    public DateTime DownloadedAtUtc { get; set; }
}

[Table("sync_state")]
public sealed class SyncStateRow
{
    [PrimaryKey] public string Id { get; set; } = null!;
    public string EstablishmentId { get; set; } = null!;
    public string DeviceId { get; set; } = null!;
    public DateTime? LastSuccessfulSyncAtUtc { get; set; }
    public string? PendingRefreshReason { get; set; }
    public string ConnectionState { get; set; } = "unknown";
}

public sealed record CachedCatalog(string PayloadJson, string ETag, long CatalogVersion, long AvailabilityVersion, int SchemaVersion);
public sealed record CachedAvailability(string PayloadJson, string ETag, long CatalogVersion, long AvailabilityVersion, int SchemaVersion);
public sealed record CartItemInput(Guid Id, Guid ProductId, Guid? ProductVariantId, string ProductType, decimal Quantity, string ConfigurationJson, string ConfigurationVersion, long CatalogVersion, long AvailabilityVersion, decimal EstimatedUnitAmount, string ValidationState = "valid_estimate");
public sealed record LocalSubmissionState(Guid CartId, string Status, Guid? SimulationId, string? SimulationVersion, DateTime? ValidUntilUtc, bool RequiresReview, Guid? ClientSubmissionId, Guid? IdempotencyKey, string? ResultJson, Guid? OrderId);
