using Appizza.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appizza.Modules.Catalog;

#pragma warning disable CA1725

public static class Phase2Permissions
{
    public static readonly string[] All =
    [
        "catalog.read", "catalog.write", "catalog.publish", "catalog.availability.manage", "media.read", "media.write"
    ];
}

public sealed class CatalogState : IVersionedEntity
{
    public Guid EstablishmentId { get; set; }
    public long CatalogVersion { get; set; }
    public long AvailabilityVersion { get; set; }
    public Guid? CurrentPublishedRevisionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class CatalogRevision
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public long? CatalogVersion { get; set; }
    public string Status { get; set; } = "validating";
    public string Snapshot { get; set; } = "{}";
    public string SemanticHash { get; set; } = null!;
    public string? ValidationErrors { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
    public Guid? PublishedBy { get; set; }
}

public sealed class IngredientAvailability : IVersionedEntity
{
    public Guid IngredientId { get; set; }
    public Guid EstablishmentId { get; set; }
    public bool ExplicitlyAvailable { get; set; } = true;
    public bool EffectivelyAvailable { get; set; } = true;
    public string? Reason { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public long Version { get; set; }
}

public sealed class ProductAvailability : IVersionedEntity
{
    public Guid ProductId { get; set; }
    public Guid EstablishmentId { get; set; }
    public bool ExplicitlyAvailable { get; set; } = true;
    public bool EffectivelyAvailable { get; set; } = true;
    public string? DerivedReason { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public long Version { get; set; }
}

public sealed class ProductVariantAvailability : IVersionedEntity
{
    public Guid ProductVariantId { get; set; }
    public Guid EstablishmentId { get; set; }
    public bool ExplicitlyAvailable { get; set; } = true;
    public bool EffectivelyAvailable { get; set; } = true;
    public string? DerivedReason { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public long Version { get; set; }
}

public sealed class CatalogStateConfiguration : IEntityTypeConfiguration<CatalogState>
{
    public void Configure(EntityTypeBuilder<CatalogState> b) { b.ToTable("catalog_state", "catalog", t => { t.HasCheckConstraint("ck_catalog_state_catalog_version", "catalog_version >= 0"); t.HasCheckConstraint("ck_catalog_state_availability_version", "availability_version >= 0"); }); b.HasKey(x => x.EstablishmentId); b.Property(x => x.Version).IsConcurrencyToken(); }
}
public sealed class CatalogRevisionConfiguration : IEntityTypeConfiguration<CatalogRevision>
{
    public void Configure(EntityTypeBuilder<CatalogRevision> b) { b.ToTable("catalog_revision", "catalog", t => t.HasCheckConstraint("ck_catalog_revision_status", "status in ('validating','published','rejected','superseded')")); b.HasKey(x => x.Id); b.Property(x => x.Snapshot).HasColumnType("jsonb"); b.Property(x => x.ValidationErrors).HasColumnType("jsonb"); b.Property(x => x.SemanticHash).HasMaxLength(64); b.HasIndex(x => new { x.EstablishmentId, x.CatalogVersion }).IsUnique().HasFilter("catalog_version is not null"); b.HasIndex(x => new { x.EstablishmentId, x.Status }); }
}
public sealed class IngredientAvailabilityConfiguration : IEntityTypeConfiguration<IngredientAvailability>
{
    public void Configure(EntityTypeBuilder<IngredientAvailability> b) { b.ToTable("ingredient_availability", "catalog"); b.HasKey(x => x.IngredientId); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.EstablishmentId, x.EffectivelyAvailable }); }
}
public sealed class ProductAvailabilityConfiguration : IEntityTypeConfiguration<ProductAvailability>
{
    public void Configure(EntityTypeBuilder<ProductAvailability> b) { b.ToTable("product_availability", "catalog"); b.HasKey(x => x.ProductId); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.EstablishmentId, x.EffectivelyAvailable }); }
}
public sealed class ProductVariantAvailabilityConfiguration : IEntityTypeConfiguration<ProductVariantAvailability>
{
    public void Configure(EntityTypeBuilder<ProductVariantAvailability> b) { b.ToTable("product_variant_availability", "catalog"); b.HasKey(x => x.ProductVariantId); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.EstablishmentId, x.EffectivelyAvailable }); }
}
