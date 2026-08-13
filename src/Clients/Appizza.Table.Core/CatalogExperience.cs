using System.Text.Json;

namespace Appizza.Table.Core;

public sealed record MenuCategory(Guid Id, string Name, int DisplayOrder, IReadOnlyList<MenuProduct> Products);
public sealed record MenuProduct(Guid Id, string Type, string Name, string? Description, bool Available, string? UnavailableReason, decimal? StartingPrice, string ConfigurationVersion);
public sealed record MenuPresentation(long CatalogVersion, long AvailabilityVersion, IReadOnlyList<MenuCategory> Categories);
public sealed record SelectionAvailability(bool IsValid, IReadOnlyList<string> Messages);

public static class PublishedMenuReader
{
    public static MenuPresentation Read(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson); var root = document.RootElement;
        if (GetInt(root, "schemaVersion") != LocalContract.MenuSchemaVersion) throw new NotSupportedException("Unsupported menu schema.");
        var menu = Property(root, "menu"); var catalog = Property(root, "catalog"); var availability = Property(root, "availability");
        var productAvailability = AvailabilityMap(availability, "products"); var variantAvailability = AvailabilityMap(availability, "variants");
        var products = Elements(catalog, "products").Select(product =>
        {
            var id = GetGuid(product, "id"); var variants = Elements(catalog, "variants").Where(x => GetGuid(x, "productId") == id).ToArray();
            var prices = variants.Select(x => GetDecimal(x, "basePrice")).ToArray();
            var available = !productAvailability.TryGetValue(id, out var item) || item.Available;
            if (available && variants.Length > 0) available = variants.Any(x => !variantAvailability.TryGetValue(GetGuid(x, "id"), out var variant) || variant.Available);
            var versions = Property(root, "configurationVersions"); var configurationVersion = TryProperty(versions, id.ToString(), out var hash) ? hash.GetString()! : throw new JsonException($"Missing configurationVersion for {id}.");
            return new { CategoryId = GetNullableGuid(product, "primaryCategoryId"), Product = new MenuProduct(id, GetString(product, "productType") ?? "simple", GetString(product, "name") ?? "Produto", GetString(product, "description"), available, item.Reason, prices.Length == 0 ? null : prices.Min(), configurationVersion) };
        }).ToArray();
        var categories = Elements(catalog, "categories").Select(category => new MenuCategory(GetGuid(category, "id"), GetString(category, "name") ?? "Categoria", GetInt(category, "displayOrder"), products.Where(x => x.CategoryId == GetGuid(category, "id")).Select(x => x.Product).ToArray())).Where(x => x.Products.Count > 0).OrderBy(x => x.DisplayOrder).ToList();
        var uncategorized = products.Where(x => x.CategoryId is null || categories.All(c => c.Id != x.CategoryId)).Select(x => x.Product).ToArray(); if (uncategorized.Length > 0) categories.Add(new(Guid.Empty, "Outros", int.MaxValue, uncategorized));
        return new(GetLong(menu, "catalogVersion"), GetLong(menu, "availabilityVersion"), categories);
    }

    public static SelectionAvailability ReconcileSelection(string configurationJson, string availabilityJson)
    {
        using var config = JsonDocument.Parse(configurationJson); using var availability = JsonDocument.Parse(availabilityJson); var messages = new List<string>();
        var root = availability.RootElement; var maps = new[] { AvailabilityMap(root, "products"), AvailabilityMap(root, "variants"), AvailabilityMap(root, "ingredients") };
        foreach (var property in new[] { "productId", "productVariantId", "ingredientId", "doughId", "crustId" })
            VisitIds(config.RootElement, property, id => { foreach (var map in maps) if (map.TryGetValue(id, out var state) && !state.Available) { messages.Add($"{property}:{id:N}:{state.Reason ?? "unavailable"}"); break; } });
        return new(messages.Count == 0, messages);
    }

    private static Dictionary<Guid, (bool Available, string? Reason)> AvailabilityMap(JsonElement root, string property) => Elements(root, property).ToDictionary(x => GetGuid(x, "resourceId"), x => (GetBool(x, "effectiveAvailable"), GetString(x, "reasonCode")));
    private static void VisitIds(JsonElement element, string propertyName, Action<Guid> visit) { if (element.ValueKind == JsonValueKind.Object) foreach (var property in element.EnumerateObject()) { if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String && Guid.TryParse(property.Value.GetString(), out var id)) visit(id); else VisitIds(property.Value, propertyName, visit); } else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) VisitIds(item, propertyName, visit); }
    private static JsonElement[] Elements(JsonElement root, string name) => TryProperty(root, name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(x => x.Clone()).ToArray() : [];
    private static JsonElement Property(JsonElement root, string name) => TryProperty(root, name, out var value) ? value : throw new JsonException($"Missing {name}.");
    private static bool TryProperty(JsonElement root, string name, out JsonElement value) { if (root.ValueKind == JsonValueKind.Object) foreach (var property in root.EnumerateObject()) if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; } value = default; return false; }
    private static string? GetString(JsonElement root, string name) => TryProperty(root, name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    private static Guid GetGuid(JsonElement root, string name) => Guid.Parse(GetString(root, name)!);
    private static Guid? GetNullableGuid(JsonElement root, string name) => Guid.TryParse(GetString(root, name), out var id) ? id : null;
    private static int GetInt(JsonElement root, string name) => TryProperty(root, name, out var value) ? value.GetInt32() : 0;
    private static long GetLong(JsonElement root, string name) => TryProperty(root, name, out var value) ? value.GetInt64() : 0;
    private static decimal GetDecimal(JsonElement root, string name) => TryProperty(root, name, out var value) ? value.GetDecimal() : 0;
    private static bool GetBool(JsonElement root, string name) => TryProperty(root, name, out var value) && value.GetBoolean();
}

