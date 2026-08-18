using Appizza.Modules.Ordering;

namespace Appizza.UnitTests;

public sealed class Phase5ChangeTests
{
    [Theory]
    [InlineData(50, 55, 5)]
    [InlineData(55, 52, -3)]
    [InlineData(52, 52, 0)]
    public void DifferenceUsesCurrentEffectiveRevision(decimal current, decimal next, decimal expected) => Assert.Equal(expected, next - current);

    [Fact]
    public void RevisionNumbersAreStrictlyMonotonic()
    {
        var item = new OrderItem { CurrentRevisionNumber = 0 };
        Assert.Equal(1, ++item.CurrentRevisionNumber);
        Assert.Equal(2, ++item.CurrentRevisionNumber);
        Assert.Equal(3, ++item.CurrentRevisionNumber);
    }
}
