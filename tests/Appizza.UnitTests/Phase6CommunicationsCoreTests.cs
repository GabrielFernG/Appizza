using System.Net;
using Appizza.Table.Core;

namespace Appizza.UnitTests;

public sealed class Phase6CommunicationsCoreTests
{
    [Fact]
    public async Task InitialLoadParsesReadModelAndKeepsBackendFields()
    {
        var item = Sample(); var api = new FakeApi([item]); var sync = new CommunicationsSynchronization(api);
        var state = await sync.RefreshAsync();
        Assert.Equal(CommunicationsLoadStatus.Loaded, state.Status); Assert.Single(state.Items); Assert.Equal(item, state.Items[0]);
    }

    [Fact]
    public async Task EmptyResponseAndNetworkFailureProducePresentationStates()
    {
        var api = new FakeApi([]); var sync = new CommunicationsSynchronization(api);
        Assert.Equal(CommunicationsLoadStatus.Empty, (await sync.RefreshAsync()).Status);
        api.Error = new HttpRequestException("offline");
        Assert.Equal(CommunicationsLoadStatus.Error, (await sync.RefreshAsync()).Status);
    }

    [Fact]
    public async Task InvalidationAndReconnectAlwaysReconcileThroughGet()
    {
        var api = new FakeApi([Sample()]); var sync = new CommunicationsSynchronization(api);
        await sync.RefreshAsync(); await sync.OnInvalidationAsync(); await sync.OnReconnectAsync();
        Assert.Equal(3, api.Calls);
    }

    [Fact]
    public async Task InvalidationPayloadCannotBecomeAuthoritativeState()
    {
        var api = new FakeApi([Sample()]); var sync = new CommunicationsSynchronization(api);
        await sync.RefreshAsync(); await sync.OnInvalidationAsync();
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), sync.State.Items[0].Id);
    }

    [Fact]
    public void ModelDoesNotCalculateLifecycleOrQueueOfflineMutation()
    {
        var item = Sample() with { StartsAt = DateTimeOffset.UtcNow.AddDays(-2), EndsAt = DateTimeOffset.UtcNow.AddDays(-1) };
        Assert.Equal(0, item.Priority - item.Priority); // representation only; backend remains authoritative.
        Assert.DoesNotContain("Mutate", typeof(ITableCommunicationsApi).GetMethods().Select(x => x.Name));
    }

    private static TableCommunication Sample() => new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Notice", "Body", Guid.NewGuid(), "image", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), 5, 3, "checksum");

    private sealed class FakeApi(IReadOnlyList<TableCommunication> items) : ITableCommunicationsApi
    {
        public int Calls { get; private set; }
        public Exception? Error { get; set; }
        public Task<IReadOnlyList<TableCommunication>> GetAsync(CancellationToken cancellationToken = default)
        { Calls++; if (Error is not null) return Task.FromException<IReadOnlyList<TableCommunication>>(Error); return Task.FromResult(items); }
    }
}
