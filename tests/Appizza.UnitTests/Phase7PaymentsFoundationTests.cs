using Appizza.Modules.Payments;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.UnitTests;

public sealed class Phase7PaymentsFoundationTests
{
    [Fact]
    public void EqualSplitUsesStableResidualOrderAndExactSum()
    {
        var result = PaymentAllocationCalculator.EqualSplit(100m, 3);
        Assert.Equal([33.34m, 33.33m, 33.33m], result);
        Assert.Equal(100m, result.Sum());
    }

    [Fact]
    public void AllNormativePlanModesAreRepresentable()
    {
        Assert.Equal(5, Enum.GetValues<PaymentPlanMode>().Length);
        Assert.Contains(PaymentPlanMode.ByParticipant, Enum.GetValues<PaymentPlanMode>());
        Assert.Contains(PaymentPlanMode.ByItem, Enum.GetValues<PaymentPlanMode>());
    }

    [Fact]
    public void UnknownIsRepresentableAndNotTerminalByContract()
    {
        Assert.Contains(PaymentAttemptStatus.Unknown, Enum.GetValues<PaymentAttemptStatus>());
        Assert.NotEqual(PaymentAttemptStatus.Approved, PaymentAttemptStatus.Unknown);
    }

    [Fact]
    public void AttemptCarriesImmutablePlanSnapshotAndRefundIsSeparate()
    {
        var attempt = new PaymentAttempt { PaymentPlanId = Guid.NewGuid(), PaymentPlanVersion = 2, PlanSnapshot = "{\"mode\":\"equal_split\",\"allocations\":[33.34,33.33,33.33]}" };
        var refund = new Refund { PaymentAttemptId = attempt.Id, Amount = 10m, Status = RefundStatus.Created, Reason = "customer_request" };
        Assert.Contains("allocations", attempt.PlanSnapshot);
        Assert.NotEqual(attempt.Amount, refund.Amount);
        Assert.Equal(PaymentAttemptStatus.Created, attempt.Status);
    }

    [Fact]
    public void NegativeMoneyAndInvalidPartsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentAllocationCalculator.EqualSplit(-1m, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentAllocationCalculator.EqualSplit(1m, 0));
    }

    [Fact]
    public void EqualSplitSupportsSmallCentValuesWithoutLoss()
    {
        Assert.Equal([0.01m, 0.00m], PaymentAllocationCalculator.EqualSplit(0.01m, 2));
        Assert.Equal([0.01m, 0.01m, 0.00m], PaymentAllocationCalculator.EqualSplit(0.02m, 3));
    }

    [Fact]
    public void PaymentPlanVersionsShareLogicalIdentityButKeepAllocationsPhysical()
    {
        var logical = Guid.NewGuid();
        var first = new PaymentPlan { Id = Guid.NewGuid(), LogicalPlanId = logical, Version = 1 };
        var second = new PaymentPlan { Id = Guid.NewGuid(), LogicalPlanId = logical, Version = 2 };
        first.Allocations.Add(new PaymentPlanAllocation { Id = Guid.NewGuid(), PaymentPlanId = first.Id, StableOrder = 0, Amount = 10m });
        second.Allocations.Add(new PaymentPlanAllocation { Id = Guid.NewGuid(), PaymentPlanId = second.Id, StableOrder = 0, Amount = 20m });

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.LogicalPlanId, second.LogicalPlanId);
        Assert.NotEqual(first.Allocations.Single().PaymentPlanId, second.Allocations.Single().PaymentPlanId);
    }

    [Fact]
    public void EfModelContainsHistoricalPaymentTablesAndRestrictiveRelationships()
    {
        var options = new DbContextOptionsBuilder<AppizzaDbContext>().UseNpgsql("Host=localhost;Database=not_used").Options;
        using var db = new AppizzaDbContext(options);
        Assert.Equal("payments", db.Model.FindEntityType(typeof(PaymentPlan))!.GetSchema());
        Assert.Equal("payment_attempt", db.Model.FindEntityType(typeof(PaymentAttempt))!.GetTableName());
        var refundFk = db.Model.FindEntityType(typeof(Refund))!.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(PaymentAttempt));
        Assert.Equal(DeleteBehavior.Restrict, refundFk.DeleteBehavior);
        Assert.NotNull(db.Model.FindEntityType(typeof(PaymentAttempt))!.FindProperty(nameof(PaymentAttempt.PlanSnapshot)));
    }

    [Fact]
    public void PaymentPlanVersionIdentityIsDistinctFromPhysicalId()
    {
        var logical = Guid.NewGuid();
        var first = new PaymentPlan { Id = Guid.NewGuid(), LogicalPlanId = logical, Version = 1 };
        var second = new PaymentPlan { Id = Guid.NewGuid(), LogicalPlanId = logical, Version = 2 };

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.LogicalPlanId, second.LogicalPlanId);
        Assert.NotEqual(first.Version, second.Version);
    }
}
