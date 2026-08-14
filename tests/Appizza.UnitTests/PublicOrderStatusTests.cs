using Appizza.Modules.Kitchen;

namespace Appizza.UnitTests;

public sealed class PublicOrderStatusTests
{
    [Theory]
    [InlineData(null, "received", "pending_kitchen_intake")]
    [InlineData("awaiting_acceptance", "received", "awaiting_kitchen_acceptance")]
    [InlineData("accepted", "received", "awaiting_preparation")]
    [InlineData("awaiting_preparation", "received", "awaiting_preparation")]
    [InlineData("in_preparation", "preparing", "preparing")]
    [InlineData("paused", "preparing", "paused")]
    [InlineData("ready", "ready", "ready")]
    [InlineData("awaiting_delivery_confirmation", "on_the_way", "awaiting_delivery_confirmation")]
    [InlineData("delivered", "delivered", "delivered")]
    public void ItemMapsCommercialAndOperationalStates(string? production, string expectedStatus, string expectedSubstatus)
    {
        var result = PublicOrderStatusCalculator.Item(new("submitted", production, production is not null, production == "in_preparation", production == "paused"));
        Assert.Equal(expectedStatus, result.Status); Assert.Equal(expectedSubstatus, result.Substatus);
    }

    [Fact]
    public void CommercialCancellationWinsAndAttentionExplainsFutureExceptionalStates()
    {
        var cancelled = PublicOrderStatusCalculator.Item(new("cancelled", "in_preparation"));
        Assert.Equal("cancelled", cancelled.Status);
        var attention = PublicOrderStatusCalculator.Item(new("submitted", "paused", HasPendingRequest: true, HasOpenDeliveryContest: true, HasUncompensatedProductionRejection: true, PauseRequiresAction: true));
        Assert.Equal("preparing", attention.Status); Assert.Equal("attention_required", attention.Substatus);
        Assert.Equal(4, attention.AttentionReasons.Count);
    }

    [Fact]
    public void OrderUsesLeastAdvancedActiveStageAndIsPermutationInvariant()
    {
        PublicOrderItemStatus[] items =
        [
            new("ready", "ready", []), new("received", "awaiting_preparation", []),
            new("preparing", "paused", []), new("cancelled", "cancelled", [])
        ];
        var expected = PublicOrderStatusCalculator.Order(items);
        Assert.Equal("received", expected.Status); Assert.Equal("partially_cancelled", expected.Substatus);
        foreach (var permutation in Permutations(items)) Assert.Equal(expected, PublicOrderStatusCalculator.Order(permutation));
    }

    [Fact]
    public void OrderHandlesHomogeneousCancelledDeliveredAndAttentionPrecedence()
    {
        Assert.Equal("cancelled", PublicOrderStatusCalculator.Order([new("cancelled", "cancelled", []), new("cancelled", "cancelled", [])]).Status);
        Assert.Equal("delivered", PublicOrderStatusCalculator.Order([new("delivered", "delivered", []), new("delivered", "delivered", [])]).Status);
        var mixed = PublicOrderStatusCalculator.Order([new("cancelled", "cancelled", []), new("ready", "attention_required", ["request"])]);
        Assert.Equal("ready", mixed.Status); Assert.Equal("attention_required", mixed.Substatus); Assert.Equal(["request"], mixed.AttentionReasons);
    }

    private static IEnumerable<PublicOrderItemStatus[]> Permutations(PublicOrderItemStatus[] values)
    {
        if (values.Length == 1) { yield return values; yield break; }
        for (var i = 0; i < values.Length; i++)
        foreach (var tail in Permutations(values.Where((_, index) => index != i).ToArray()))
            yield return [values[i], .. tail];
    }
}
