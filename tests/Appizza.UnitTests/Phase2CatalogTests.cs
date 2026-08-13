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

public sealed class Phase3PublishedMenuTests
{
    [Fact]
    public void SemanticHashIsDeterministicAndIgnoresTechnicalFields()
    {
        using var first = System.Text.Json.JsonDocument.Parse("""{"name":"Pizza","price":40.00,"updatedAt":"2026-01-01T00:00:00Z"}""");
        using var reordered = System.Text.Json.JsonDocument.Parse("""{"updatedAt":"2027-01-01T00:00:00Z","price":40,"name":"Pizza"}""");
        Assert.Equal(SemanticConfigurationHash.Compute(first.RootElement), SemanticConfigurationHash.Compute(reordered.RootElement));
    }

    [Fact]
    public void SemanticChangeChangesHash()
    {
        using var first = System.Text.Json.JsonDocument.Parse("""{"name":"Pizza","price":40}""");
        using var changed = System.Text.Json.JsonDocument.Parse("""{"name":"Pizza","price":41}""");
        Assert.NotEqual(SemanticConfigurationHash.Compute(first.RootElement), SemanticConfigurationHash.Compute(changed.RootElement));
    }
}
