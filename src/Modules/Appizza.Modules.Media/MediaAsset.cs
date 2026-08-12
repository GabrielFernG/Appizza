using Appizza.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appizza.Modules.Media;

public sealed class MediaAsset : IVersionedEntity
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public string FileName { get; set; } = null!;
    public string ObjectKey { get; set; } = null!;
    public string MimeType { get; set; } = null!;
    public long FileSize { get; set; }
    public string ChecksumSha256 { get; set; } = null!;
    public string Status { get; set; } = "pending_upload";
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ReadyAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public long Version { get; set; }
}

public sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.ToTable("asset", "media", table =>
        {
            table.HasCheckConstraint("ck_media_asset_status", "status in ('pending_upload','ready','failed','archived')");
            table.HasCheckConstraint("ck_media_asset_size", "file_size > 0");
            table.HasCheckConstraint("ck_media_asset_checksum", "length(checksum_sha256) = 64");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FileName).HasMaxLength(255);
        builder.Property(x => x.ObjectKey).HasMaxLength(512);
        builder.Property(x => x.MimeType).HasMaxLength(100);
        builder.Property(x => x.ChecksumSha256).HasMaxLength(64);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => x.ObjectKey).IsUnique();
        builder.HasIndex(x => new { x.EstablishmentId, x.Status });
    }
}
