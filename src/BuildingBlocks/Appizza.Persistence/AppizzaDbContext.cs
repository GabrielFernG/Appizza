using Appizza.BuildingBlocks;
using Appizza.Modules.Devices;
using Appizza.Modules.Establishments;
using Appizza.Modules.Identity;
using Appizza.Modules.Tables;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppizzaDbContext).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EstablishmentConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UserConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeviceConfiguration).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DiningTableConfiguration).Assembly);
        modelBuilder.HasSequence<long>("table_session_number_seq", "tables");
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
        modelBuilder.Entity<TableSessionStatusHistory>().HasOne<TableSession>().WithMany().HasForeignKey(x => x.TableSessionId).OnDelete(DeleteBehavior.Restrict);
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
