using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Appizza.Modules.Catalog;

namespace Appizza.Api;

public sealed record CartSimulationRequest(Guid SessionId, Guid LocalCartId, long CatalogVersion, long AvailabilityVersion, IReadOnlyList<CartIntentItem> Items);
public sealed record CartIntentItem(Guid LocalCartItemId, Guid ProductId, Guid? ProductVariantId, int Quantity, string ConfigurationVersion, decimal? EstimatedUnitAmount, JsonElement Configuration);
public sealed record SubmitOrderRequest(Guid SessionId, Guid LocalCartId, Guid ClientSubmissionId, Guid SimulationId, string SimulationVersion, bool AcceptedReview);
public sealed record AuthoritativeCartResult(IReadOnlyList<AuthoritativeCartItem> Items, decimal SubtotalAmount, decimal DiscountAmount, decimal TotalAmount, bool RequiresReview, bool CanSubmit, IReadOnlyList<CartIssue> Issues);
public sealed record AuthoritativeCartItem(Guid LocalCartItemId, Guid ProductId, Guid? ProductVariantId, string ProductType, string ProductName, string? VariantName, int Quantity, decimal UnitAmount, decimal TotalAmount, string ConfigurationVersion, Guid? PreparationStationId, bool RequiresProduction, JsonElement Configuration, JsonElement Snapshot);
public sealed record CartIssue(string ErrorCode, string Message, Guid? LocalCartItemId);

public static class Phase4Pricing
{
    public static AuthoritativeCartResult Simulate(JsonElement catalog, CartSimulationRequest request, ISet<Guid> unavailableProducts, ISet<Guid> unavailableVariants, ISet<Guid> unavailableIngredients)
    {
        var items = new List<AuthoritativeCartItem>(); var issues = new List<CartIssue>(); var review = false;
        if (request.Items.Count == 0) issues.Add(new("CART_EMPTY", "O carrinho está vazio.", null));
        foreach (var intent in request.Items)
        {
            if (intent.Quantity <= 0) { issues.Add(new("INVALID_QUANTITY", "Quantidade deve ser positiva.", intent.LocalCartItemId)); continue; }
            var product = Find(catalog, "products", intent.ProductId); if (product is null) { issues.Add(new("PRODUCT_CONFIGURATION_CHANGED", "Produto não pertence à revisão publicada.", intent.LocalCartItemId)); continue; }
            var type = Text(product.Value, "productType") ?? "simple"; var name = Text(product.Value, "name") ?? "Produto";
            if (unavailableProducts.Contains(intent.ProductId)) issues.Add(new("PRODUCT_NOT_AVAILABLE", $"{name} está indisponível.", intent.LocalCartItemId));
            var variant = intent.ProductVariantId is Guid variantId ? Find(catalog, "variants", variantId) : null;
            if (intent.ProductVariantId is not null && (variant is null || GuidValue(variant.Value, "productId") != intent.ProductId)) { issues.Add(new("PRODUCT_CONFIGURATION_CHANGED", "Variação inválida.", intent.LocalCartItemId)); continue; }
            if (intent.ProductVariantId is Guid selectedVariant && unavailableVariants.Contains(selectedVariant)) issues.Add(new("PRODUCT_NOT_AVAILABLE", "Variação indisponível.", intent.LocalCartItemId));
            var configuration = BuildConfiguration(catalog, product.Value, intent.ProductId); var version = SemanticConfigurationHash.Compute(configuration); if (!StringComparer.Ordinal.Equals(version, intent.ConfigurationVersion)) review = true;
            try
            {
                var unit = BasePrice(variant); unit += Options(catalog, intent, issues); unit += Ingredients(catalog, intent, unavailableIngredients, issues);
                if (type is "pizza" or "custom_pizza") unit += Pizza(catalog, intent, type, issues) - BasePrice(variant);
                else if (type == "combo") unit = Combo(catalog, intent, issues);
                unit = Round(unit); var total = Round(unit * intent.Quantity); if (intent.EstimatedUnitAmount is decimal estimated && Round(estimated) != unit) review = true;
                using var snap = JsonDocument.Parse(JsonSerializer.Serialize(new { snapshotSchemaVersion = 1, product = product.Value, variant, configuration = intent.Configuration, publishedConfiguration = HistoricalConfiguration(catalog, product.Value, intent.ProductId), pricing = new { currency = "BRL", rounding = "AwayFromZero", unitAmount = unit, quantity = intent.Quantity, totalAmount = total }, catalogConfigurationVersion = version }));
                items.Add(new(intent.LocalCartItemId, intent.ProductId, intent.ProductVariantId, type, name, variant is null ? null : Text(variant.Value, "name"), intent.Quantity, unit, total, version, GuidValue(product.Value, "preparationStationId"), Bool(product.Value, "requiresProduction"), intent.Configuration.Clone(), snap.RootElement.Clone()));
            }
            catch (PricingException ex) { issues.Add(new(ex.Code, ex.Message, intent.LocalCartItemId)); }
        }
        var subtotal = Round(items.Sum(x => x.TotalAmount)); return new(items, subtotal, 0, subtotal, review, issues.Count == 0, issues);
    }

