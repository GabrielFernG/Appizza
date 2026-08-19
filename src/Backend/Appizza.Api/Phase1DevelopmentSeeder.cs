using Appizza.Modules.Establishments;
using Appizza.Modules.Catalog;
using Appizza.Modules.Identity;
using Appizza.Modules.Tables;
using Appizza.Modules.Kitchen;
using Appizza.Modules.Ordering;
using Appizza.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

public static class Phase1DevelopmentSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("DevelopmentSeed:Enabled")) return;
        var password = configuration["DevelopmentSeed:AdminPassword"] ?? throw new InvalidOperationException("DevelopmentSeed:AdminPassword is required when Development seed is enabled.");
        await using var scope = services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppizzaDbContext>(); var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var establishmentId = Guid.Parse("10000000-0000-0000-0000-000000000001"); var now = DateTimeOffset.UtcNow;
        if (!await db.Set<Establishment>().AnyAsync(x => x.Id == establishmentId, ct))
        {
            db.Add(new Establishment { Id = establishmentId, PublicCode = "APPIZZA-DEV", TradeName = "Appizza Development", Timezone = "America/Sao_Paulo", CurrencyCode = "BRL", CreatedAt = now, UpdatedAt = now });
            db.AddRange(
                new EstablishmentSetting { Id = Guid.NewGuid(), EstablishmentId = establishmentId, SettingKey = Phase1SettingKeys.MaximumTableDevices, SettingValue = "2", ValueType = "integer", UpdatedAt = now },
                new EstablishmentSetting { Id = Guid.NewGuid(), EstablishmentId = establishmentId, SettingKey = Phase1SettingKeys.SessionOpeningMode, SettingValue = "on_start_ordering", ValueType = "string", UpdatedAt = now },
                new EstablishmentSetting { Id = Guid.NewGuid(), EstablishmentId = establishmentId, SettingKey = Phase1SettingKeys.TableReleaseMode, SettingValue = "after_cleaning_confirmation", ValueType = "string", UpdatedAt = now },
                new EstablishmentSetting { Id = Guid.NewGuid(), EstablishmentId = establishmentId, SettingKey = Phase1SettingKeys.CpfRetentionDays, SettingValue = "30", ValueType = "integer", UpdatedAt = now });
            db.Add(new EstablishmentSetting { Id = Guid.NewGuid(), EstablishmentId = establishmentId, SettingKey = Phase4Ordering.SimulationValiditySetting, SettingValue = "300", ValueType = "integer", UpdatedAt = now });
            var sector = new Sector { Id = Guid.NewGuid(), EstablishmentId = establishmentId, Name = "Salão", DisplayOrder = 1, CreatedAt = now, UpdatedAt = now }; db.Add(sector);
            for (var number = 1; number <= 4; number++) db.Add(new DiningTable { Id = Guid.NewGuid(), EstablishmentId = establishmentId, SectorId = sector.Id, Name = $"Mesa {number:00}", InternalCode = $"M{number:00}", DisplayOrder = number, CreatedAt = now, UpdatedAt = now });
        }
        var permissions = new Dictionary<string, Permission>(StringComparer.Ordinal);
        var allPermissions = Phase1Permissions.All.Concat(Phase2Permissions.All).Concat(Phase4KitchenPermissions.All).Concat(Phase5CancellationPermissions.All).Concat(Appizza.Modules.Promotions.Phase6PromotionPermissions.All).ToArray();
        foreach (var code in allPermissions)
        {
            var permission = await db.Set<Permission>().SingleOrDefaultAsync(x => x.Code == code, ct) ?? new Permission { Id = Guid.NewGuid(), Code = code, Module = code.Split('.')[0], Name = code };
            if (db.Entry(permission).State == EntityState.Detached) db.Add(permission); permissions[code] = permission;
        }
        var roleNames = new[] { "Administrador", "Gerente", "Caixa", "Cozinha", "Garçom", "Atendente" }; var roles = new Dictionary<string, Role>();
        foreach (var name in roleNames) { var role = await db.Set<Role>().SingleOrDefaultAsync(x => x.EstablishmentId == establishmentId && x.Name == name, ct) ?? new Role { Id = Guid.NewGuid(), EstablishmentId = establishmentId, Name = name, IsSystemRole = true, CreatedAt = now, UpdatedAt = now }; if (db.Entry(role).State == EntityState.Detached) db.Add(role); roles[name] = role; }
        await db.SaveChangesAsync(ct);
        var mapping = new Dictionary<string, string[]> { ["Administrador"] = allPermissions, ["Gerente"] = allPermissions, ["Atendente"] = ["tables.view", "tables.session.view", "devices.view", "devices.table.configure", "catalog.read", "ordering.order_item_request.view", "ordering.order_item_request.decide"], ["Garçom"] = ["tables.view", "tables.session.view", "tables.cleaning.confirm", "catalog.read", "ordering.order_item_request.view"], ["Caixa"] = ["tables.view", "tables.session.view", "catalog.read", "catalog.availability.manage"], ["Cozinha"] = ["catalog.read", "catalog.availability.manage", "kitchen.order_item_request.decide", .. Phase4KitchenPermissions.All] };
        foreach (var (roleName, codes) in mapping) foreach (var code in codes) if (!await db.Set<RolePermission>().AnyAsync(x => x.RoleId == roles[roleName].Id && x.PermissionId == permissions[code].Id && x.ScopeType == null, ct)) db.Add(new RolePermission { Id = Guid.NewGuid(), RoleId = roles[roleName].Id, PermissionId = permissions[code].Id, CreatedAt = now });
        var admin = await db.Set<User>().SingleOrDefaultAsync(x => x.EstablishmentId == establishmentId && x.Login == "admin", ct);
        if (admin is null) { admin = new User { Id = Guid.NewGuid(), EstablishmentId = establishmentId, Name = "Administrador Development", Login = "admin", CreatedAt = now, UpdatedAt = now }; admin.PasswordHash = hasher.HashPassword(admin, password); db.Add(admin); db.Add(new UserRole { Id = Guid.NewGuid(), UserId = admin.Id, RoleId = roles["Administrador"].Id, CreatedAt = now }); }
        await db.SaveChangesAsync(ct);
        if (!await db.Set<Station>().AnyAsync(x => x.EstablishmentId == establishmentId && x.IsDefault && x.Status == "active", ct)) { db.Add(new Station { Id = Guid.Parse("40000000-0000-0000-0000-000000000001"), EstablishmentId = establishmentId, Name = "Cozinha Geral", IsDefault = true, DisplayOrder = 1, CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync(ct); }
    }
}
