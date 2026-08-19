using Appizza.BuildingBlocks;
using Appizza.Modules.Devices;
using Appizza.Modules.Establishments;
using Appizza.Modules.Identity;
using Appizza.Modules.Tables;
using Appizza.Modules.Catalog;
using Appizza.Modules.Media;
using Appizza.Modules.Ordering;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Promotions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text.RegularExpressions;

namespace Appizza.Persistence;

public sealed class AppizzaDbContext(DbContextOptions<AppizzaDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<Establishment> Establishments => Set<Establishment>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DiningTable> DiningTables => Set<DiningTable>();
    public DbSet<TableSession> TableSessions => Set<TableSession>();
    public DbSet<DeliveryConfirmation> DeliveryConfirmations => Set<DeliveryConfirmation>();
    public DbSet<DeliveryContest> DeliveryContests => Set<DeliveryContest>();
    public DbSet<Promotion> Promotions => Set<Promotion>();
    public DbSet<PromotionVersion> PromotionVersions => Set<PromotionVersion>();
    public DbSet<PromotionApplication> PromotionApplications => Set<PromotionApplication>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppizzaDbContext).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EstablishmentConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeviceConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DiningTableConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CategoryConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediaAssetConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PromotionConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CartSimulationConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StationConfiguration).Assembly);
        modelBuilder.HasSequence<long>("table_session_number_seq", "tables");
        modelBuilder.HasSequence<long>("order_number_seq", "ordering");
        modelBuilder.HasSequence<long>("production_queue_position_seq", "kitchen");
        ConfigureRelationships(modelBuilder);
        ApplySnakeCaseNames(modelBuilder);
    }

    private static void ConfigureRelationships(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Address>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<BusinessHour>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<EstablishmentSetting>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<User>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Role>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RolePermission>().HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<RolePermission>().HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<UserRole>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<UserRole>().HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<UserPermission>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<UserPermission>().HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<UserSession>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Device>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DeviceSession>().HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DeviceTableBinding>().HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DeviceHeartbeat>().HasOne<Device>().WithOne().HasForeignKey<DeviceHeartbeat>(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DeviceEvent>().HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Sector>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DiningTable>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DiningTable>().HasOne<Sector>().WithMany().HasForeignKey(x => x.SectorId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DeviceTableBinding>().HasOne<DiningTable>().WithMany().HasForeignKey(x => x.DiningTableId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TableSession>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TableSession>().HasOne<DiningTable>().WithMany().HasForeignKey(x => x.DiningTableId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SessionCustomerIdentification>().HasOne<TableSession>().WithMany().HasForeignKey(x => x.TableSessionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DeliveryConfirmation>().HasOne<ProductionItem>().WithMany().HasForeignKey(x => x.ProductionItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DeliveryContest>().HasOne<DeliveryConfirmation>().WithMany().HasForeignKey(x => x.DeliveryConfirmationId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DeliveryContest>().HasOne<ProductionItem>().WithMany().HasForeignKey(x => x.ProductionItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<TableSessionStatusHistory>().HasOne<TableSession>().WithMany().HasForeignKey(x => x.TableSessionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Category>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Category>().HasOne<Category>().WithMany().HasForeignKey(x => x.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Product>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Product>().HasOne<Category>().WithMany().HasForeignKey(x => x.PrimaryCategoryId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductCategory>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductCategory>().HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductVariant>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Ingredient>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<IngredientAttributeDefinition>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<IngredientAttribute>().HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<IngredientAttribute>().HasOne<IngredientAttributeDefinition>().WithMany().HasForeignKey(x => x.AttributeDefinitionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductIngredient>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductIngredient>().HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductVariantIngredientOverride>().HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductVariantIngredientOverride>().HasOne<ProductIngredient>().WithMany().HasForeignKey(x => x.ProductIngredientId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CustomizationGroup>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CustomizationOption>().HasOne<CustomizationGroup>().WithMany().HasForeignKey(x => x.CustomizationGroupId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CustomizationOption>().HasOne<Ingredient>().WithMany().HasForeignKey(x => x.LinkedIngredientId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CustomizationOption>().HasOne<Product>().WithMany().HasForeignKey(x => x.LinkedProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductCustomizationGroup>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductCustomizationGroup>().HasOne<CustomizationGroup>().WithMany().HasForeignKey(x => x.CustomizationGroupId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductCustomizationVariantRule>().HasOne<ProductCustomizationGroup>().WithMany().HasForeignKey(x => x.ProductCustomizationGroupId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductCustomizationVariantRule>().HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PizzaSize>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PizzaFlavor>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PizzaFlavorPrice>().HasOne<PizzaFlavor>().WithMany().HasForeignKey(x => x.PizzaFlavorId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PizzaFlavorPrice>().HasOne<PizzaSize>().WithMany().HasForeignKey(x => x.PizzaSizeId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Dough>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DoughSizePrice>().HasOne<Dough>().WithMany().HasForeignKey(x => x.DoughId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<DoughSizePrice>().HasOne<PizzaSize>().WithMany().HasForeignKey(x => x.PizzaSizeId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Crust>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CrustSizePrice>().HasOne<Crust>().WithMany().HasForeignKey(x => x.CrustId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CrustSizePrice>().HasOne<PizzaSize>().WithMany().HasForeignKey(x => x.PizzaSizeId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PizzaProductSize>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PizzaProductSize>().HasOne<PizzaSize>().WithMany().HasForeignKey(x => x.PizzaSizeId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PizzaDough>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PizzaDough>().HasOne<Dough>().WithMany().HasForeignKey(x => x.DoughId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PizzaCrust>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PizzaCrust>().HasOne<Crust>().WithMany().HasForeignKey(x => x.CrustId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CustomPizzaBasePrice>().HasOne<Product>().WithMany().HasForeignKey(x => x.CustomPizzaProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CustomPizzaBasePrice>().HasOne<PizzaSize>().WithMany().HasForeignKey(x => x.PizzaSizeId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Combo>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ComboGroup>().HasOne<Combo>().WithMany().HasForeignKey(x => x.ComboId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ComboGroupItem>().HasOne<ComboGroup>().WithMany().HasForeignKey(x => x.ComboGroupId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ComboGroupItem>().HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ComboGroupItem>().HasOne<ProductVariant>().WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ComboGroupItem>().HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ComboItemRestriction>().HasOne<ComboGroupItem>().WithMany().HasForeignKey(x => x.ComboGroupItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CatalogState>().HasOne<Establishment>().WithOne().HasForeignKey<CatalogState>(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CatalogRevision>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CatalogState>().HasOne<CatalogRevision>().WithMany().HasForeignKey(x => x.CurrentPublishedRevisionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<IngredientAvailability>().HasOne<Ingredient>().WithOne().HasForeignKey<IngredientAvailability>(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductAvailability>().HasOne<Product>().WithOne().HasForeignKey<ProductAvailability>(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductVariantAvailability>().HasOne<ProductVariant>().WithOne().HasForeignKey<ProductVariantAvailability>(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MediaAsset>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Category>().HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.ImageMediaId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Product>().HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.PrimaryImageMediaId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductVariant>().HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.ImageMediaId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Ingredient>().HasOne<MediaAsset>().WithMany().HasForeignKey(x => x.ImageMediaId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CartSimulation>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CartSimulation>().HasOne<Device>().WithMany().HasForeignKey(x => x.SourceDeviceId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CartSimulation>().HasOne<TableSession>().WithMany().HasForeignKey(x => x.TableSessionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Order>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Order>().HasOne<TableSession>().WithMany().HasForeignKey(x => x.TableSessionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Order>().HasOne<Device>().WithMany().HasForeignKey(x => x.SourceDeviceId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItem>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItemIngredient>().HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItemOption>().HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItemNote>().HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItemPizzaConfiguration>().HasOne<OrderItem>().WithOne().HasForeignKey<OrderItemPizzaConfiguration>(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItemPizzaFraction>().HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItemComboSelection>().HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItemRequest>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItemRequest>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItemRequest>().HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItemRevision>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItemRevision>().HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderItemRevision>().HasOne<OrderItemRequest>().WithOne().HasForeignKey<OrderItemRevision>(x => x.SourceRequestId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderStatusHistory>().HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Station>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionItem>().HasOne<Establishment>().WithMany().HasForeignKey(x => x.EstablishmentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionItem>().HasOne<Station>().WithMany().HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionItem>().HasOne<OrderItem>().WithOne().HasForeignKey<ProductionItem>(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionStatusHistory>().HasOne<ProductionItem>().WithMany().HasForeignKey(x => x.ProductionItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionAttempt>().HasOne<ProductionItem>().WithMany().HasForeignKey(x => x.ProductionItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionPause>().HasOne<ProductionItem>().WithMany().HasForeignKey(x => x.ProductionItemId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionPause>().HasOne<ProductionAttempt>().WithMany().HasForeignKey(x => x.ProductionAttemptId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            foreach (var property in entity.GetProperties()) property.SetColumnName(ToSnakeCase(property.GetColumnName(table)!));
            foreach (var index in entity.GetIndexes()) index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
        }
    }

    private static string ToSnakeCase(string value) => Regex.Replace(value, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        IncrementVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        IncrementVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void IncrementVersions()
    {
        foreach (var entry in ChangeTracker.Entries<IVersionedEntity>())
        {
            if (entry.State == EntityState.Added && entry.Entity.Version < 1)
            {
                entry.Entity.Version = 1;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.Version++;
            }
        }
    }
}
