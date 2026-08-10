using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appizza.Persistence;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_message", "integration");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.EstablishmentId).HasColumnName("establishment_id");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(180);
        builder.Property(x => x.SchemaVersion).HasColumnName("schema_version");
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at");
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        builder.Property(x => x.RetryCount).HasColumnName("retry_count");
        builder.Property(x => x.NextRetryAt).HasColumnName("next_retry_at");
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        builder.Property(x => x.CausationId).HasColumnName("causation_id");
        builder.HasIndex(x => new { x.ProcessedAt, x.NextRetryAt }).HasDatabaseName("ix_outbox_pending");
        builder.HasIndex(x => new { x.EventType, x.OccurredAt }).HasDatabaseName("ix_outbox_event_occurred");
    }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_message", "integration");
        builder.HasKey(x => new { x.EventId, x.ConsumerName });
        builder.Property(x => x.EventId).HasColumnName("event_id");
        builder.Property(x => x.ConsumerName).HasColumnName("consumer_name").HasMaxLength(160);
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        builder.Property(x => x.Result).HasColumnName("result").HasMaxLength(40);
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_record", "integration");
        builder.HasKey(x => new { x.IdempotencyKey, x.OperationType });
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(120);
        builder.Property(x => x.EstablishmentId).HasColumnName("establishment_id");
        builder.Property(x => x.OperationType).HasColumnName("operation_type").HasMaxLength(160);
        builder.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(160);
        builder.Property(x => x.ResponseStatus).HasColumnName("response_status");
        builder.Property(x => x.ResponsePayload).HasColumnName("response_payload").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
    }
}
