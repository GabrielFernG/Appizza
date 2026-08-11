using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Appizza.Table;

public partial class MainPage : ContentPage
{
    private static readonly HttpClient Http = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private Guid _deviceId;
    private string? _configurationToken;
    private string? _userAccessToken;
    private string? _deviceAccessToken;

    public MainPage() => InitializeComponent();

    private async void OnRegisterAndSignIn(object? sender, EventArgs args)
    {
        try
        {
            Http.BaseAddress = new Uri(ApiUrl.Text.TrimEnd('/') + "/", UriKind.Absolute);
            var installationText = await SecureStorage.Default.GetAsync("installation_id");
            var installationId = Guid.TryParse(installationText, out var stored) ? stored : Guid.NewGuid();
            await SecureStorage.Default.SetAsync("installation_id", installationId.ToString());

            var registration = await PostAsync<RegisterResponse>("api/v1/table-devices/register", new
            {
                installationId,
                deviceName = DeviceInfo.Name,
                platform = DeviceInfo.Platform.ToString().ToLowerInvariant(),
                model = DeviceInfo.Model,
                operatingSystemVersion = DeviceInfo.VersionString,
                applicationVersion = AppInfo.Current.VersionString
            });
            _deviceId = registration.DeviceId;
            _configurationToken = registration.ConfigurationToken;

            var signIn = await PostAsync<SignInResponse>("api/v1/auth/sign-in", new
            {
                establishmentCode = EstablishmentCode.Text,
                login = Login.Text,
                password = Password.Text
            });
            _userAccessToken = signIn.AccessToken;
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userAccessToken);
            var available = await Http.GetFromJsonAsync<AvailableTablesResponse>("api/v1/table-devices/configuration/available-tables");
            Tables.ItemsSource = available?.Tables.ToList() ?? [];
            Status.Text = "Dispositivo registrado. Selecione uma mesa.";
        }
        catch (Exception exception)
        {
            Status.Text = exception.Message;
        }
    }

    private async void OnBind(object? sender, EventArgs args)
    {
        if (Tables.SelectedItem is not TableItem table || _configurationToken is null) return;
        try
        {
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userAccessToken);
            var binding = await PostAsync<BindResponse>($"api/v1/table-devices/{_deviceId}/bind", new
            {
                tableId = table.Id,
                configurationToken = _configurationToken
            });
            _deviceAccessToken = binding.DeviceAccessToken;
            await SecureStorage.Default.SetAsync("device_refresh_token", binding.RefreshToken);
            Status.Text = $"Tablet vinculado à {table.Name}.";
        }
        catch (Exception exception)
        {
            Status.Text = exception.Message;
        }
    }

    private async void OnOpenSession(object? sender, EventArgs args)
    {
        if (_deviceAccessToken is null) return;
        try
        {
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _deviceAccessToken);
            var response = await Http.PostAsync("api/v1/table-device/session/open-or-get", null);
            var body = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(body);
            Status.Text = $"Sessão {json.RootElement.GetProperty("session").GetProperty("number").GetString()} pronta.";
        }
        catch (Exception exception)
        {
            Status.Text = exception.Message;
        }
    }

    private static async Task<T> PostAsync<T>(string path, object body)
    {
        var response = await Http.PostAsJsonAsync(path, body);
        var content = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<T>(content, JsonOptions)!;
    }

    private sealed record RegisterResponse(Guid DeviceId, string ConfigurationToken);
    private sealed record SignInResponse(string AccessToken, string RefreshToken);
    private sealed record BindResponse(string DeviceAccessToken, string RefreshToken);
    private sealed record AvailableTablesResponse(IReadOnlyList<TableItem> Tables);
    private sealed record TableItem(Guid Id, string Name);
}
