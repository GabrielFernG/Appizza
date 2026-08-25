using System.Text.Json;
using Appizza.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appizza.Modules.Promotions;

public static class PromotionKinds { public const string Percentage = "percentage"; public const string FixedAmount = "fixed_amount"; }
public static class PromotionScopes { public const string EntireOrder = "entire_order"; public const string SpecificProducts = "specific_products"; }
public static class Phase6PromotionPermissions { public static readonly string[] All = ["promotions.view", "promotions.create", "promotions.edit", "promotions.activate"]; }
public sealed class Promotion : IVersionedEntity
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public string Name { get; set; } = null!;
    public string Status { get; set; } = "draft";
    public int Priority { get; set; }
    public Guid? CurrentVersionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}
public sealed class PromotionVersion : IVersionedEntity
{
    public Guid Id { get; set; }
    public Guid PromotionId { get; set; }
    public Guid EstablishmentId { get; set; }
    public string Kind { get; set; } = null!;
    public string Scope { get; set; } = PromotionScopes.EntireOrder;
    public decimal Value { get; set; }
    public string EligibleProductIds { get; set; } = "[]";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
}
public sealed class PromotionApplication : IVersionedEntity
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public Guid OrderId { get; set; }
    public Guid PromotionId { get; set; }
    public Guid PromotionVersionId { get; set; }
    public decimal EligibleBaseAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string Snapshot { get; set; } = null!;
    public DateTimeOffset AppliedAt { get; set; }
    public long Version { get; set; }
}
public sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder) { builder.ToTable("promotion", "promotions", t => t.HasCheckConstraint("ck_promotion_status", "status in ('draft','active','inactive','expired')")); builder.HasKey(x => x.Id); builder.Property(x => x.Name).HasMaxLength(200); builder.Property(x => x.Status).HasMaxLength(30); builder.HasIndex(x => new { x.EstablishmentId, x.Status }); builder.Property(x => x.Version).IsConcurrencyToken(); }
}
public sealed class PromotionVersionConfiguration : IEntityTypeConfiguration<PromotionVersion>
{
    public void Configure(EntityTypeBuilder<PromotionVersion> builder) { builder.ToTable("promotion_version", "promotions", t => { t.HasCheckConstraint("ck_promotion_version_kind", "kind in ('percentage','fixed_amount')"); t.HasCheckConstraint("ck_promotion_version_scope", "scope in ('entire_order','specific_products')"); t.HasCheckConstraint("ck_promotion_version_value", "value >= 0"); }); builder.HasKey(x => x.Id); builder.Property(x => x.Kind).HasMaxLength(30); builder.Property(x => x.Scope).HasMaxLength(30); builder.Property(x => x.Value).HasPrecision(14,2); builder.Property(x => x.EligibleProductIds).HasColumnType("jsonb"); builder.HasIndex(x => new { x.EstablishmentId, x.StartsAt, x.EndsAt }); builder.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class PromotionApplicationConfiguration : IEntityTypeConfiguration<PromotionApplication>
{
    public void Configure(EntityTypeBuilder<PromotionApplication> builder) { builder.ToTable("promotion_application", "promotions"); builder.HasKey(x => x.Id); builder.Property(x => x.EligibleBaseAmount).HasPrecision(14,2); builder.Property(x => x.DiscountAmount).HasPrecision(14,2); builder.Property(x => x.Snapshot).HasColumnType("jsonb"); builder.HasIndex(x => x.OrderId).IsUnique(); builder.HasIndex(x => new { x.EstablishmentId, x.PromotionId }); }
}
public sealed record PromotionDiscount(Guid PromotionId, Guid VersionId, string Name, decimal BaseAmount, decimal DiscountAmount, string Kind, string Scope, int Priority, decimal Value, string ProductIds);