    public static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    public static string MaterialHash(AuthoritativeCartResult result) => Hash(JsonSerializer.Serialize(new { items = result.Items.Select(x => new { x.LocalCartItemId, x.ProductId, x.ProductVariantId, x.ProductType, x.Quantity, x.UnitAmount, x.TotalAmount, x.ConfigurationVersion, configuration = x.Configuration }), result.SubtotalAmount, result.DiscountAmount, result.TotalAmount, result.RequiresReview, result.CanSubmit, result.Issues }));
    public static bool MateriallyEquivalent(AuthoritativeCartResult previous, AuthoritativeCartResult current) =>
        previous.CanSubmit == current.CanSubmit && previous.SubtotalAmount == current.SubtotalAmount && previous.DiscountAmount == current.DiscountAmount && previous.TotalAmount == current.TotalAmount &&
        previous.Items.Count == current.Items.Count && previous.Items.Zip(current.Items).All(pair => pair.First.LocalCartItemId == pair.Second.LocalCartItemId && pair.First.ProductId == pair.Second.ProductId && pair.First.ProductVariantId == pair.Second.ProductVariantId && pair.First.Quantity == pair.Second.Quantity && pair.First.UnitAmount == pair.Second.UnitAmount && pair.First.TotalAmount == pair.Second.TotalAmount && pair.First.ConfigurationVersion == pair.Second.ConfigurationVersion && pair.First.Configuration.GetRawText() == pair.Second.Configuration.GetRawText()) &&
        previous.Issues.Select(x => (x.ErrorCode, x.LocalCartItemId)).SequenceEqual(current.Issues.Select(x => (x.ErrorCode, x.LocalCartItemId)));
    public static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal Options(JsonElement root, CartIntentItem intent, List<CartIssue> issues)
    {
        if (!Property(intent.Configuration, "selectedOptions", out var selected)) return 0; decimal total = 0;
        foreach (var row in selected.EnumerateArray()) { var id = RequiredGuid(row, "optionId"); var quantity = Int(row, "quantity", 1); var option = NestedFind(root, "customization", "options", id) ?? throw new PricingException("PRODUCT_CONFIGURATION_CHANGED", "Opção não existe mais."); if (quantity <= 0) throw new PricingException("INVALID_QUANTITY", "Quantidade de opção inválida."); total += Decimal(option, "baseAdditionalPrice") * quantity; }
        return total;
    }

