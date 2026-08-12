using Appizza.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appizza.Modules.Catalog;

#pragma warning disable CA1711 // IngredientAttribute is the documented domain entity name.
#pragma warning disable CA1725 // Compact mapping declarations consistently use a short builder name.

public static class CatalogRules
{
    public static readonly string[] AdministrativeStatuses = ["active", "inactive", "archived"];
    public static readonly string[] ProductTypes = ["simple", "configurable", "pizza", "custom_pizza", "combo"];
    public static bool IsValidIngredientRule(ProductIngredient value) =>
        !value.RequiredForRecipe || value.IncludedByDefault && !value.CanBeRemoved;
}

public abstract class CatalogEntity : IVersionedEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class Category : CatalogEntity
{
    public Guid EstablishmentId { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public Guid? ImageMediaId { get; set; }
    public int DisplayOrder { get; set; }
    public string Status { get; set; } = "active";
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public sealed class Product : CatalogEntity
{
    public Guid EstablishmentId { get; set; }
    public string ProductType { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? ShortName { get; set; }
    public string? Description { get; set; }
    public string? InternalCode { get; set; }
    public Guid? PrimaryCategoryId { get; set; }
    public Guid? PrimaryImageMediaId { get; set; }
    public string Status { get; set; } = "active";
    public int DisplayOrder { get; set; }
    public bool RequiresProduction { get; set; }
    public bool RequiresOperationalAcceptance { get; set; }
    public bool AllowsNotes { get; set; }
    public int? MaximumNoteLength { get; set; }
    public Guid? PreparationStationId { get; set; }
    public int? EstimatedPreparationMinutes { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public sealed class ProductCategory
{
    public Guid ProductId { get; set; }
    public Guid CategoryId { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class ProductVariant : CatalogEntity
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = null!;
    public string? ShortName { get; set; }
    public string? InternalCode { get; set; }
    public string? Barcode { get; set; }
    public decimal BasePrice { get; set; }
    public Guid? ImageMediaId { get; set; }
    public string Status { get; set; } = "active";
    public int DisplayOrder { get; set; }
    public int? EstimatedPreparationMinutes { get; set; }
    public bool StockControlEnabled { get; set; }
}

public sealed class Ingredient : CatalogEntity
{
    public Guid EstablishmentId { get; set; }
    public string Name { get; set; } = null!;
    public string? KitchenName { get; set; }
    public string? Description { get; set; }
    public decimal DefaultAdditionalPrice { get; set; }
    public string? UnitOfMeasure { get; set; }
    public bool StockControlEnabled { get; set; }
    public string Status { get; set; } = "active";
    public Guid? ImageMediaId { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public sealed class IngredientAttributeDefinition
{
    public Guid Id { get; set; }
    public Guid? EstablishmentId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string AttributeType { get; set; } = null!;
    public string Status { get; set; } = "active";
}

public sealed class IngredientAttribute
{
    public Guid IngredientId { get; set; }
    public Guid AttributeDefinitionId { get; set; }
    public bool? ValueBoolean { get; set; }
    public string? ValueText { get; set; }
}

public sealed class ProductIngredient : CatalogEntity
{
    public Guid ProductId { get; set; }
    public Guid IngredientId { get; set; }
    public bool IncludedByDefault { get; set; }
    public bool RequiredForRecipe { get; set; }
    public bool CanBeRemoved { get; set; }
    public bool CanBeAdded { get; set; }
    public decimal? DefaultQuantity { get; set; }
    public decimal? MaximumAdditionalQuantity { get; set; }
    public decimal AdditionalPrice { get; set; }
    public string ApplicationScope { get; set; } = "whole_product";
    public int DisplayOrder { get; set; }
}

public sealed class ProductVariantIngredientOverride : CatalogEntity
{
    public Guid ProductVariantId { get; set; }
    public Guid ProductIngredientId { get; set; }
    public bool? IncludedByDefaultOverride { get; set; }
    public bool? RequiredForRecipeOverride { get; set; }
    public bool? CanBeRemovedOverride { get; set; }
    public bool? CanBeAddedOverride { get; set; }
    public decimal? MaximumAdditionalQuantityOverride { get; set; }
    public decimal? AdditionalPriceOverride { get; set; }
    public bool Available { get; set; } = true;
}

public sealed class CustomizationGroup : CatalogEntity
{
    public Guid EstablishmentId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string SelectionType { get; set; } = null!;
    public int MinimumSelections { get; set; }
    public int? MaximumSelections { get; set; }
    public string DisplayType { get; set; } = null!;
    public bool Reusable { get; set; }
    public string Status { get; set; } = "active";
}

public sealed class CustomizationOption : CatalogEntity
{
    public Guid CustomizationGroupId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string PriceRuleType { get; set; } = "fixed";
    public decimal BaseAdditionalPrice { get; set; }
    public Guid? LinkedIngredientId { get; set; }
    public Guid? LinkedProductId { get; set; }
    public string Status { get; set; } = "active";
    public int DisplayOrder { get; set; }
}

public sealed class ProductCustomizationGroup : CatalogEntity
{
    public Guid ProductId { get; set; }
    public Guid CustomizationGroupId { get; set; }
    public bool? RequiredOverride { get; set; }
    public int? MinimumOverride { get; set; }
    public int? MaximumOverride { get; set; }
    public int DisplayOrder { get; set; }
    public string Status { get; set; } = "active";
}

public sealed class ProductCustomizationVariantRule : CatalogEntity
{
    public Guid ProductCustomizationGroupId { get; set; }
    public Guid ProductVariantId { get; set; }
    public int? MinimumSelections { get; set; }
    public int? MaximumSelections { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b) { b.ToTable("category", "catalog", t => t.HasCheckConstraint("ck_category_status", "status in ('active','inactive','archived')")); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.Status).HasMaxLength(30); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.EstablishmentId, x.ParentCategoryId, x.Status, x.DisplayOrder }); }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b) { b.ToTable("product", "catalog", t => { t.HasCheckConstraint("ck_product_status", "status in ('active','inactive','archived')"); t.HasCheckConstraint("ck_product_type", "product_type in ('simple','configurable','pizza','custom_pizza','combo')"); t.HasCheckConstraint("ck_product_note_length", "maximum_note_length is null or maximum_note_length > 0"); }); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(180); b.Property(x => x.ShortName).HasMaxLength(100); b.Property(x => x.InternalCode).HasMaxLength(80); b.Property(x => x.ProductType).HasMaxLength(40); b.Property(x => x.Status).HasMaxLength(30); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.EstablishmentId, x.InternalCode }).IsUnique().HasFilter("internal_code is not null"); b.HasIndex(x => new { x.EstablishmentId, x.Status, x.DisplayOrder }); }
}

public sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{ public void Configure(EntityTypeBuilder<ProductCategory> b) { b.ToTable("product_category", "catalog"); b.HasKey(x => new { x.ProductId, x.CategoryId }); } }

public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{ public void Configure(EntityTypeBuilder<ProductVariant> b) { b.ToTable("product_variant", "catalog", t => { t.HasCheckConstraint("ck_product_variant_status", "status in ('active','inactive','archived')"); t.HasCheckConstraint("ck_product_variant_price", "base_price >= 0"); }); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.InternalCode).HasMaxLength(80); b.Property(x => x.BasePrice).HasPrecision(14, 2); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.ProductId, x.InternalCode }).IsUnique().HasFilter("internal_code is not null"); b.HasIndex(x => new { x.ProductId, x.Status, x.DisplayOrder }); } }

