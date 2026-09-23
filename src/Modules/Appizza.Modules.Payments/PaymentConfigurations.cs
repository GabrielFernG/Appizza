using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Appizza.Modules.Payments;

internal static class PaymentMapping
{
    public static void Money<T>(EntityTypeBuilder<T> b, string property) where T : class => b.Property<decimal>(property).HasPrecision(14, 2);
}

public sealed class PaymentPlanConfiguration : IEntityTypeConfiguration<PaymentPlan>
{
    private static string ToStorage(PaymentPlanMode value) => value switch
    {
        PaymentPlanMode.Total => "total",
        PaymentPlanMode.EqualSplit => "equal_split",
        PaymentPlanMode.ByParticipant => "by_participant",
        PaymentPlanMode.ByItem => "by_item",
        PaymentPlanMode.CustomAmount => "custom_amount",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    private static PaymentPlanMode FromStorage(string value) => value switch
    {
        "total" => PaymentPlanMode.Total,
        "equal_split" => PaymentPlanMode.EqualSplit,
        "by_participant" => PaymentPlanMode.ByParticipant,
        "by_item" => PaymentPlanMode.ByItem,
        "custom_amount" => PaymentPlanMode.CustomAmount,
        _ => throw new InvalidOperationException($"Unsupported payment plan mode '{value}'.")
    };

    public void Configure(EntityTypeBuilder<PaymentPlan> builder)
    {
        var b = builder;
        b.ToTable("payment_plan", "payments", t => t.HasCheckConstraint("ck_payment_plan_mode", "mode in ('total','equal_split','by_participant','by_item','custom_amount')"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Mode)
            .HasConversion(
                value => ToStorage(value),
                value => FromStorage(value))
            .HasMaxLength(30);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.EstablishmentId, x.TableSessionId, x.Version });
        b.HasIndex(x => new { x.EstablishmentId, x.TableSessionId, x.LogicalPlanId, x.Version })
            .IsUnique()
            .HasDatabaseName("ux_payment_plan_logical_version");
        b.HasIndex(x => new { x.EstablishmentId, x.TableSessionId, x.Id }).IsUnique();
    }
}

