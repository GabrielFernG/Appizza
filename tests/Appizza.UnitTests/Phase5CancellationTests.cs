using Appizza.Modules.Ordering;

namespace Appizza.UnitTests;

public sealed class Phase5CancellationTests
{
    [Theory]
    [InlineData(null, CancellationDisposition.Automatic)]
    [InlineData("awaiting_acceptance", CancellationDisposition.Automatic)]
    [InlineData("accepted", CancellationDisposition.Automatic)]
    [InlineData("awaiting_preparation", CancellationDisposition.Automatic)]
    [InlineData("in_preparation", CancellationDisposition.KitchenDecision)]
    [InlineData("paused", CancellationDisposition.KitchenDecision)]
    [InlineData("ready", CancellationDisposition.ManagerDecision)]
    [InlineData("awaiting_delivery_confirmation", CancellationDisposition.Forbidden)]
    [InlineData("delivered", CancellationDisposition.Forbidden)]
    [InlineData("rejected", CancellationDisposition.Forbidden)]
    [InlineData("cancelled", CancellationDisposition.Forbidden)]
    public void MatrixIsExplicit(string? status, CancellationDisposition expected) => Assert.Equal(expected, CancellationPolicy.For(status));

    [Theory]
    [InlineData(0, 2, "submitted")]
    [InlineData(1, 2, "partially_cancelled")]
    [InlineData(2, 2, "cancelled")]
    public void CommercialOrderStatusDependsOnlyOnCancelledItems(int cancelled, int total, string expected) => Assert.Equal(expected, CancellationPolicy.OrderStatus(cancelled, total));
}
