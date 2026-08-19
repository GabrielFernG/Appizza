using Appizza.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appizza.Modules.Ordering;

#pragma warning disable CA1725

public static class Phase4Ordering
{
    public const string SimulationValiditySetting = "ordering.simulation_validity_seconds";
    public const int DefaultSimulationValiditySeconds = 300;
    public const int SnapshotSchemaVersion = 1;
}

public sealed class CartSimulation
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public Guid SourceDeviceId { get; set; }
    public Guid TableSessionId { get; set; }
    public Guid LocalCartId { get; set; }
    public Guid CatalogRevisionId { get; set; }
    public long CatalogVersion { get; set; }
    public long AvailabilityVersion { get; set; }
    public string RequestHash { get; set; } = null!;
    public string SimulationVersion { get; set; } = null!;
    public bool RequiresReview { get; set; }
    public bool CanSubmit { get; set; }
    public string IntentSnapshot { get; set; } = null!;
    public string ResultSnapshot { get; set; } = null!;
    public DateTimeOffset ValidUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Order : IVersionedEntity
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public Guid TableSessionId { get; set; }
    public Guid SourceDeviceId { get; set; }
    public Guid ClientSubmissionId { get; set; }
    public long OrderNumber { get; set; }
    public string Status { get; set; } = "submitted";
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class OrderItem : IVersionedEntity
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid LocalCartItemId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public string ProductType { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public string? VariantName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string ConfigurationVersion { get; set; } = null!;
    public string CommercialStatus { get; set; } = "submitted";
    public Guid CatalogRevisionId { get; set; }
    public long CatalogVersion { get; set; }
    public long AvailabilityVersion { get; set; }
    public string Snapshot { get; set; } = null!;
    public int SnapshotSchemaVersion { get; set; } = Phase4Ordering.SnapshotSchemaVersion;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
    public int CurrentRevisionNumber { get; set; }
}

public sealed class OrderItemIngredient { public Guid Id { get; set; } public Guid OrderItemId { get; set; } public Guid IngredientId { get; set; } public string Name { get; set; } = null!; public string Action { get; set; } = null!; public decimal Quantity { get; set; } public decimal AdditionalAmount { get; set; } }
public sealed class OrderItemOption { public Guid Id { get; set; } public Guid OrderItemId { get; set; } public Guid OptionId { get; set; } public string Name { get; set; } = null!; public int Quantity { get; set; } public decimal AdditionalAmount { get; set; } }
public sealed class OrderItemNote { public Guid Id { get; set; } public Guid OrderItemId { get; set; } public int Position { get; set; } public string Note { get; set; } = null!; }
public sealed class OrderItemPizzaConfiguration { public Guid OrderItemId { get; set; } public Guid SizeId { get; set; } public string SizeName { get; set; } = null!; public Guid? DoughId { get; set; } public string? DoughName { get; set; } public Guid? CrustId { get; set; } public string? CrustName { get; set; } public int FractionCount { get; set; } }
public sealed class OrderItemPizzaFraction { public Guid Id { get; set; } public Guid OrderItemId { get; set; } public int Position { get; set; } public Guid? FlavorId { get; set; } public string? FlavorName { get; set; } public bool IsCustom { get; set; } public int FractionNumerator { get; set; } = 1; public int FractionDenominator { get; set; } public decimal ReferenceAmount { get; set; } public string Configuration { get; set; } = "{}"; }
public sealed class OrderItemComboSelection { public Guid Id { get; set; } public Guid OrderItemId { get; set; } public Guid ComboGroupId { get; set; } public Guid ComboGroupItemId { get; set; } public Guid? SelectedProductId { get; set; } public Guid? SelectedVariantId { get; set; } public int Quantity { get; set; } public decimal ComponentAmount { get; set; } public string Configuration { get; set; } = "{}"; }

