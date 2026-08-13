using Appizza.Table.Core;

namespace Appizza.UnitTests;

public sealed class Phase3ConfigurationTests
{
    [Fact]
    public void SimpleProductSupportsRequiredRemovableAndAdditionalIngredients()
    {
        var required = new IngredientChoice(Guid.NewGuid(), true, false, false, 0, 0, 0);
        var additional = new IngredientChoice(Guid.NewGuid(), false, true, true, 2.505m, 2, 3);
        var estimate = LocalConfigurationRules.EstimateProduct(20m, [required, additional]);
        Assert.True(estimate.Valid); Assert.Equal(25.01m, estimate.Amount);
    }

    [Fact]
    public void RequiredIngredientCannotBeConfiguredAsRemovable()
    { var estimate = LocalConfigurationRules.EstimateProduct(10, [new(Guid.NewGuid(), true, true, false, 0, 0, 0)]); Assert.False(estimate.Valid); Assert.Contains("REQUIRED_INGREDIENT_CANNOT_BE_REMOVED", estimate.Errors); }

    [Fact]
    public void AdditionalIngredientHonorsMaximumQuantity()
    { var estimate = LocalConfigurationRules.EstimateProduct(10, [new(Guid.NewGuid(), false, true, true, 2, 3, 2)]); Assert.False(estimate.Valid); Assert.Contains("INGREDIENT_QUANTITY_OUT_OF_RANGE", estimate.Errors); }

    [Fact]
    public void MultiFlavorPizzaUsesEqualDivisionAndServerLimitWithoutPrematureRounding()
    { var result = LocalConfigurationRules.EstimatePizza(new(4, [33.33m, 33.34m, 33.34m], 2m, 3m, [])); Assert.True(result.Valid); Assert.Equal(38.34m, result.Amount); }

    [Fact]
    public void PizzaRejectsFlavorCountAboveContractLimit()
    { var result = LocalConfigurationRules.EstimatePizza(new(2, [10, 20, 30], 0, 0, [])); Assert.False(result.Valid); Assert.Contains("PIZZA_FLAVOR_LIMIT_EXCEEDED", result.Errors); }

    [Fact]
    public void CustomPizzaCombinesBaseFlavorsDoughCrustAndAdditionalIngredients()
    { var result = LocalConfigurationRules.EstimatePizza(new(4, [20], 2, 3, [new(Guid.NewGuid(), false, true, true, 1.25m, 2, 3)])); Assert.Equal(27.50m, result.Amount); }

    [Fact]
    public void ComboRejectsNestedComboAndAcceptsProductsAndVariants()
    { Assert.False(LocalConfigurationRules.EstimateCombo(40, ["simple", "combo"]).Valid); Assert.True(LocalConfigurationRules.EstimateCombo(40, ["simple", "pizza"]).Valid); }

    [Fact]
    public void AvailabilityChangeMarksExistingSelectionForReviewWithoutMutation()
    {
        var productId = Guid.NewGuid(); var configuration = $$"""{"productId":"{{productId}}","notes":"keep me"}"""; var overlay = $$"""{"products":[{"resourceId":"{{productId}}","effectiveAvailable":false,"reasonCode":"manual"}],"variants":[],"ingredients":[]}""";
        var result = PublishedMenuReader.ReconcileSelection(configuration, overlay);
        Assert.False(result.IsValid); Assert.Contains("keep me", configuration); Assert.Single(result.Messages);
    }
}
