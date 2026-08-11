using Appizza.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appizza.Modules.Identity;

#pragma warning disable CA1725 // Compact mapping declarations use a conventional short builder name.
#pragma warning disable CA1711 // Permission is the documented domain terminology, not a CLR permission set.

public sealed class User : IVersionedEntity
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public string Name { get; set; } = null!;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Login { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string? PinHash { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset? LastLoginAt { get; set; }
    public int FailedPinAttempts { get; set; }
    public DateTimeOffset? PinLockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public long Version { get; set; }
}

public sealed class Role
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public sealed class Permission
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public string Module { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
}

public sealed class RolePermission
{
    public Guid Id { get; set; }
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public string? ScopeType { get; set; }
    public Guid? ScopeId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class UserRole
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

public sealed class UserPermission
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PermissionId { get; set; }
    public string Effect { get; set; } = null!;
    public string? ScopeType { get; set; }
    public Guid? ScopeId { get; set; }
    public DateTimeOffset? ValidFrom { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

public sealed class UserSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? DeviceId { get; set; }
    public string RefreshTokenHash { get; set; } = null!;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public Guid? ReplacedBySessionId { get; set; }
}

public static class Phase1Permissions
{
    public static readonly string[] All = ["establishments.view", "establishments.settings.manage", "identity.users.view", "identity.users.manage", "identity.roles.manage", "devices.view", "devices.table.configure", "devices.table.replace-active", "devices.configuration.revoke", "devices.block", "tables.view", "tables.manage", "tables.cleaning.confirm", "tables.session.view", "tables.session.force-close"];
}

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("user", "identity", t => t.HasCheckConstraint("ck_user_status", "status in ('active','blocked','inactive')")); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.Email).HasMaxLength(200); b.Property(x => x.Phone).HasMaxLength(40); b.Property(x => x.Login).HasMaxLength(120);
        b.Property(x => x.Status).HasMaxLength(30); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.EstablishmentId, x.Login }).IsUnique();
        b.HasIndex(x => new { x.EstablishmentId, x.Email }).IsUnique().HasFilter("email is not null"); b.HasIndex(x => new { x.EstablishmentId, x.Status });
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b) { b.ToTable("role", "identity", t => t.HasCheckConstraint("ck_role_status", "status in ('active','inactive')")); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120); b.Property(x => x.Status).HasMaxLength(30); b.HasIndex(x => new { x.EstablishmentId, x.Name }).IsUnique(); }
}

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b) { b.ToTable("permission", "identity"); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(180); b.Property(x => x.Module).HasMaxLength(80); b.Property(x => x.Name).HasMaxLength(160); b.HasIndex(x => x.Code).IsUnique(); }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("role_permission", "identity", t => t.HasCheckConstraint("ck_role_permission_scope", "(scope_type is null and scope_id is null) or (scope_type is not null and scope_id is not null)")); b.HasKey(x => x.Id);
        b.Property(x => x.ScopeType).HasMaxLength(40); b.HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique().HasFilter("scope_type is null and scope_id is null"); b.HasIndex(x => new { x.RoleId, x.PermissionId, x.ScopeType, x.ScopeId }).IsUnique().HasFilter("scope_type is not null and scope_id is not null");
    }
}

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b) { b.ToTable("user_role", "identity"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique().HasFilter("valid_from is null and valid_until is null"); b.HasIndex(x => new { x.UserId, x.RoleId, x.ValidFrom }).IsUnique().HasFilter("valid_from is not null"); }
}

public sealed class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> b) { b.ToTable("user_permission", "identity", t => { t.HasCheckConstraint("ck_user_permission_effect", "effect in ('allow','deny')"); t.HasCheckConstraint("ck_user_permission_scope", "(scope_type is null and scope_id is null) or (scope_type is not null and scope_id is not null)"); }); b.HasKey(x => x.Id); b.Property(x => x.Effect).HasMaxLength(10); b.Property(x => x.ScopeType).HasMaxLength(40); b.HasIndex(x => new { x.UserId, x.PermissionId, x.Effect }).HasFilter("scope_type is null and scope_id is null"); }
}

public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> b) { b.ToTable("user_session", "identity"); b.HasKey(x => x.Id); b.Property(x => x.RefreshTokenHash).HasMaxLength(160); b.HasIndex(x => x.RefreshTokenHash).IsUnique(); b.HasIndex(x => new { x.UserId, x.RevokedAt }); b.HasIndex(x => x.ExpiresAt); }
}
