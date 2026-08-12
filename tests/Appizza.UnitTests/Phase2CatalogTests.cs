using Appizza.Modules.Catalog;

namespace Appizza.UnitTests;

public sealed class Phase2CatalogTests
{
    [Fact]
    public void RequiredIngredientCannotBeRemovable()
    {
        Assert.False(CatalogRules.IsValidIngredientRule(new ProductIngredient
        {
            RequiredForRecipe = true,
            IncludedByDefault = true,
            CanBeRemoved = true,
        }));
    }

    [Fact]
    public void OptionalAndAdditionalIngredientRulesAreAccepted()
    {
        Assert.True(CatalogRules.IsValidIngredientRule(new ProductIngredient { CanBeRemoved = true }));
        Assert.True(CatalogRules.IsValidIngredientRule(new ProductIngredient { CanBeAdded = true, AdditionalPrice = 3.50m, MaximumAdditionalQuantity = 2 }));
    }

    [Theory]
    [InlineData(40, 60, 50)]
    [InlineData(30, 60, 45)]
    public void EqualFlavorFractionsUseArithmeticMean(decimal first, decimal second, decimal expected)
    {
        Assert.Equal(expected, PizzaPricing.EqualFractionBasePrice([first, second]));
    }

    [Fact]
    public void PizzaPricingRequiresAtLeastOneFlavor()
    {
        Assert.Throws<ArgumentException>(() => PizzaPricing.EqualFractionBasePrice([]));
    }
}