public sealed class CartSimulationConfiguration : IEntityTypeConfiguration<CartSimulation>
{
    public void Configure(EntityTypeBuilder<CartSimulation> b) { b.ToTable("cart_simulation", "ordering", t => t.HasCheckConstraint("ck_cart_simulation_versions", "catalog_version >= 0 and availability_version >= 0")); b.HasKey(x => x.Id); b.Property(x => x.RequestHash).HasMaxLength(128); b.Property(x => x.SimulationVersion).HasMaxLength(128); b.Property(x => x.IntentSnapshot).HasColumnType("jsonb"); b.Property(x => x.ResultSnapshot).HasColumnType("jsonb"); b.HasIndex(x => new { x.EstablishmentId, x.TableSessionId, x.ValidUntil }); b.HasIndex(x => new { x.EstablishmentId, x.SourceDeviceId, x.LocalCartId, x.CreatedAt }); }
}
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b) { b.ToTable("customer_order", "ordering", t => { t.HasCheckConstraint("ck_customer_order_status", "status in ('submitted','partially_cancelled','cancelled')"); t.HasCheckConstraint("ck_customer_order_amounts", "subtotal_amount >= 0 and discount_amount >= 0 and total_amount >= 0 and total_amount = subtotal_amount - discount_amount"); }); b.HasKey(x => x.Id); b.Property(x => x.OrderNumber).HasDefaultValueSql("nextval('ordering.order_number_seq')"); b.Property(x => x.Status).HasMaxLength(40); Money(b, nameof(Order.SubtotalAmount)); Money(b, nameof(Order.DiscountAmount)); Money(b, nameof(Order.TotalAmount)); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => x.OrderNumber).IsUnique(); b.HasIndex(x => new { x.EstablishmentId, x.SourceDeviceId, x.ClientSubmissionId }).IsUnique(); b.HasIndex(x => new { x.EstablishmentId, x.TableSessionId, x.SubmittedAt }); }
    private static void Money(EntityTypeBuilder<Order> b, string name) => b.Property<decimal>(name).HasPrecision(14, 2);
}
public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b) { b.ToTable("order_item", "ordering", t => { t.HasCheckConstraint("ck_order_item_status", "commercial_status in ('submitted','partially_cancelled','cancelled','completed')"); t.HasCheckConstraint("ck_order_item_quantity", "quantity > 0"); }); b.HasKey(x => x.Id); b.Property(x => x.ProductType).HasMaxLength(40); b.Property(x => x.ProductName).HasMaxLength(180); b.Property(x => x.VariantName).HasMaxLength(160); b.Property(x => x.ConfigurationVersion).HasMaxLength(128); b.Property(x => x.CommercialStatus).HasMaxLength(40); b.Property(x => x.Snapshot).HasColumnType("jsonb"); b.Property(x => x.UnitAmount).HasPrecision(14, 2); b.Property(x => x.TotalAmount).HasPrecision(14, 2); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.OrderId, x.LocalCartItemId }).IsUnique(); }
}
public sealed class OrderItemIngredientConfiguration : IEntityTypeConfiguration<OrderItemIngredient> { public void Configure(EntityTypeBuilder<OrderItemIngredient> b) { b.ToTable("order_item_ingredient", "ordering"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.Action).HasMaxLength(20); b.Property(x => x.Quantity).HasPrecision(12, 3); b.Property(x => x.AdditionalAmount).HasPrecision(14, 2); b.HasIndex(x => new { x.OrderItemId, x.IngredientId, x.Action }).IsUnique(); } }
public sealed class OrderItemOptionConfiguration : IEntityTypeConfiguration<OrderItemOption> { public void Configure(EntityTypeBuilder<OrderItemOption> b) { b.ToTable("order_item_option", "ordering"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.AdditionalAmount).HasPrecision(14, 2); b.HasIndex(x => new { x.OrderItemId, x.OptionId }).IsUnique(); } }
public sealed class OrderItemNoteConfiguration : IEntityTypeConfiguration<OrderItemNote> { public void Configure(EntityTypeBuilder<OrderItemNote> b) { b.ToTable("order_item_note", "ordering"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.OrderItemId, x.Position }).IsUnique(); } }
public sealed class OrderItemPizzaConfigurationConfiguration : IEntityTypeConfiguration<OrderItemPizzaConfiguration> { public void Configure(EntityTypeBuilder<OrderItemPizzaConfiguration> b) { b.ToTable("order_item_pizza_configuration", "ordering"); b.HasKey(x => x.OrderItemId); b.Property(x => x.SizeName).HasMaxLength(120); b.Property(x => x.DoughName).HasMaxLength(120); b.Property(x => x.CrustName).HasMaxLength(120); } }
public sealed class OrderItemPizzaFractionConfiguration : IEntityTypeConfiguration<OrderItemPizzaFraction> { public void Configure(EntityTypeBuilder<OrderItemPizzaFraction> b) { b.ToTable("order_item_pizza_fraction", "ordering"); b.HasKey(x => x.Id); b.Property(x => x.ReferenceAmount).HasPrecision(14, 2); b.Property(x => x.Configuration).HasColumnType("jsonb"); b.HasIndex(x => new { x.OrderItemId, x.Position }).IsUnique(); } }
public sealed class OrderItemComboSelectionConfiguration : IEntityTypeConfiguration<OrderItemComboSelection> { public void Configure(EntityTypeBuilder<OrderItemComboSelection> b) { b.ToTable("order_item_combo_selection", "ordering"); b.HasKey(x => x.Id); b.Property(x => x.ComponentAmount).HasPrecision(14, 2); b.Property(x => x.Configuration).HasColumnType("jsonb"); b.HasIndex(x => new { x.OrderItemId, x.ComboGroupId, x.ComboGroupItemId, x.SelectedProductId, x.SelectedVariantId }); } }
