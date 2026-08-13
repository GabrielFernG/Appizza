using System.Text.Json;
using Appizza.Api;

namespace Appizza.UnitTests;

public sealed class Phase4PricingTests
{
    private static readonly HashSet<Guid> NoIds = [];
    [Fact]
    public void ServerIgnoresEstimatedPriceAndUsesPublishedVariant()
    {
        var product = Guid.NewGuid(); var variant = Guid.NewGuid(); using var catalog = JsonDocument.Parse(JsonSerializer.Serialize(new { products = new[] { new { id = product, productType = "simple", name = "Suco", requiresProduction = false } }, variants = new[] { new { id = variant, productId = product, name = "500 ml", basePrice = 12.345m } }, productIngredients = Array.Empty<object>(), customization = new { groups = Array.Empty<object>(), options = Array.Empty<object>(), products = Array.Empty<object>() }, pizza = new { }, combos = new { } }));
        var config = PublishedConfiguration(catalog.RootElement, product); var request = new CartSimulationRequest(Guid.NewGuid(), Guid.NewGuid(), 1, 1, [new(Guid.NewGuid(), product, variant, 2, Appizza.Modules.Catalog.SemanticConfigurationHash.Compute(config), 0.01m, Empty())]);
        var result = Phase4Pricing.Simulate(catalog.RootElement, request, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        Assert.True(result.CanSubmit); Assert.True(result.RequiresReview); Assert.Equal(12.35m, result.Items[0].UnitAmount); Assert.Equal(24.70m, result.TotalAmount); Assert.Equal(0, result.DiscountAmount);
    }

    [Fact]
    public void EqualPizzaFractionsPreservePrecisionBeforeAwayFromZeroRounding()
    { Assert.Equal(33.34m, Phase4Pricing.Round((33.33m + 33.34m) / 2m)); Assert.Equal(1.01m, Phase4Pricing.Round(1.005m)); }

    [Fact]
    public void UnavailablePublishedProductCannotBeSubmitted()
    {
        var product = Guid.NewGuid(); var variant = Guid.NewGuid(); using var catalog = JsonDocument.Parse(JsonSerializer.Serialize(new { products = new[] { new { id = product, productType = "simple", name = "Suco" } }, variants = new[] { new { id = variant, productId = product, basePrice = 10m } }, productIngredients = Array.Empty<object>(), customization = new { }, pizza = new { }, combos = new { } })); var config = PublishedConfiguration(catalog.RootElement, product); var request = new CartSimulationRequest(Guid.NewGuid(), Guid.NewGuid(), 1, 1, [new(Guid.NewGuid(), product, variant, 1, Appizza.Modules.Catalog.SemanticConfigurationHash.Compute(config), 10m, Empty())]);
        var result = Phase4Pricing.Simulate(catalog.RootElement, request, new HashSet<Guid> { product }, new HashSet<Guid>(), new HashSet<Guid>()); Assert.False(result.CanSubmit); Assert.Contains(result.Issues, x => x.ErrorCode == "PRODUCT_NOT_AVAILABLE");
    }

    [Theory]
    [InlineData("9.99")]
    [InlineData("10.01")]
    public void AnyAuthoritativePriceDifferenceRequiresReview(string estimate)
    {
        var product = Guid.NewGuid(); var variant = Guid.NewGuid(); using var catalog = JsonDocument.Parse(JsonSerializer.Serialize(new { products = new[] { new { id = product, productType = "simple", name = "Item" } }, variants = new[] { new { id = variant, productId = product, name = "V", basePrice = 10m } }, productIngredients = Array.Empty<object>(), customization = new { }, pizza = new { }, combos = new { } })); var config = PublishedConfiguration(catalog.RootElement, product); var request = new CartSimulationRequest(Guid.NewGuid(), Guid.NewGuid(), 1, 1, [new(Guid.NewGuid(), product, variant, 1, Appizza.Modules.Catalog.SemanticConfigurationHash.Compute(config), decimal.Parse(estimate, System.Globalization.CultureInfo.InvariantCulture), Empty())]); var result = Phase4Pricing.Simulate(catalog.RootElement, request, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()); Assert.True(result.CanSubmit); Assert.True(result.RequiresReview); Assert.Equal(10m, result.TotalAmount);
    }

    [Fact]
    public void RemovedOptionReducedIngredientLimitAndChangedVariantAreNeverSilentlySubstituted()
    {
        var product = Guid.NewGuid(); var variant = Guid.NewGuid(); var foreignVariant = Guid.NewGuid(); var ingredient = Guid.NewGuid(); var option = Guid.NewGuid(); using var catalog = JsonDocument.Parse(JsonSerializer.Serialize(new { products = new[] { new { id = product, productType = "simple", name = "Item" } }, variants = new[] { new { id = variant, productId = product, basePrice = 10m }, new { id = foreignVariant, productId = Guid.NewGuid(), basePrice = 20m } }, productIngredients = new[] { new { productId = product, ingredientId = ingredient, canBeAdded = true, maximumAdditionalQuantity = 1m, additionalPrice = 2m } }, customization = new { options = Array.Empty<object>() }, pizza = new { }, combos = new { } })); var version = "obsolete";
        JsonElement Config(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
        var removed = Phase4Pricing.Simulate(catalog.RootElement, new(Guid.NewGuid(), Guid.NewGuid(), 1, 1, [new(Guid.NewGuid(), product, variant, 1, version, 10m, Config($$"""{"selectedOptions":[{"optionId":"{{option}}","quantity":1}]}"""))]), NoIds, NoIds, NoIds); Assert.False(removed.CanSubmit); Assert.Contains(removed.Issues, x => x.ErrorCode == "PRODUCT_CONFIGURATION_CHANGED");
        var reduced = Phase4Pricing.Simulate(catalog.RootElement, new(Guid.NewGuid(), Guid.NewGuid(), 1, 1, [new(Guid.NewGuid(), product, variant, 1, version, 10m, Config($$"""{"ingredients":[{"ingredientId":"{{ingredient}}","action":"add","quantity":2}]}"""))]), NoIds, NoIds, NoIds); Assert.False(reduced.CanSubmit); Assert.Contains(reduced.Issues, x => x.ErrorCode == "INGREDIENT_QUANTITY_LIMIT_EXCEEDED");
        var changedVariant = Phase4Pricing.Simulate(catalog.RootElement, new(Guid.NewGuid(), Guid.NewGuid(), 1, 1, [new(Guid.NewGuid(), product, foreignVariant, 1, version, 10m, Empty())]), NoIds, NoIds, NoIds); Assert.False(changedVariant.CanSubmit); Assert.Contains(changedVariant.Issues, x => x.ErrorCode == "PRODUCT_CONFIGURATION_CHANGED");
    }

    private static JsonElement Empty() { using var document = JsonDocument.Parse("{}"); return document.RootElement.Clone(); }
    private static JsonElement PublishedConfiguration(JsonElement root, Guid productId) { using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { product = root.GetProperty("products")[0], variants = root.GetProperty("variants").EnumerateArray().Where(x => x.GetProperty("productId").GetGuid() == productId), productIngredients = Array.Empty<object>(), customization = root.GetProperty("customization"), pizza = root.GetProperty("pizza"), combos = root.GetProperty("combos") })); return document.RootElement.Clone(); }
}