    private static decimal Ingredients(JsonElement root, CartIntentItem intent, ISet<Guid> unavailable, List<CartIssue> issues)
    {
        if (!Property(intent.Configuration, "ingredients", out var selected)) return 0; decimal total = 0;
        foreach (var row in selected.EnumerateArray())
        {
            var id = RequiredGuid(row, "ingredientId"); var action = Text(row, "action") ?? ""; var quantity = Decimal(row, "quantity", 1); var link = Elements(root, "productIngredients").FirstOrDefault(x => GuidValue(x, "productId") == intent.ProductId && GuidValue(x, "ingredientId") == id); if (link.ValueKind == JsonValueKind.Undefined) throw new PricingException("PRODUCT_CONFIGURATION_CHANGED", "Ingrediente não pertence ao produto.");
            if (action == "remove" && (Bool(link, "requiredForRecipe") || !Bool(link, "canBeRemoved"))) throw new PricingException("REQUIRED_INGREDIENT_CANNOT_BE_REMOVED", "Ingrediente obrigatório não pode ser removido.");
            if (action == "add") { if (!Bool(link, "canBeAdded")) throw new PricingException("INGREDIENT_CANNOT_BE_ADDED", "Ingrediente não pode ser adicionado."); var maximum = NullableDecimal(link, "maximumAdditionalQuantity"); if (quantity <= 0 || maximum is decimal max && quantity > max) throw new PricingException("INGREDIENT_QUANTITY_LIMIT_EXCEEDED", "Limite do adicional excedido."); if (unavailable.Contains(id)) throw new PricingException("PRODUCT_NOT_AVAILABLE", "Ingrediente adicional indisponível."); total += Decimal(link, "additionalPrice") * quantity; }
        }
        return total;
    }

    private static decimal Pizza(JsonElement root, CartIntentItem intent, string type, List<CartIssue> issues)
    {
        if (!Property(intent.Configuration, "pizza", out var pizza)) throw new PricingException("PIZZA_CONFIGURATION_REQUIRED", "Configuração da pizza é obrigatória."); var size = RequiredGuid(pizza, "sizeId"); if (!Property(pizza, "fractions", out var fractions) || fractions.GetArrayLength() == 0) throw new PricingException("PIZZA_FLAVORS_REQUIRED", "Selecione ao menos um sabor.");
        var allowed = NestedElements(root, "pizza", "products").FirstOrDefault(x => GuidValue(x, "productId") == intent.ProductId && GuidValue(x, "pizzaSizeId") == size); if (allowed.ValueKind == JsonValueKind.Undefined || !Bool(allowed, "available")) throw new PricingException("PRODUCT_CONFIGURATION_CHANGED", "Tamanho inválido."); var maximum = Int(allowed, "maximumFlavorCount", 4); if (fractions.GetArrayLength() > maximum) throw new PricingException("PIZZA_FLAVOR_LIMIT_EXCEEDED", "Limite de sabores excedido.");
        var references = new List<decimal>(); foreach (var fraction in fractions.EnumerateArray()) { if (GuidValue(fraction, "flavorId") is Guid flavor) { var price = NestedElements(root, "pizza", "flavorPrices").FirstOrDefault(x => GuidValue(x, "pizzaFlavorId") == flavor && GuidValue(x, "pizzaSizeId") == size); if (price.ValueKind == JsonValueKind.Undefined) throw new PricingException("PRODUCT_CONFIGURATION_CHANGED", "Preço do sabor não existe."); references.Add(Decimal(price, "price")); } else if (Property(fraction, "custom", out _)) { var price = NestedElements(root, "pizza", "customBasePrices").FirstOrDefault(x => GuidValue(x, "customPizzaProductId") == intent.ProductId && GuidValue(x, "pizzaSizeId") == size); if (price.ValueKind == JsonValueKind.Undefined) throw new PricingException("PRODUCT_CONFIGURATION_CHANGED", "Base do Monte sua Pizza não existe."); references.Add(Decimal(price, "basePrice")); } else throw new PricingException("PIZZA_FLAVOR_REQUIRED", "Fatia sem sabor."); }
        decimal total = references.Average(); if (GuidValue(pizza, "doughId") is Guid dough) { var row = NestedElements(root, "pizza", "doughPrices").FirstOrDefault(x => GuidValue(x, "doughId") == dough && GuidValue(x, "pizzaSizeId") == size); if (row.ValueKind == JsonValueKind.Undefined || !Bool(row, "available")) throw new PricingException("PRODUCT_CONFIGURATION_CHANGED", "Massa inválida."); total += Decimal(row, "additionalPrice"); } if (GuidValue(pizza, "crustId") is Guid crust) { var row = NestedElements(root, "pizza", "crustPrices").FirstOrDefault(x => GuidValue(x, "crustId") == crust && GuidValue(x, "pizzaSizeId") == size); if (row.ValueKind == JsonValueKind.Undefined || !Bool(row, "available")) throw new PricingException("PRODUCT_CONFIGURATION_CHANGED", "Borda inválida."); total += Decimal(row, "additionalPrice"); } return total;
    }

