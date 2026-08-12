using Appizza.Table.Core;

namespace Appizza.Table;

public sealed class CartPage : ContentPage
{
    private readonly VerticalStackLayout _layout = new() { Padding = 24, Spacing = 10 };
    public CartPage() { Title = "Carrinho"; Content = new ScrollView { Content = _layout }; }
    protected override async void OnAppearing() { base.OnAppearing(); _layout.Clear(); if (TableRuntime.Context is not LocalContext context || TableRuntime.Menu is not MenuPresentation menu) return; var cart = await TableRuntime.Database.GetOrCreateCartAsync(context, menu.CatalogVersion, menu.AvailabilityVersion, DateTime.UtcNow); var items = await TableRuntime.Database.GetCartItemsAsync(Guid.ParseExact(cart.Id, "N")); _layout.Add(new Label { Text = "Carrinho desta sessão", FontSize = 26, FontAttributes = FontAttributes.Bold }); foreach (var item in items) _layout.Add(new Label { Text = $"{item.ProductType} · {item.Quantity:N0} × R$ {item.EstimatedUnitAmount:N2} = R$ {item.EstimatedTotalAmount:N2} · {item.ValidationState}" }); _layout.Add(new Label { Text = "Estimativas locais. Envio de pedido e validação autoritativa não fazem parte desta fase.", FontAttributes = FontAttributes.Italic }); }
}
