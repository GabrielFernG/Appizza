using System.Net;
using System.Text.Json;
using Appizza.Table.Core;

namespace Appizza.Table;

public sealed class DeliveryPage : ContentPage
{
    private readonly VerticalStackLayout _content = new() { Padding = 24, Spacing = 12 };
    private readonly Label _status = new() { FontAttributes = FontAttributes.Bold };
    private readonly Dictionary<Guid, bool> _busy = [];
    public DeliveryPage() { Title = "Acompanhar pedido"; _content.Add(_status); Content = new ScrollView { Content = _content }; }
    protected override async void OnAppearing() { base.OnAppearing(); await RefreshAsync(); }
    private async Task RefreshAsync()
    {
        try
        {
            var result = await new TableDeliveryApi(TableRuntime.Http).GetStatusAsync(CancellationToken.None);
            _status.Text = "Status atualizado"; while (_content.Count > 1) _content.RemoveAt(1);
            foreach (var item in result.Orders.SelectMany(x => x.Items)) Render(item);
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private void Render(TableOrderItem item)
    {
        var delivery = item.Delivery; var title = new Label { Text = $"{item.ProductName} — {StatusLabel(item)}", FontAttributes = FontAttributes.Bold };
        _content.Add(title);
        if (delivery?.AttentionRequired == true) _content.Add(new Label { Text = $"Entrega contestada{(string.IsNullOrWhiteSpace(delivery.Contest?.Reason) ? "" : $": {delivery.Contest.Reason}")}" });
        if (item.PublicStatus == "on_the_way" && delivery?.Confirmation?.Status == "pending") AddAction(item, "Confirmar entrega", () => ConfirmAsync(item));
        if (delivery?.Confirmation?.Status == "confirmed_automatic" && delivery.Contest is null) AddAction(item, "Informar problema", () => ContestAsync(item));
    }
    private void AddAction(TableOrderItem item, string text, Func<Task> action) { var button = new Button { Text = text }; button.Clicked += async (_, _) => { if (_busy.GetValueOrDefault(item.ItemId)) return; _busy[item.ItemId] = true; button.IsEnabled = false; try { await action(); } finally { _busy[item.ItemId] = false; button.IsEnabled = true; } }; _content.Add(button); }
    private async Task ConfirmAsync(TableOrderItem item) { await SendAsync(new TableDeliveryApi(TableRuntime.Http).ConfirmAsync(item.ItemId, item.Version, Guid.NewGuid(), CancellationToken.None)); }
    private async Task ContestAsync(TableOrderItem item) { await SendAsync(new TableDeliveryApi(TableRuntime.Http).ContestAsync(item.ItemId, item.Version, "NOT_RECEIVED", null, Guid.NewGuid(), CancellationToken.None)); }
    private async Task SendAsync(Task<HttpResponseMessage> operation)
    {
        using var response = await operation; if (!response.IsSuccessStatusCode) { string? code = null; try { using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); code = json.RootElement.TryGetProperty("errorCode", out var value) ? value.GetString() : null; } catch { } _status.Text = DeliveryErrorMessages.For(response.StatusCode, code); }
        else await RefreshAsync();
    }
    private static string StatusLabel(TableOrderItem item) => item.Delivery?.AttentionRequired == true ? "Entrega contestada" : item.Delivery?.Confirmation?.Status switch { "pending" => "Aguardando confirmação", "confirmed_automatic" => "Confirmada automaticamente", "confirmed_manual" => "Entregue", _ => item.PublicStatus };
}