public sealed record IngredientChoice(Guid IngredientId, bool Required, bool CanBeRemoved, bool CanBeAdded, decimal AdditionalPrice, decimal Quantity, decimal MaximumAdditionalQuantity);
public sealed record PizzaChoice(int MaximumFlavors, IReadOnlyList<decimal> FlavorPrices, decimal DoughPrice, decimal CrustPrice, IReadOnlyList<IngredientChoice> Ingredients);
public sealed record ConfigurationEstimate(bool Valid, decimal Amount, IReadOnlyList<string> Errors);

public static class LocalConfigurationRules
{
    public static ConfigurationEstimate EstimateProduct(decimal basePrice, IEnumerable<IngredientChoice> ingredients)
    {
        var errors = ValidateIngredients(ingredients); var additional = ingredients.Where(x => x.CanBeAdded && x.Quantity > 0).Sum(x => x.AdditionalPrice * x.Quantity);
        return new(errors.Count == 0, Money.Estimate(basePrice + additional), errors);
    }

    public static ConfigurationEstimate EstimatePizza(PizzaChoice choice)
    {
        var errors = ValidateIngredients(choice.Ingredients); if (choice.FlavorPrices.Count == 0) errors.Add("PIZZA_FLAVOR_REQUIRED"); if (choice.FlavorPrices.Count > choice.MaximumFlavors) errors.Add("PIZZA_FLAVOR_LIMIT_EXCEEDED");
        var basePrice = choice.FlavorPrices.Count == 0 ? 0 : choice.FlavorPrices.Sum() / choice.FlavorPrices.Count; var additions = choice.Ingredients.Where(x => x.CanBeAdded && x.Quantity > 0).Sum(x => x.AdditionalPrice * x.Quantity);
        return new(errors.Count == 0, Money.Estimate(basePrice + choice.DoughPrice + choice.CrustPrice + additions), errors);
    }

    public static ConfigurationEstimate EstimateCombo(decimal fixedPrice, IEnumerable<string> selectedProductTypes)
    { var errors = selectedProductTypes.Any(x => x.Equals("combo", StringComparison.OrdinalIgnoreCase)) ? new[] { "COMBO_NESTING_NOT_ALLOWED" } : []; return new(errors.Length == 0, Money.Estimate(fixedPrice), errors); }

    private static List<string> ValidateIngredients(IEnumerable<IngredientChoice> ingredients)
    { var errors = new List<string>(); foreach (var item in ingredients) { if (item.Required && item.CanBeRemoved) errors.Add("REQUIRED_INGREDIENT_CANNOT_BE_REMOVED"); if (item.Quantity < 0 || item.Quantity > item.MaximumAdditionalQuantity) errors.Add("INGREDIENT_QUANTITY_OUT_OF_RANGE"); } return errors; }
}
