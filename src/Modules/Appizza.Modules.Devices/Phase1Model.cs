using Appizza.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appizza.Modules.Devices;

#pragma warning disable CA1725 // Compact mapping declarations use a conventional short builder name.

public sealed class Device : IVersionedEntity
{
    public Guid Id { get; set; }
    public Guid? EstablishmentId { get; set; }
    public Guid InstallationId { get; set; }
    public string Name { get; set; } = null!;
    public string DeviceType { get; set; } = "table";
    public string Platform { get; set; } = null!;
    public string? Model { get; set; }
    public string? OperatingSystemVersion { get; set; }
    public string AppVersion { get; set; } = null!;
    public string Status { get; set; } = "awaiting_configuration";
    public string? CredentialHash { get; set; }
    public int CredentialVersion { get; set; } = 1;
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? BlockedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class DeviceTableBinding : IVersionedEntity
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Guid DiningTableId { get; set; }
    public DateTimeOffset BoundAt { get; set; }
    public DateTimeOffset? UnboundAt { get; set; }
    public Guid BoundByUserId { get; set; }
    public Guid? UnboundByUserId { get; set; }
    public string? UnbindReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class DeviceSession
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string RefreshTokenHash { get; set; } = null!;
    public int CredentialVersion { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedBySessionId { get; set; }
}

public sealed class DeviceHeartbeat
{
    public Guid DeviceId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public int? BatteryPercentage { get; set; }
    public string? NetworkStatus { get; set; }
    public long? StorageAvailableBytes { get; set; }
    public bool? KioskModeActive { get; set; }
    public string? SyncStatus { get; set; }
    public DateTimeOffset? LastCatalogSyncAt { get; set; }
}

public sealed class DeviceEvent
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string EventType { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public string? Details { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> b)
    {
        b.ToTable("device", "devices", t => { t.HasCheckConstraint("ck_device_status", "status in ('awaiting_configuration','active','revoked','blocked')"); t.HasCheckConstraint("ck_device_establishment", "(status = 'awaiting_configuration' and establishment_id is null) or (status <> 'awaiting_configuration' and establishment_id is not null)"); });
        b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(160); b.Property(x => x.DeviceType).HasMaxLength(40); b.Property(x => x.Platform).HasMaxLength(40); b.Property(x => x.Model).HasMaxLength(120); b.Property(x => x.OperatingSystemVersion).HasMaxLength(80); b.Property(x => x.AppVersion).HasMaxLength(40); b.Property(x => x.Status).HasMaxLength(40); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => x.InstallationId).IsUnique(); b.HasIndex(x => new { x.EstablishmentId, x.Status }); b.HasIndex(x => x.LastSeenAt);
    }
}

public sealed class DeviceTableBindingConfiguration : IEntityTypeConfiguration<DeviceTableBinding>
{
    public void Configure(EntityTypeBuilder<DeviceTableBinding> b) { b.ToTable("device_table_binding", "devices"); b.HasKey(x => x.Id); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => x.DeviceId).IsUnique().HasFilter("unbound_at is null"); b.HasIndex(x => new { x.DiningTableId, x.UnboundAt }); }
}

public sealed class DeviceSessionConfiguration : IEntityTypeConfiguration<DeviceSession>
{
    public void Configure(EntityTypeBuilder<DeviceSession> b) { b.ToTable("device_session", "devices"); b.HasKey(x => x.Id); b.Property(x => x.RefreshTokenHash).HasMaxLength(160); b.HasIndex(x => x.RefreshTokenHash).IsUnique(); b.HasIndex(x => new { x.DeviceId, x.RevokedAt }); b.HasIndex(x => x.ExpiresAt); }
}

public sealed class DeviceHeartbeatConfiguration : IEntityTypeConfiguration<DeviceHeartbeat>
{
    public void Configure(EntityTypeBuilder<DeviceHeartbeat> b) { b.ToTable("device_heartbeat", "devices", t => t.HasCheckConstraint("ck_device_heartbeat_battery", "battery_percentage is null or battery_percentage between 0 and 100")); b.HasKey(x => x.DeviceId); b.Property(x => x.NetworkStatus).HasMaxLength(30); b.Property(x => x.SyncStatus).HasMaxLength(40); }
}

public sealed class DeviceEventConfiguration : IEntityTypeConfiguration<DeviceEvent>
{
    public void Configure(EntityTypeBuilder<DeviceEvent> b) { b.ToTable("device_event", "devices"); b.HasKey(x => x.Id); b.Property(x => x.EventType).HasMaxLength(80); b.Property(x => x.Severity).HasMaxLength(30); b.Property(x => x.Details).HasColumnType("jsonb"); b.HasIndex(x => new { x.DeviceId, x.OccurredAt }); }
}