    private static decimal Combo(JsonElement root, CartIntentItem intent, List<CartIssue> issues)
    {
        var combo = NestedElements(root, "combos", "definitions").SingleOrDefault(x => GuidValue(x, "productId") == intent.ProductId); if (combo.ValueKind == JsonValueKind.Undefined) throw new PricingException("PRODUCT_CONFIGURATION_CHANGED", "Combo inválido."); if (!Property(intent.Configuration, "combo", out var config) || !Property(config, "groups", out var groups)) throw new PricingException("COMBO_CONFIGURATION_REQUIRED", "Seleções do combo são obrigatórias."); decimal components = 0, additions = 0;
        foreach (var groupIntent in groups.EnumerateArray())
        {
            var groupId = RequiredGuid(groupIntent, "groupId"); var group = NestedElements(root, "combos", "groups").SingleOrDefault(x => GuidValue(x, "id") == groupId && GuidValue(x, "comboId") == GuidValue(combo, "id")); if (group.ValueKind == JsonValueKind.Undefined) throw new PricingException("PRODUCT_CONFIGURATION_CHANGED", "Grupo do combo inválido."); if (!Property(groupIntent, "selections", out var selections)) throw new PricingException("COMBO_LIMIT_VIOLATION", "Seleção ausente."); var count = selections.EnumerateArray().Sum(x => Int(x, "quantity", 1)); if (count < Int(group, "minimumItems") || count > Int(group, "maximumItems")) throw new PricingException("COMBO_LIMIT_VIOLATION", "Quantidade do grupo inválida.");
            foreach (var selection in selections.EnumerateArray())
            {
                var row = NestedFind(root, "combos", "items", RequiredGuid(selection, "comboGroupItemId")) ?? throw new PricingException("PRODUCT_CONFIGURATION_CHANGED", "Item do combo inválido."); if (GuidValue(row, "comboGroupId") != groupId) throw new PricingException("PRODUCT_CONFIGURATION_CHANGED", "Item não pertence ao grupo."); var selectedProduct = GuidValue(row, "productId"); if (selectedProduct is Guid productId && Text(Find(root, "products", productId)!.Value, "productType") == "combo") throw new PricingException("NESTED_COMBO_NOT_ALLOWED", "Combo dentro de combo não é permitido."); var quantity = Int(selection, "quantity", 1); additions += Decimal(row, "additionalPrice") * quantity;
                var selectedVariant = GuidValue(row, "productVariantId"); JsonElement? variant = selectedVariant is Guid v ? Find(root, "variants", v) : selectedProduct is Guid p ? Elements(root, "variants").Where(x => GuidValue(x, "productId") == p).Cast<JsonElement?>().FirstOrDefault() : null; components += BasePrice(variant) * quantity;
            }
        }
        return (Text(combo, "pricingStrategy") ?? "fixed") switch { "fixed" or "fixed_price" => NullableDecimal(combo, "fixedPrice") ?? throw new PricingException("PRODUCT_CONFIGURATION_CHANGED", "Preço fixo ausente."), "calculated" => components, _ => throw new PricingException("PRODUCT_CONFIGURATION_CHANGED", "Estratégia do combo não suportada.") } + additions;
    }

