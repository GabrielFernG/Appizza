using Appizza.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appizza.Modules.Kitchen;

#pragma warning disable CA1725

public static class Phase4KitchenPermissions
{
    public static readonly string[] All = ["kitchen.queue.view", "kitchen.production.view", "kitchen.production.accept",
        "kitchen.production.start", "kitchen.production.pause", "kitchen.production.resume",
        "kitchen.production.fail", "kitchen.production.restart", "kitchen.production.ready", "kitchen.delivery.send", "kitchen.delivery.confirm", "kitchen.delivery.resolve"];
}

public sealed class Station : IVersionedEntity
{
    public Guid Id { get; set; } public Guid EstablishmentId { get; set; } public string Name { get; set; } = null!; public string StationType { get; set; } = "general"; public string Status { get; set; } = "active"; public bool IsDefault { get; set; } public int DisplayOrder { get; set; } public int? DefaultTargetMinutes { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; } public long Version { get; set; }
}
public sealed class ProductionItem : IVersionedEntity
{
    public Guid Id { get; set; } public Guid EstablishmentId { get; set; } public Guid OrderItemId { get; set; } public Guid StationId { get; set; } public string Status { get; set; } = "awaiting_acceptance"; public long QueuePosition { get; set; } public bool RequiresProduction { get; set; } public DateTimeOffset ReceivedAt { get; set; } public DateTimeOffset? AcceptedAt { get; set; } public Guid? AcceptedByUserId { get; set; } public DateTimeOffset? PreparationStartedAt { get; set; } public DateTimeOffset? ReadyAt { get; set; } public int CurrentAttemptNumber { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; } public long Version { get; set; }
}
public sealed class ProductionStatusHistory { public Guid Id { get; set; } public Guid ProductionItemId { get; set; } public string? PreviousStatus { get; set; } public string NewStatus { get; set; } = null!; public Guid? UserId { get; set; } public DateTimeOffset ChangedAt { get; set; } public Guid? CorrelationId { get; set; } }
public sealed class ProductionAttempt
{
    public Guid Id { get; set; } public Guid ProductionItemId { get; set; } public int AttemptNumber { get; set; } public string Status { get; set; } = "active"; public DateTimeOffset StartedAt { get; set; } public DateTimeOffset? FinishedAt { get; set; } public string? FailureReasonCode { get; set; } public string? FailureDescription { get; set; } public Guid CreatedByUserId { get; set; } public DateTimeOffset CreatedAt { get; set; }
}
public sealed class ProductionPause
{
    public Guid Id { get; set; } public Guid ProductionItemId { get; set; } public Guid ProductionAttemptId { get; set; } public string ReasonCode { get; set; } = null!; public string? Description { get; set; } public DateTimeOffset PausedAt { get; set; } public DateTimeOffset? ResumedAt { get; set; } public Guid PausedByUserId { get; set; } public Guid? ResumedByUserId { get; set; }
}

public sealed class DeliveryConfirmation
{
    public Guid Id { get; set; } public Guid EstablishmentId { get; set; } public Guid ProductionItemId { get; set; }
    public int SequenceNumber { get; set; } public string Status { get; set; } = "pending"; public long Version { get; set; }
    public DateTimeOffset RequestedAt { get; set; } public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; } public string? ConfirmationSource { get; set; }
    public Guid? ConfirmedByUserId { get; set; } public Guid? ConfirmedByDeviceId { get; set; }
    public DateTimeOffset? ContestedAt { get; set; } public DateTimeOffset? SupersededAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DeliveryContest
{
    public Guid Id { get; set; } public Guid EstablishmentId { get; set; } public Guid DeliveryConfirmationId { get; set; }
    public Guid ProductionItemId { get; set; } public string Status { get; set; } = "open"; public long Version { get; set; }
    public DateTimeOffset OpenedAt { get; set; } public string? Resolution { get; set; } public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? OpenedByUserId { get; set; } public Guid? OpenedByDeviceId { get; set; } public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionNote { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class StationConfiguration : IEntityTypeConfiguration<Station> { public void Configure(EntityTypeBuilder<Station> b) { b.ToTable("station", "kitchen", t => t.HasCheckConstraint("ck_station_status", "status in ('active','inactive')")); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(120); b.Property(x => x.StationType).HasMaxLength(50); b.Property(x => x.Status).HasMaxLength(30); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.EstablishmentId, x.Name }).IsUnique(); b.HasIndex(x => x.EstablishmentId).IsUnique().HasFilter("is_default and status = 'active'"); } }