public sealed class PaymentPlanAllocationConfiguration : IEntityTypeConfiguration<PaymentPlanAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentPlanAllocation> builder)
    {
        var b = builder;
        b.ToTable("payment_plan_allocation", "payments", t => t.HasCheckConstraint("ck_payment_plan_allocation_amount", "amount >= 0"));
        b.HasKey(x => x.Id); PaymentMapping.Money(b, nameof(PaymentPlanAllocation.Amount));
        b.HasIndex(x => new { x.PaymentPlanId, x.StableOrder }).IsUnique();
        b.HasIndex(x => x.EstablishmentId);
        b.HasOne<PaymentPlan>().WithMany(x => x.Allocations).HasForeignKey(x => x.PaymentPlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    private static string MethodToDb(PaymentMethod value) => value switch { PaymentMethod.Pix => "pix", PaymentMethod.Cash => "cash", PaymentMethod.Credit => "credit", PaymentMethod.Debit => "debit", PaymentMethod.SoftPos => "soft_pos", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static PaymentMethod MethodFromDb(string value) => value switch { "pix" => PaymentMethod.Pix, "cash" => PaymentMethod.Cash, "credit" => PaymentMethod.Credit, "debit" => PaymentMethod.Debit, "soft_pos" => PaymentMethod.SoftPos, _ => throw new InvalidOperationException($"Unsupported payment method '{value}'.") };
    private static string StatusToDb(PaymentAttemptStatus value) => value switch { PaymentAttemptStatus.Created => "created", PaymentAttemptStatus.AwaitingCustomerAction => "awaiting_customer_action", PaymentAttemptStatus.Processing => "processing", PaymentAttemptStatus.Approved => "approved", PaymentAttemptStatus.Declined => "declined", PaymentAttemptStatus.Expired => "expired", PaymentAttemptStatus.Cancelled => "cancelled", PaymentAttemptStatus.Unknown => "unknown", _ => throw new ArgumentOutOfRangeException(nameof(value)) };
    private static PaymentAttemptStatus StatusFromDb(string value) => value switch { "created" => PaymentAttemptStatus.Created, "awaiting_customer_action" => PaymentAttemptStatus.AwaitingCustomerAction, "processing" => PaymentAttemptStatus.Processing, "approved" => PaymentAttemptStatus.Approved, "declined" => PaymentAttemptStatus.Declined, "expired" => PaymentAttemptStatus.Expired, "cancelled" => PaymentAttemptStatus.Cancelled, "unknown" => PaymentAttemptStatus.Unknown, _ => throw new InvalidOperationException($"Unsupported payment attempt status '{value}'.") };
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        var b = builder;
        b.ToTable("payment_attempt", "payments", t =>
        {
            t.HasCheckConstraint("ck_payment_attempt_status", "status in ('created','awaiting_customer_action','processing','approved','declined','expired','cancelled','unknown')");
            t.HasCheckConstraint("ck_payment_attempt_method", "method in ('pix','cash','credit','debit','soft_pos')");
            t.HasCheckConstraint("ck_payment_attempt_amounts", "amount >= 0 and reserved_amount >= 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Method).HasConversion(v => MethodToDb(v), v => MethodFromDb(v)).HasMaxLength(30);
        b.Property(x => x.Status).HasConversion(v => StatusToDb(v), v => StatusFromDb(v)).HasMaxLength(40);
        PaymentMapping.Money(b, nameof(PaymentAttempt.Amount)); PaymentMapping.Money(b, nameof(PaymentAttempt.ReservedAmount));
        b.Property(x => x.Provider).HasMaxLength(80); b.Property(x => x.ProviderReference).HasMaxLength(200); b.Property(x => x.IdempotencyKey).HasMaxLength(200); b.Property(x => x.PlanSnapshot).HasColumnType("jsonb"); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.EstablishmentId, x.TableSessionId, x.Status }); b.HasIndex(x => new { x.Provider, x.ProviderReference }).IsUnique().HasFilter("provider_reference is not null");
        b.HasIndex(x => new { x.EstablishmentId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key is not null");
    }
}

public sealed class PaymentAttemptAllocationConfiguration : IEntityTypeConfiguration<PaymentAttemptAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAttemptAllocation> builder)
    {
        builder.ToTable("payment_attempt_allocation", "payments");
        builder.HasKey(x => x.Id);
        PaymentMapping.Money(builder, nameof(PaymentAttemptAllocation.Amount));
        builder.HasIndex(x => new { x.PaymentAttemptId, x.PaymentPlanAllocationId }).IsUnique();
        builder.HasOne(x => x.PaymentAttempt).WithMany(x => x.Allocations).HasForeignKey(x => x.PaymentAttemptId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PaymentPlanAllocation).WithMany().HasForeignKey(x => x.PaymentPlanAllocationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    private static string ToStorage(RefundStatus value) => value switch
    {
        RefundStatus.Created => "created", RefundStatus.Processing => "processing", RefundStatus.Completed => "completed",
        RefundStatus.Failed => "failed", RefundStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };
    private static RefundStatus FromStorage(string value) => value switch
    {
        "created" => RefundStatus.Created, "processing" => RefundStatus.Processing, "completed" => RefundStatus.Completed,
        "failed" => RefundStatus.Failed, "cancelled" => RefundStatus.Cancelled,
        _ => throw new InvalidOperationException($"Unsupported refund status '{value}'.")
    };
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        var b = builder;
        b.ToTable("refund", "payments", t => { t.HasCheckConstraint("ck_refund_status", "status in ('created','processing','completed','failed','cancelled')"); t.HasCheckConstraint("ck_refund_amount", "amount >= 0"); });
        b.HasKey(x => x.Id); b.Property(x => x.Status).HasConversion(v => ToStorage(v), v => FromStorage(v)).HasMaxLength(30); PaymentMapping.Money(b, nameof(Refund.Amount)); b.Property(x => x.ProviderReference).HasMaxLength(200); b.Property(x => x.IdempotencyKey).HasMaxLength(200); b.Property(x => x.Reason).HasMaxLength(1000); b.Property(x => x.Version).IsConcurrencyToken();
        b.HasIndex(x => new { x.EstablishmentId, x.PaymentAttemptId }); b.HasIndex(x => new { x.EstablishmentId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key is not null");
    }
}

public sealed class PaymentProviderExecutionConfiguration : IEntityTypeConfiguration<PaymentProviderExecution>
{
    private static string ToStorage(PaymentProviderExecutionStatus value) => value switch
    {
        PaymentProviderExecutionStatus.Pending => "pending",
        PaymentProviderExecutionStatus.Processing => "processing",
        PaymentProviderExecutionStatus.AwaitingCustomerAction => "awaiting_customer_action",
        PaymentProviderExecutionStatus.Unknown => "unknown",
        PaymentProviderExecutionStatus.OutcomeObserved => "outcome_observed",
        PaymentProviderExecutionStatus.Completed => "completed",
        PaymentProviderExecutionStatus.TerminalFailure => "terminal_failure",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };
    private static PaymentProviderExecutionStatus FromStorage(string value) => value switch
    {
        "pending" => PaymentProviderExecutionStatus.Pending,
        "processing" => PaymentProviderExecutionStatus.Processing,
        "awaiting_customer_action" => PaymentProviderExecutionStatus.AwaitingCustomerAction,
        "unknown" => PaymentProviderExecutionStatus.Unknown,
        "outcome_observed" => PaymentProviderExecutionStatus.OutcomeObserved,
        "completed" => PaymentProviderExecutionStatus.Completed,
        "terminal_failure" => PaymentProviderExecutionStatus.TerminalFailure,
        _ => throw new InvalidOperationException($"Unsupported provider execution status '{value}'.")
    };

    public void Configure(EntityTypeBuilder<PaymentProviderExecution> builder)
    {
        builder.ToTable("payment_provider_execution", "payments", t =>
            t.HasCheckConstraint("ck_payment_provider_execution_status", "status in ('pending','processing','awaiting_customer_action','unknown','outcome_observed','completed','terminal_failure')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Provider).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ProviderIdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasConversion(v => ToStorage(v), v => FromStorage(v)).HasMaxLength(40).IsRequired();
        builder.Property(x => x.NormalizedOutcome).HasMaxLength(40);
        builder.Property(x => x.ProviderReference).HasMaxLength(200);
        builder.Property(x => x.LastErrorClassification).HasMaxLength(80);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.EstablishmentId, x.PaymentAttemptId }).IsUnique();
        builder.HasIndex(x => new { x.EstablishmentId, x.Status, x.ClaimedUntil });
        builder.HasOne<PaymentAttempt>().WithMany().HasForeignKey(x => x.PaymentAttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RefundProviderExecutionConfiguration : IEntityTypeConfiguration<RefundProviderExecution>
{
    private static string ToStorage(RefundProviderExecutionStatus value) => value switch
    {
        RefundProviderExecutionStatus.Pending => "pending",
        RefundProviderExecutionStatus.Processing => "processing",
        RefundProviderExecutionStatus.Unknown => "unknown",
        RefundProviderExecutionStatus.OutcomeObserved => "outcome_observed",
        RefundProviderExecutionStatus.Completed => "completed",
        RefundProviderExecutionStatus.TerminalFailure => "terminal_failure",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };
    private static RefundProviderExecutionStatus FromStorage(string value) => value switch
    {
        "pending" => RefundProviderExecutionStatus.Pending,
        "processing" => RefundProviderExecutionStatus.Processing,
        "unknown" => RefundProviderExecutionStatus.Unknown,
        "outcome_observed" => RefundProviderExecutionStatus.OutcomeObserved,
        "completed" => RefundProviderExecutionStatus.Completed,
        "terminal_failure" => RefundProviderExecutionStatus.TerminalFailure,
        _ => throw new InvalidOperationException($"Unsupported refund provider execution status '{value}'.")
    };

    public void Configure(EntityTypeBuilder<RefundProviderExecution> builder)
    {
        builder.ToTable("refund_provider_execution", "payments", t =>
            t.HasCheckConstraint("ck_refund_provider_execution_status", "status in ('pending','processing','unknown','outcome_observed','completed','terminal_failure')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Provider).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ProviderIdempotencyKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasConversion(v => ToStorage(v), v => FromStorage(v)).HasMaxLength(40).IsRequired();
        builder.Property(x => x.NormalizedOutcome).HasMaxLength(40);
        builder.Property(x => x.ProviderReference).HasMaxLength(200);
        builder.Property(x => x.LastErrorClassification).HasMaxLength(80);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.EstablishmentId, x.RefundId }).IsUnique();
        builder.HasIndex(x => new { x.EstablishmentId, x.Provider, x.ProviderIdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.EstablishmentId, x.Status, x.ClaimedUntil });
        builder.HasOne<Refund>().WithOne().HasForeignKey<RefundProviderExecution>(x => x.RefundId).OnDelete(DeleteBehavior.Restrict);
    }
}