public sealed class IngredientConfiguration : IEntityTypeConfiguration<Ingredient>
{ public void Configure(EntityTypeBuilder<Ingredient> b) { b.ToTable("ingredient", "catalog", t => { t.HasCheckConstraint("ck_ingredient_status", "status in ('active','inactive','archived')"); t.HasCheckConstraint("ck_ingredient_price", "default_additional_price >= 0"); }); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.DefaultAdditionalPrice).HasPrecision(14, 2); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.EstablishmentId, x.Status, x.Name }); } }

public sealed class IngredientAttributeDefinitionConfiguration : IEntityTypeConfiguration<IngredientAttributeDefinition>
{ public void Configure(EntityTypeBuilder<IngredientAttributeDefinition> b) { b.ToTable("ingredient_attribute_definition", "catalog", t => t.HasCheckConstraint("ck_ingredient_attribute_definition_status", "status in ('active','inactive','archived')")); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(100); b.HasIndex(x => new { x.EstablishmentId, x.Code }).IsUnique(); } }
public sealed class IngredientAttributeConfiguration : IEntityTypeConfiguration<IngredientAttribute>
{ public void Configure(EntityTypeBuilder<IngredientAttribute> b) { b.ToTable("ingredient_attribute", "catalog"); b.HasKey(x => new { x.IngredientId, x.AttributeDefinitionId }); } }

