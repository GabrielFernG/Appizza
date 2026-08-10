namespace Appizza.Persistence;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid? EstablishmentId { get; set; }
    public required string EventType { get; set; }
    public int SchemaVersion { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string? ErrorMessage { get; set; }
    public Guid? CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
}

public sealed class InboxMessage
{
    public Guid EventId { get; set; }
    public required string ConsumerName { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
    public string? Result { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class IdempotencyRecord
{
    public required string IdempotencyKey { get; set; }
    public Guid? EstablishmentId { get; set; }
    public required string OperationType { get; set; }
    public required string RequestHash { get; set; }
    public int? ResponseStatus { get; set; }
    public string? ResponsePayload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
