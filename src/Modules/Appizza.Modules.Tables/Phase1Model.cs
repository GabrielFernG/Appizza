using Appizza.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appizza.Modules.Tables;

#pragma warning disable CA1725 // Compact mapping declarations use a conventional short builder name.

public sealed class Sector
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DiningTable : IVersionedEntity
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public Guid? SectorId { get; set; }
    public string Name { get; set; } = null!;
    public string? InternalCode { get; set; }
    public int? Capacity { get; set; }
    public string Status { get; set; } = "available";
    public int DisplayOrder { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public long Version { get; set; }
}

public sealed class TableSession : IVersionedEntity
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public Guid DiningTableId { get; set; }
    public string SessionNumber { get; set; } = null!;
    public string Status { get; set; } = "open";
    public string CustomerIdentificationStatus { get; set; } = "pending";
    public DateTimeOffset? CustomerIdentificationResolvedAt { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosingStartedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? OpenedByDeviceId { get; set; }
    public Guid? OpenedByUserId { get; set; }
    public int? GuestCount { get; set; }
    public decimal SubtotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal AdjustmentAmount { get; set; }
    public decimal ServiceChargeAmount { get; set; }
    public decimal CoverChargeAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal ReservedAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed class SessionCustomerIdentification
{
    public Guid Id { get; set; }
    public Guid TableSessionId { get; set; }
    public string IdentificationType { get; set; } = "cpf";
    public string? EncryptedValue { get; set; }
    public string? EncryptionNonce { get; set; }
    public string? EncryptionTag { get; set; }
    public string? ValueHash { get; set; }
    public string MaskedValue { get; set; } = null!;
    public string Purpose { get; set; } = null!;
    public DateTimeOffset CollectedAt { get; set; }
    public Guid? DeviceId { get; set; }
    public DateTimeOffset? RetentionUntil { get; set; }
    public DateTimeOffset? AnonymizedAt { get; set; }
}

public sealed class TableSessionStatusHistory
{
    public Guid Id { get; set; }
    public Guid TableSessionId { get; set; }
    public string? PreviousStatus { get; set; }
    public string NewStatus { get; set; } = null!;
    public Guid? ChangedByUserId { get; set; }
    public Guid? ChangedByDeviceId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public Guid? CorrelationId { get; set; }
}

public sealed class SectorConfiguration : IEntityTypeConfiguration<Sector>
{
    public void Configure(EntityTypeBuilder<Sector> b) { b.ToTable("sector", "tables", t => t.HasCheckConstraint("ck_sector_status", "status in ('active','inactive')")); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120); b.Property(x => x.Status).HasMaxLength(30); b.HasIndex(x => new { x.EstablishmentId, x.DisplayOrder }); }
}

public sealed class DiningTableConfiguration : IEntityTypeConfiguration<DiningTable>
{
    public void Configure(EntityTypeBuilder<DiningTable> b)
    {
        b.ToTable("dining_table", "tables", t => { t.HasCheckConstraint("ck_dining_table_status", "status in ('available','occupied','closing','awaiting_cleaning','blocked','inactive')"); t.HasCheckConstraint("ck_dining_table_capacity", "capacity is null or capacity > 0"); }); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120); b.Property(x => x.InternalCode).HasMaxLength(80); b.Property(x => x.Status).HasMaxLength(30); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.EstablishmentId, x.InternalCode }).IsUnique().HasFilter("internal_code is not null"); b.HasIndex(x => new { x.EstablishmentId, x.Status });
    }
}

public sealed class TableSessionConfiguration : IEntityTypeConfiguration<TableSession>
{
    public void Configure(EntityTypeBuilder<TableSession> b)
    {
        b.ToTable("table_session", "tables", t => { t.HasCheckConstraint("ck_table_session_status", "status in ('open','closing','awaiting_payment','partially_paid','paid','closed','suspended','cancelled')"); t.HasCheckConstraint("ck_table_session_identification", "customer_identification_status in ('pending','provided','skipped') and ((customer_identification_status = 'pending' and customer_identification_resolved_at is null) or (customer_identification_status <> 'pending' and customer_identification_resolved_at is not null))"); });
        b.HasKey(x => x.Id); b.Property(x => x.SessionNumber).HasMaxLength(80); b.Property(x => x.Status).HasMaxLength(40); b.Property(x => x.CustomerIdentificationStatus).HasMaxLength(20); b.Property(x => x.Version).IsConcurrencyToken();
        foreach (var property in new[] { nameof(TableSession.SubtotalAmount), nameof(TableSession.DiscountAmount), nameof(TableSession.AdjustmentAmount), nameof(TableSession.ServiceChargeAmount), nameof(TableSession.CoverChargeAmount), nameof(TableSession.TotalAmount), nameof(TableSession.PaidAmount), nameof(TableSession.ReservedAmount), nameof(TableSession.RemainingAmount) }) b.Property<decimal>(property).HasPrecision(14, 2);
        b.HasIndex(x => new { x.EstablishmentId, x.SessionNumber }).IsUnique(); b.HasIndex(x => x.DiningTableId).IsUnique().HasFilter("status in ('open','closing','awaiting_payment','partially_paid','paid','suspended')"); b.HasIndex(x => new { x.EstablishmentId, x.Status }); b.HasIndex(x => new { x.DiningTableId, x.Status });
    }
}

public sealed class SessionCustomerIdentificationConfiguration : IEntityTypeConfiguration<SessionCustomerIdentification>
{
    public void Configure(EntityTypeBuilder<SessionCustomerIdentification> b) { b.ToTable("session_customer_identification", "tables"); b.HasKey(x => x.Id); b.Property(x => x.IdentificationType).HasMaxLength(30); b.Property(x => x.ValueHash).HasMaxLength(160); b.Property(x => x.MaskedValue).HasMaxLength(80); b.Property(x => x.Purpose).HasMaxLength(160); b.HasIndex(x => new { x.TableSessionId, x.Purpose }).IsUnique(); b.HasIndex(x => new { x.RetentionUntil, x.AnonymizedAt }); }
}

public sealed class TableSessionStatusHistoryConfiguration : IEntityTypeConfiguration<TableSessionStatusHistory>
{
    public void Configure(EntityTypeBuilder<TableSessionStatusHistory> b) { b.ToTable("table_session_status_history", "tables"); b.HasKey(x => x.Id); b.Property(x => x.PreviousStatus).HasMaxLength(40); b.Property(x => x.NewStatus).HasMaxLength(40); b.HasIndex(x => new { x.TableSessionId, x.ChangedAt }); }
}
