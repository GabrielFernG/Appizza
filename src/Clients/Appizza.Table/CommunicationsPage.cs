using Appizza.Table.Core;

namespace Appizza.Table;

public sealed class CommunicationsPage : ContentPage
{
    private readonly VerticalStackLayout _layout = new() { Padding = 24, Spacing = 12 };
    private readonly Label _status = new() { FontAttributes = FontAttributes.Bold };
    private bool _visible;

    public CommunicationsPage()
    {
        Title = "Comunicados";
        Content = new ScrollView { Content = _layout };
        _layout.Add(_status);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (TableRuntime.Communications is not { } communications) { _status.Text = "Sessão do tablet indisponível."; return; }
        _visible = true;
        communications.StateChanged += OnStateChanged;
        await communications.RefreshAsync();
    }

    protected override void OnDisappearing()
    {
        if (_visible && TableRuntime.Communications is { } communications) communications.StateChanged -= OnStateChanged;
        _visible = false;
        base.OnDisappearing();
    }

    private void OnStateChanged(CommunicationsState state)
    {
        MainThread.BeginInvokeOnMainThread(() => Render(state));
    }

    private void Render(CommunicationsState state)
    {
        while (_layout.Count > 1) _layout.RemoveAt(1);
        _status.Text = state.Status switch
        {
            CommunicationsLoadStatus.Loading => "Carregando comunicados...",
            CommunicationsLoadStatus.Empty => "Nenhum comunicado vigente.",
            CommunicationsLoadStatus.Error => "Não foi possível carregar os comunicados.",
            _ => ""
        };
        if (state.Status == CommunicationsLoadStatus.Error)
        {
            var retry = new Button { Text = "Tentar novamente" };
            retry.Clicked += async (_, _) => await (TableRuntime.Communications?.RefreshAsync() ?? Task.CompletedTask);
            _layout.Add(retry);
            return;
        }
        foreach (var item in state.Items)
        {
            var title = new Label { Text = item.Title ?? "Comunicado", FontSize = 22, FontAttributes = FontAttributes.Bold };
            var body = new Label { Text = item.Body ?? string.Empty };
            var frame = new Border { Padding = 12, Content = new VerticalStackLayout { Spacing = 6 } };
            ((VerticalStackLayout)frame.Content).Add(title);
            if (!string.IsNullOrWhiteSpace(item.Body)) ((VerticalStackLayout)frame.Content).Add(body);
            if (item.MediaAssetId is not null)
                ((VerticalStackLayout)frame.Content).Add(new Label { Text = string.Equals(item.MediaType, "video", StringComparison.OrdinalIgnoreCase) ? "Vídeo disponível" : "Imagem disponível", FontAttributes = FontAttributes.Italic });
            _layout.Add(frame);
        }
    }
}
