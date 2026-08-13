using Appizza.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appizza.Modules.Kitchen;

#pragma warning disable CA1725

public static class Phase4KitchenPermissions
{
    public static readonly string[] All = ["kitchen.queue.view", "kitchen.production.view", "kitchen.production.accept"];
}

public sealed class Station : IVersionedEntity
{
    public Guid Id { get; set; } public Guid EstablishmentId { get; set; } public string Name { get; set; } = null!; public string StationType { get; set; } = "general"; public string Status { get; set; } = "active"; public bool IsDefault { get; set; } public int DisplayOrder { get; set; } public int? DefaultTargetMinutes { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; } public long Version { get; set; }
}
public sealed class ProductionItem : IVersionedEntity
{
    public Guid Id { get; set; } public Guid EstablishmentId { get; set; } public Guid OrderItemId { get; set; } public Guid StationId { get; set; } public string Status { get; set; } = "awaiting_acceptance"; public long QueuePosition { get; set; } public bool RequiresProduction { get; set; } public DateTimeOffset ReceivedAt { get; set; } public DateTimeOffset? AcceptedAt { get; set; } public Guid? AcceptedByUserId { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; } public long Version { get; set; }
}
public sealed class ProductionStatusHistory { public Guid Id { get; set; } public Guid ProductionItemId { get; set; } public string? PreviousStatus { get; set; } public string NewStatus { get; set; } = null!; public Guid? UserId { get; set; } public DateTimeOffset ChangedAt { get; set; } public Guid? CorrelationId { get; set; } }

public sealed class StationConfiguration : IEntityTypeConfiguration<Station> { public void Configure(EntityTypeBuilder<Station> b) { b.ToTable("station", "kitchen", t => t.HasCheckConstraint("ck_station_status", "status in ('active','inactive')")); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120); b.Property(x => x.StationType).HasMaxLength(50); b.Property(x => x.Status).HasMaxLength(30); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.EstablishmentId, x.Name }).IsUnique(); b.HasIndex(x => x.EstablishmentId).IsUnique().HasFilter("is_default and status = 'active'"); } }
public sealed class ProductionItemConfiguration : IEntityTypeConfiguration<ProductionItem> { public void Configure(EntityTypeBuilder<ProductionItem> b) { b.ToTable("production_item", "kitchen", t => t.HasCheckConstraint("ck_production_item_phase4_status", "status in ('awaiting_acceptance','accepted','awaiting_preparation')")); b.HasKey(x => x.Id); b.Property(x => x.QueuePosition).HasDefaultValueSql("nextval('kitchen.production_queue_position_seq')"); b.Property(x => x.Status).HasMaxLength(50); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => x.OrderItemId).IsUnique(); b.HasIndex(x => new { x.EstablishmentId, x.StationId, x.Status, x.QueuePosition }); } }
public sealed class ProductionStatusHistoryConfiguration : IEntityTypeConfiguration<ProductionStatusHistory> { public void Configure(EntityTypeBuilder<ProductionStatusHistory> b) { b.ToTable("production_status_history", "kitchen"); b.HasKey(x => x.Id); b.Property(x => x.PreviousStatus).HasMaxLength(50); b.Property(x => x.NewStatus).HasMaxLength(50); b.HasIndex(x => new { x.ProductionItemId, x.ChangedAt }); } }
