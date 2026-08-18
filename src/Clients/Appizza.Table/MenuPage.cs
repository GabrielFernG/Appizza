using Appizza.Table.Core;

namespace Appizza.Table;

public sealed class MenuPage : ContentPage
{
    private readonly VerticalStackLayout _content = new() { Padding = 24, Spacing = 12 };
    private readonly Label _status = new() { FontAttributes = FontAttributes.Bold };
    public MenuPage() { Title = "Cardápio"; _content.Add(_status); Content = new ScrollView { Content = _content }; }
    protected override async void OnAppearing() { base.OnAppearing(); await LoadAsync(); }
    private async Task LoadAsync()
    {
        if (TableRuntime.Context is not LocalContext context) { _status.Text = "Sessão do tablet indisponível."; return; }
        await TableRuntime.Database.InitializeAsync(); var sync = new MenuSynchronizationService(TableRuntime.Database, new TableMenuApi(TableRuntime.Http), () => TableRuntime.IsOnline, () => DateTime.UtcNow); var result = await sync.InitializeAsync(context, CancellationToken.None);
        _status.Text = result.Status switch { SynchronizationStatus.StaleOffline => "Offline — estimativas do último catálogo.", SynchronizationStatus.Unavailable => "Cardápio indisponível. Verifique a conexão.", _ => "Online — valores exibidos são estimativas." };
        if (result.Catalog is null) { AddRetry(); return; } TableRuntime.Menu = PublishedMenuReader.Read(result.Catalog.PayloadJson); using (var document = System.Text.Json.JsonDocument.Parse(result.Catalog.PayloadJson)) await TableRuntime.Database.ReconcileActiveCartAsync(context, TableRuntime.Menu, document.RootElement.GetProperty("availability").GetRawText(), DateTime.UtcNow); await new MediaManifestSynchronizer(TableRuntime.Http, TableRuntime.MediaCache).SynchronizeAsync(context, result.Catalog.PayloadJson, CancellationToken.None); Render(TableRuntime.Menu);
    }
    private void Render(MenuPresentation menu)
    {
        while (_content.Count > 1) _content.RemoveAt(1);
        foreach (var category in menu.Categories) { _content.Add(new Label { Text = category.Name, FontSize = 24, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 16, 0, 4) }); foreach (var product in category.Products) { var button = new Button { Text = $"{product.Name}{(product.StartingPrice is decimal price ? $" · a partir de R$ {price:N2}" : "")}{(!product.Available ? " · INDISPONÍVEL" : "")}", IsEnabled = product.Available }; button.Clicked += async (_, _) => { TableRuntime.SelectedProduct = product; await Shell.Current.GoToAsync(nameof(ProductConfigurationPage)); }; _content.Add(button); } }
        var cart = new Button { Text = "Ver carrinho" }; cart.Clicked += async (_, _) => await Shell.Current.GoToAsync(nameof(CartPage)); _content.Add(cart);
        var delivery = new Button { Text = "Acompanhar pedidos" }; delivery.Clicked += async (_, _) => await Shell.Current.GoToAsync(nameof(DeliveryPage)); _content.Add(delivery);
    }
    private void AddRetry() { if (_content.Count > 1) return; var retry = new Button { Text = "Tentar novamente" }; retry.Clicked += async (_, _) => await LoadAsync(); _content.Add(retry); }
}