    private static decimal BasePrice(JsonElement? variant) => variant is null ? 0 : Decimal(variant.Value, "basePrice");
    private static JsonElement BuildConfiguration(JsonElement root, JsonElement product, Guid id) { using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { product, variants = Elements(root, "variants").Where(x => GuidValue(x, "productId") == id), productIngredients = Elements(root, "productIngredients").Where(x => GuidValue(x, "productId") == id), customization = Property(root, "customization", out var customization) ? customization : default, pizza = Property(root, "pizza", out var pizza) ? pizza : default, combos = Property(root, "combos", out var combos) ? combos : default })); return document.RootElement.Clone(); }
    private static JsonElement HistoricalConfiguration(JsonElement root, JsonElement product, Guid id) { using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { product, variants = Elements(root, "variants").Where(x => GuidValue(x, "productId") == id), productIngredients = Elements(root, "productIngredients").Where(x => GuidValue(x, "productId") == id), ingredients = Elements(root, "ingredients"), products = Elements(root, "products"), allVariants = Elements(root, "variants"), customization = Property(root, "customization", out var customization) ? customization : default, pizza = Property(root, "pizza", out var pizza) ? pizza : default, combos = Property(root, "combos", out var combos) ? combos : default })); return document.RootElement.Clone(); }
    private static JsonElement? Find(JsonElement root, string collection, Guid id) => Elements(root, collection).Cast<JsonElement?>().FirstOrDefault(x => GuidValue(x!.Value, "id") == id);
    private static JsonElement? NestedFind(JsonElement root, string parent, string collection, Guid id) => NestedElements(root, parent, collection).Cast<JsonElement?>().FirstOrDefault(x => GuidValue(x!.Value, "id") == id);
    private static JsonElement[] Elements(JsonElement root, string name) => Property(root, name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(x => x.Clone()).ToArray() : [];
    private static JsonElement[] NestedElements(JsonElement root, string parent, string name) => Property(root, parent, out var nested) ? Elements(nested, name) : [];
    private static bool Property(JsonElement value, string name, out JsonElement result) { if (value.ValueKind == JsonValueKind.Object) foreach (var property in value.EnumerateObject()) if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { result = property.Value; return true; } result = default; return false; }
    private static string? Text(JsonElement value, string name) => Property(value, name, out var result) && result.ValueKind != JsonValueKind.Null ? result.GetString() : null;
    private static Guid? GuidValue(JsonElement value, string name) => Property(value, name, out var result) && result.ValueKind != JsonValueKind.Null && Guid.TryParse(result.GetString(), out var id) ? id : null;
    private static Guid RequiredGuid(JsonElement value, string name) => GuidValue(value, name) ?? throw new PricingException("PRODUCT_CONFIGURATION_CHANGED", $"{name} inválido.");
    private static bool Bool(JsonElement value, string name) => Property(value, name, out var result) && result.ValueKind == JsonValueKind.True;
    private static int Int(JsonElement value, string name, int fallback = 0) => Property(value, name, out var result) && result.TryGetInt32(out var number) ? number : fallback;
    private static decimal Decimal(JsonElement value, string name, decimal fallback = 0) => Property(value, name, out var result) && result.TryGetDecimal(out var number) ? number : fallback;
    private static decimal? NullableDecimal(JsonElement value, string name) => Property(value, name, out var result) && result.ValueKind != JsonValueKind.Null && result.TryGetDecimal(out var number) ? number : null;
    private sealed class PricingException(string code, string message) : Exception(message) { public string Code { get; } = code; }
}