public sealed class ProductionItemConfiguration : IEntityTypeConfiguration<ProductionItem> { public void Configure(EntityTypeBuilder<ProductionItem> b) { b.ToTable("production_item", "kitchen", t => t.HasCheckConstraint("ck_production_item_status", "status in ('awaiting_acceptance','accepted','awaiting_preparation','in_preparation','paused','ready','awaiting_delivery_confirmation','delivered','cancelled')")); b.HasKey(x => x.Id); b.Property(x => x.QueuePosition).HasDefaultValueSql("nextval('kitchen.production_queue_position_seq')"); b.Property(x => x.Status).HasMaxLength(50); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => x.OrderItemId).IsUnique(); b.HasIndex(x => new { x.EstablishmentId, x.StationId, x.Status, x.QueuePosition }); } }
public sealed class ProductionStatusHistoryConfiguration : IEntityTypeConfiguration<ProductionStatusHistory> { public void Configure(EntityTypeBuilder<ProductionStatusHistory> b) { b.ToTable("production_status_history", "kitchen"); b.HasKey(x => x.Id); b.Property(x => x.PreviousStatus).HasMaxLength(50); b.Property(x => x.NewStatus).HasMaxLength(50); b.HasIndex(x => new { x.ProductionItemId, x.ChangedAt }); } }
public sealed class ProductionAttemptConfiguration : IEntityTypeConfiguration<ProductionAttempt> { public void Configure(EntityTypeBuilder<ProductionAttempt> b) { b.ToTable("production_attempt", "kitchen", t => { t.HasCheckConstraint("ck_production_attempt_status", "status in ('active','completed','failed','abandoned')"); t.HasCheckConstraint("ck_production_attempt_number", "attempt_number > 0"); }); b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(40); b.Property(x => x.FailureReasonCode).HasMaxLength(80); b.Property(x => x.FailureDescription).HasMaxLength(1000); b.HasIndex(x => new { x.ProductionItemId, x.AttemptNumber }).IsUnique(); b.HasIndex(x => x.ProductionItemId).IsUnique().HasFilter("status = 'active'"); } }
public sealed class ProductionPauseConfiguration : IEntityTypeConfiguration<ProductionPause> { public void Configure(EntityTypeBuilder<ProductionPause> b) { b.ToTable("production_pause", "kitchen"); b.HasKey(x => x.Id); b.Property(x => x.ReasonCode).HasMaxLength(80); b.Property(x => x.Description).HasMaxLength(1000); b.HasIndex(x => x.ProductionItemId).IsUnique().HasFilter("resumed_at is null"); } }
public sealed class DeliveryConfirmationConfiguration : IEntityTypeConfiguration<DeliveryConfirmation> { public void Configure(EntityTypeBuilder<DeliveryConfirmation> b) { b.ToTable("delivery_confirmation", "kitchen", t => { t.HasCheckConstraint("ck_delivery_confirmation_sequence", "sequence_number > 0"); t.HasCheckConstraint("ck_delivery_confirmation_status", "status in ('pending','confirmed_manual','confirmed_automatic','contested','superseded')"); t.HasCheckConstraint("ck_delivery_confirmation_actor", "not (confirmed_by_user_id is not null and confirmed_by_device_id is not null)"); }); b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(30); b.Property(x => x.ConfirmationSource).HasMaxLength(30); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => new { x.ProductionItemId, x.SequenceNumber }).IsUnique(); b.HasIndex(x => new { x.EstablishmentId, x.ProductionItemId, x.Status }); b.HasIndex(x => new { x.Status, x.ExpiresAt }); b.HasIndex(x => x.ProductionItemId).IsUnique().HasFilter("status in ('pending','contested')"); } }
public sealed class DeliveryContestConfiguration : IEntityTypeConfiguration<DeliveryContest> { public void Configure(EntityTypeBuilder<DeliveryContest> b) { b.ToTable("delivery_contest", "kitchen", t => { t.HasCheckConstraint("ck_delivery_contest_status", "status in ('open','resolved_delivered','resolved_retry')"); t.HasCheckConstraint("ck_delivery_contest_resolution", "(status = 'open' and resolution is null and resolved_at is null) or (status <> 'open' and resolution in ('confirm_delivered','retry_delivery') and resolved_at is not null)"); t.HasCheckConstraint("ck_delivery_contest_actor", "not (opened_by_user_id is not null and opened_by_device_id is not null)"); }); b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(30); b.Property(x => x.Resolution).HasMaxLength(30); b.Property(x => x.ResolutionNote).HasMaxLength(1000); b.Property(x => x.Version).IsConcurrencyToken(); b.HasIndex(x => x.DeliveryConfirmationId); b.HasIndex(x => new { x.ProductionItemId, x.Status }); b.HasIndex(x => x.ProductionItemId).IsUnique().HasFilter("status = 'open'"); } }

public static class ProductionLifecycle
{
    public static bool CanStart(string status, bool hasActiveAttempt) => status == "awaiting_preparation" && !hasActiveAttempt;
    public static bool CanPause(string status, bool hasActiveAttempt, bool hasOpenPause) => status == "in_preparation" && hasActiveAttempt && !hasOpenPause;
    public static bool CanResume(string status, bool hasActiveAttempt, bool hasOpenPause) => status == "paused" && hasActiveAttempt && hasOpenPause;
    public static bool CanFail(string status, bool hasActiveAttempt) => status == "in_preparation" && hasActiveAttempt;
    public static bool CanRestart(string status, bool hasActiveAttempt, bool hasOpenPause, string? lastAttemptStatus) => status == "paused" && !hasActiveAttempt && !hasOpenPause && lastAttemptStatus == "failed";
    public static bool CanReady(string status, bool hasActiveAttempt, bool hasOpenPause) => status == "in_preparation" && hasActiveAttempt && !hasOpenPause;
    public static TimeSpan EffectiveDuration(DateTimeOffset startedAt, DateTimeOffset endAt, IEnumerable<(DateTimeOffset Start, DateTimeOffset? End)> pauses) => endAt - startedAt - TimeSpan.FromTicks(pauses.Sum(x => ((x.End ?? endAt) - x.Start).Ticks));
}
