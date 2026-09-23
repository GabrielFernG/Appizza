using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appizza.Modules.Auditing;

public sealed class AuditEntry
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public string Action { get; set; } = null!;
    public string AggregateType { get; set; } = null!;
    public Guid AggregateId { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? ActorDeviceId { get; set; }
    public string? Reason { get; set; }
    public string? CorrelationId { get; set; }
    public string Outcome { get; set; } = "success";
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entry", "auditing", t => t.HasCheckConstraint("ck_audit_entry_actor", "not (actor_user_id is not null and actor_device_id is not null)"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(100);
        builder.Property(x => x.AggregateType).HasMaxLength(100);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.Property(x => x.Outcome).HasMaxLength(30);
        builder.HasIndex(x => new { x.EstablishmentId, x.AggregateType, x.AggregateId, x.OccurredAt });
    }
}