public sealed class ProductIngredientConfiguration : IEntityTypeConfiguration<ProductIngredient>
{ public void Configure(EntityTypeBuilder<ProductIngredient> b) { b.ToTable("product_ingredient", "catalog", t => { t.HasCheckConstraint("ck_product_ingredient_required", "not required_for_recipe or (included_by_default and not can_be_removed)"); t.HasCheckConstraint("ck_product_ingredient_quantity", "(default_quantity is null or default_quantity > 0) and (maximum_additional_quantity is null or maximum_additional_quantity > 0)"); t.HasCheckConstraint("ck_product_ingredient_price", "additional_price >= 0"); t.HasCheckConstraint("ck_product_ingredient_scope", "application_scope in ('whole_product','fraction','both')"); }); b.HasKey(x => x.Id); b.Property(x => x.DefaultQuantity).HasPrecision(12, 3); b.Property(x => x.MaximumAdditionalQuantity).HasPrecision(12, 3); b.Property(x => x.AdditionalPrice).HasPrecision(14, 2); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.ProductId, x.IngredientId }).IsUnique(); } }

public sealed class ProductVariantIngredientOverrideConfiguration : IEntityTypeConfiguration<ProductVariantIngredientOverride>
{ public void Configure(EntityTypeBuilder<ProductVariantIngredientOverride> b) { b.ToTable("product_variant_ingredient_override", "catalog"); b.HasKey(x => x.Id); b.Property(x => x.MaximumAdditionalQuantityOverride).HasPrecision(12, 3); b.Property(x => x.AdditionalPriceOverride).HasPrecision(14, 2); b.HasIndex(x => new { x.ProductVariantId, x.ProductIngredientId }).IsUnique(); } }

public sealed class CustomizationGroupConfiguration : IEntityTypeConfiguration<CustomizationGroup>
{ public void Configure(EntityTypeBuilder<CustomizationGroup> b) { b.ToTable("customization_group", "catalog", t => { t.HasCheckConstraint("ck_customization_group_status", "status in ('active','inactive','archived')"); t.HasCheckConstraint("ck_customization_group_selection", "selection_type in ('single','multiple','quantity')"); t.HasCheckConstraint("ck_customization_group_limits", "minimum_selections >= 0 and (maximum_selections is null or maximum_selections >= minimum_selections)"); }); b.HasKey(x => x.Id); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.EstablishmentId, x.Status, x.Name }); } }

public sealed class CustomizationOptionConfiguration : IEntityTypeConfiguration<CustomizationOption>
{ public void Configure(EntityTypeBuilder<CustomizationOption> b) { b.ToTable("customization_option", "catalog", t => { t.HasCheckConstraint("ck_customization_option_status", "status in ('active','inactive','archived')"); t.HasCheckConstraint("ck_customization_option_price", "base_additional_price >= 0"); t.HasCheckConstraint("ck_customization_option_link", "linked_ingredient_id is null or linked_product_id is null"); }); b.HasKey(x => x.Id); b.Property(x => x.BaseAdditionalPrice).HasPrecision(14, 2); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.CustomizationGroupId, x.Status, x.DisplayOrder }); } }

public sealed class ProductCustomizationGroupConfiguration : IEntityTypeConfiguration<ProductCustomizationGroup>
{ public void Configure(EntityTypeBuilder<ProductCustomizationGroup> b) { b.ToTable("product_customization_group", "catalog", t => t.HasCheckConstraint("ck_product_customization_group_limits", "(minimum_override is null or minimum_override >= 0) and (maximum_override is null or minimum_override is null or maximum_override >= minimum_override)")); b.HasKey(x => x.Id); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.ProductId, x.CustomizationGroupId }).IsUnique(); } }

public sealed class ProductCustomizationVariantRuleConfiguration : IEntityTypeConfiguration<ProductCustomizationVariantRule>
{ public void Configure(EntityTypeBuilder<ProductCustomizationVariantRule> b) { b.ToTable("product_customization_variant_rule", "catalog", t => t.HasCheckConstraint("ck_product_customization_variant_rule_limits", "(minimum_selections is null or minimum_selections >= 0) and (maximum_selections is null or minimum_selections is null or maximum_selections >= minimum_selections)")); b.HasKey(x => x.Id); b.HasIndex(x => new { x.ProductCustomizationGroupId, x.ProductVariantId }).IsUnique(); } }
