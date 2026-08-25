using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Appizza.Table.Core;

public sealed record TableCommunication(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("mediaAssetId")] Guid? MediaAssetId,
    [property: JsonPropertyName("mediaType")] string? MediaType,
    [property: JsonPropertyName("startsAt")] DateTimeOffset StartsAt,
    [property: JsonPropertyName("endsAt")] DateTimeOffset EndsAt,
    [property: JsonPropertyName("priority")] int Priority,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("mediaChecksumSha256")] string? MediaChecksumSha256 = null);

public interface ITableCommunicationsApi
{
    Task<IReadOnlyList<TableCommunication>> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class TableCommunicationsApi(HttpClient client) : ITableCommunicationsApi
{
    public async Task<IReadOnlyList<TableCommunication>> GetAsync(CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync("api/v1/table-device/session/communications", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new TableCommunicationsHttpException(response.StatusCode);
        return await response.Content.ReadFromJsonAsync<List<TableCommunication>>(cancellationToken: cancellationToken) ?? [];
    }
}

public sealed class TableCommunicationsHttpException(HttpStatusCode statusCode)
    : HttpRequestException($"Communications request failed with {(int)statusCode}.")
{
    public new HttpStatusCode StatusCode { get; } = statusCode;
}

public enum CommunicationsLoadStatus { Loading, Loaded, Empty, Error }
public sealed record CommunicationsState(CommunicationsLoadStatus Status, IReadOnlyList<TableCommunication> Items, string? Error = null);

public sealed class CommunicationsSynchronization(ITableCommunicationsApi api) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public CommunicationsState State { get; private set; } = new(CommunicationsLoadStatus.Loading, []);
    public event Action<CommunicationsState>? StateChanged;

    public async Task<CommunicationsState> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            State = State with { Status = CommunicationsLoadStatus.Loading, Error = null };
            try
            {
                var items = await api.GetAsync(cancellationToken);
                State = new(items.Count == 0 ? CommunicationsLoadStatus.Empty : CommunicationsLoadStatus.Loaded, items);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                State = new(CommunicationsLoadStatus.Error, State.Items, ex.Message);
            }
            StateChanged?.Invoke(State);
            return State;
        }
        finally { _gate.Release(); }
    }

    public Task OnInvalidationAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);
    public Task OnReconnectAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);
    public void Dispose() => _gate.Dispose();
}

public sealed record CommunicationMedia(string Kind, string? LocalPath, bool IsFallback);

public interface ICommunicationMediaResolver
{
    Task<CommunicationMedia> ResolveAsync(TableCommunication communication, LocalContext context, CancellationToken cancellationToken = default);
}

public sealed class CommunicationMediaResolver(MediaCacheService cache) : ICommunicationMediaResolver
{
    public async Task<CommunicationMedia> ResolveAsync(TableCommunication communication, LocalContext context, CancellationToken cancellationToken = default)
    {
        if (communication.MediaAssetId is null || string.IsNullOrWhiteSpace(communication.MediaChecksumSha256))
            return new(communication.MediaType ?? "", null, true);
        try
        {
            var path = await cache.TryGetAsync(context, communication.MediaAssetId.Value, communication.MediaChecksumSha256);
            return new(communication.MediaType ?? "", path, path is null);
        }
        catch (IOException) { return new(communication.MediaType ?? "", null, true); }
        }
    }
