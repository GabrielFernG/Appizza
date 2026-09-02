using Appizza.BuildingBlocks;

namespace Appizza.Modules.Payments;

public static class PaymentPermissions
{
    public static readonly string[] All =
    [
        "payments.view", "payments.create", "payments.confirm_cash", "payments.reconcile",
        "payments.cancel", "payments.refund", "closing.start", "closing.cancel",
        "closing.view", "closing.finalize"
    ];
}

public enum PaymentPlanMode { Total, EqualSplit, ByParticipant, ByItem, CustomAmount }
public enum PaymentAttemptStatus { Created, AwaitingCustomerAction, Processing, Approved, Declined, Expired, Cancelled, Unknown }
public enum PaymentMethod { Pix, Cash, Credit, Debit, SoftPos }
public enum RefundStatus { Created, Processing, Completed, Failed, Cancelled }

public sealed class PaymentPlan : IVersionedEntity
{
    public Guid Id { get; set; }
    /// <summary>Stable identity shared by all immutable physical versions of this plan.</summary>
    public Guid LogicalPlanId { get; set; }
    public Guid EstablishmentId { get; set; }
    public Guid TableSessionId { get; set; }
    public long Version { get; set; }
    public PaymentPlanMode Mode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<PaymentPlanAllocation> Allocations { get; } = new List<PaymentPlanAllocation>();
}

public sealed class PaymentPlanAllocation
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public Guid PaymentPlanId { get; set; }
    public Guid? ParticipantId { get; set; }
    public Guid? OrderItemId { get; set; }
    public int StableOrder { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class PaymentAttempt : IVersionedEntity
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public Guid TableSessionId { get; set; }
    public Guid? PaymentPlanId { get; set; }
    public long? PaymentPlanVersion { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentAttemptStatus Status { get; set; } = PaymentAttemptStatus.Created;
    public decimal Amount { get; set; }
    public decimal ReservedAmount { get; set; }
    public string? Provider { get; set; }
    public string? ProviderReference { get; set; }
    public string? PlanSnapshot { get; set; }
    public string? IdempotencyKey { get; set; }
    public Guid? CorrelationId { get; set; }
    public Guid? CausationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public long Version { get; set; }
    public ICollection<PaymentAttemptAllocation> Allocations { get; } = new List<PaymentAttemptAllocation>();
}

public sealed class PaymentAttemptAllocation
{
    public Guid Id { get; set; }
    public Guid PaymentAttemptId { get; set; }
    public Guid PaymentPlanAllocationId { get; set; }
    public decimal Amount { get; set; }
    public PaymentAttempt PaymentAttempt { get; set; } = null!;
    public PaymentPlanAllocation PaymentPlanAllocation { get; set; } = null!;
}

public sealed class Refund : IVersionedEntity
{
    public Guid Id { get; set; }
    public Guid EstablishmentId { get; set; }
    public Guid PaymentAttemptId { get; set; }
    public decimal Amount { get; set; }
    public RefundStatus Status { get; set; } = RefundStatus.Created;
    public string? ProviderReference { get; set; }
    public string Reason { get; set; } = null!;
    public string? IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }
}

public sealed record PaymentProviderCapabilities(IReadOnlySet<string> Methods);
public sealed record StartPaymentRequest(Guid AttemptId, decimal Amount, PaymentMethod Method, string? ProviderReference = null);
public sealed record PaymentProviderStatus(string Status, string? ProviderReference = null);

public interface IPaymentProvider
{
    Task<PaymentProviderCapabilities> DiscoverCapabilitiesAsync(CancellationToken cancellationToken = default);
    Task<PaymentProviderStatus> StartPaymentAsync(StartPaymentRequest request, CancellationToken cancellationToken = default);
    Task<PaymentProviderStatus> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default);
    Task<PaymentProviderStatus> CancelPaymentAsync(string providerReference, CancellationToken cancellationToken = default);
    Task<PaymentProviderStatus> ReconcilePaymentAsync(string providerReference, CancellationToken cancellationToken = default);
}

/// <summary>Deterministic provider used only by development/test composition.</summary>
public sealed class FakePaymentProvider : IPaymentProvider
{
    public Task<PaymentProviderCapabilities> DiscoverCapabilitiesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new PaymentProviderCapabilities(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "pix", "credit", "debit" }));

    public Task<PaymentProviderStatus> StartPaymentAsync(StartPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Method == PaymentMethod.Cash) throw new InvalidOperationException("CASH_DOES_NOT_USE_PROVIDER");
        return Task.FromResult(new PaymentProviderStatus("processing", request.ProviderReference ?? $"fake:{request.AttemptId:N}"));
    }

    public Task<PaymentProviderStatus> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderStatus("unknown", providerReference));
    public Task<PaymentProviderStatus> CancelPaymentAsync(string providerReference, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderStatus("declined", providerReference));
    public Task<PaymentProviderStatus> ReconcilePaymentAsync(string providerReference, CancellationToken cancellationToken = default) => Task.FromResult(new PaymentProviderStatus("unknown", providerReference));
}

public static class PaymentAllocationCalculator
{
    public static IReadOnlyList<decimal> EqualSplit(decimal total, int parts)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parts);
        var cents = decimal.ToInt64(decimal.Round(total * 100m, 0, MidpointRounding.AwayFromZero));
        var baseCents = cents / parts;
        var remainder = cents % parts;
        return Enumerable.Range(0, parts)
            .Select(i => (baseCents + (i < remainder ? 1 : 0)) / 100m)
            .ToArray();
    }
}

public sealed record PaymentEventContract(
    Guid EventId, string EventType, int EventVersion, Guid AggregateId,
    long AggregateVersion, Guid EstablishmentId, Guid CorrelationId, Guid? CausationId);

public static class PaymentEventTypes
{
    public const string SessionClosingStarted = "SessionClosingStarted";
    public const string PaymentAttemptCreated = "PaymentAttemptCreated";
    public const string PaymentAwaitingCustomerAction = "PaymentAwaitingCustomerAction";
    public const string PaymentProcessing = "PaymentProcessing";
    public const string PaymentApproved = "PaymentApproved";
    public const string PaymentDeclined = "PaymentDeclined";
    public const string PaymentExpired = "PaymentExpired";
    public const string PaymentCancelled = "PaymentCancelled";
    public const string PaymentUnknown = "PaymentUnknown";
    public const string SessionPartiallyPaid = "SessionPartiallyPaid";
    public const string SessionPaid = "SessionPaid";
    public const string SessionClosed = "SessionClosed";
    public const string PaymentRefunded = "PaymentRefunded";
}
