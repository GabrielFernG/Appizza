using System.Text.Json;
using Appizza.Table.Core;

namespace Appizza.Table;

public sealed class ProductConfigurationPage : ContentPage
{
    private readonly VerticalStackLayout _layout = new() { Padding = 24, Spacing = 12 }; private readonly Stepper _quantity = new() { Minimum = 1, Maximum = 20, Value = 1 };
    public ProductConfigurationPage() { Title = "Configurar"; Content = new ScrollView { Content = _layout }; }
    protected override void OnAppearing() { base.OnAppearing(); _layout.Clear(); var product = TableRuntime.SelectedProduct; if (product is null) return; _layout.Add(new Label { Text = product.Name, FontSize = 28, FontAttributes = FontAttributes.Bold }); _layout.Add(new Label { Text = product.Description ?? "" }); _layout.Add(new Label { Text = Description(product.Type) }); _layout.Add(new Label { Text = "Quantidade" }); _layout.Add(_quantity); var add = new Button { Text = "Adicionar estimativa ao carrinho", IsEnabled = product.Available }; add.Clicked += OnAdd; _layout.Add(add); _layout.Add(new Label { Text = "Configuração e versões serão revalidadas online pelo servidor antes de qualquer pedido futuro.", FontAttributes = FontAttributes.Italic }); }
    private async void OnAdd(object? sender, EventArgs args) { var product = TableRuntime.SelectedProduct; var context = TableRuntime.Context; var menu = TableRuntime.Menu; if (product is null || context is null || menu is null) return; var cart = await TableRuntime.Database.GetOrCreateCartAsync(context, menu.CatalogVersion, menu.AvailabilityVersion, DateTime.UtcNow); var configuration = JsonSerializer.Serialize(new { productId = product.Id, type = product.Type, quantity = (decimal)_quantity.Value, selections = Array.Empty<object>() }); await TableRuntime.Database.UpsertCartItemAsync(cart, new(Guid.NewGuid(), product.Id, null, product.Type, (decimal)_quantity.Value, configuration, product.ConfigurationVersion, menu.CatalogVersion, menu.AvailabilityVersion, product.StartingPrice ?? 0), DateTime.UtcNow); await DisplayAlertAsync("Carrinho", "Item salvo localmente como estimativa.", "OK"); }
    private static string Description(string type) => type switch { "pizza" => "Escolha tamanho, sabores em divisões iguais, massa, borda, remoções e adicionais conforme o contrato.", "custom_pizza" => "Monte sua Pizza: escolha base, sabores ou partes do zero, massa, borda e ingredientes.", "combo" => "Selecione os grupos do combo. Combo dentro de combo não é aceito.", _ => "Escolha variante, ingredientes removíveis e adicionais dentro dos limites." };
}
