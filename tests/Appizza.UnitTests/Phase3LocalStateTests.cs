using System.Net;
using System.Text;
using Appizza.Table.Core;

namespace Appizza.UnitTests;

public sealed class Phase3LocalStateTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"appizza-phase3-{Guid.NewGuid():N}");
    private LocalStateDatabase _database = null!;
    private readonly LocalContext _context = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

    public async Task InitializeAsync() { Directory.CreateDirectory(_directory); _database = new LocalStateDatabase(Path.Combine(_directory, "local.db3")); await _database.InitializeAsync(); }
    public async Task DisposeAsync() { await _database.CloseAsync(); Directory.Delete(_directory, true); }

    [Fact]
    public async Task OnlineFirstLoadInstallsCompatibleCacheAndThenUses304()
    {
        var api = new FakeApi { Menu = Menu(HttpStatusCode.OK, MenuJson(1, 1, 0), "\"catalog-1-availability-0-schema-1\"") }; var sync = new MenuSynchronizationService(_database, api, () => true, () => DateTime.UtcNow);
        Assert.Equal(SynchronizationStatus.Refreshed, (await sync.InitializeAsync(_context, default)).Status); api.Menu = Menu(HttpStatusCode.NotModified, null, api.Menu.ETag);
        Assert.Equal(SynchronizationStatus.Current, (await sync.InitializeAsync(_context, default)).Status); Assert.Equal(api.Menu.ETag, (await _database.GetActiveCatalogAsync(_context))!.ETag);
    }

    [Fact]
    public async Task OfflineWithAndWithoutCacheDegradesSafely()
    {
        var offline = new MenuSynchronizationService(_database, new FakeApi(), () => false, () => DateTime.UtcNow); Assert.Equal(SynchronizationStatus.Unavailable, (await offline.InitializeAsync(_context, default)).Status);
        await _database.InstallCatalogAsync(_context, Guid.NewGuid(), 1, 0, 1, "etag", MenuJson(1, 1, 0), DateTime.UtcNow); Assert.Equal(SynchronizationStatus.StaleOffline, (await offline.InitializeAsync(_context, default)).Status);
    }

    [Fact]
    public async Task FutureSchemaNeverReplacesKnownCacheAndUnknownFieldsAreTolerated()
    {
        await _database.InstallCatalogAsync(_context, Guid.NewGuid(), 1, 0, 1, "known", MenuJson(1, 1, 0), DateTime.UtcNow); var api = new FakeApi { Menu = Menu(HttpStatusCode.OK, MenuJson(2, 2, 0, true), "future") }; var result = await new MenuSynchronizationService(_database, api, () => true, () => DateTime.UtcNow).InitializeAsync(_context, default); Assert.Equal(SynchronizationStatus.Current, result.Status); Assert.Equal("known", result.Catalog!.ETag);
        api.Menu = Menu(HttpStatusCode.OK, MenuJson(1, 2, 0, true), "compatible"); Assert.Equal(SynchronizationStatus.Refreshed, (await new MenuSynchronizationService(_database, api, () => true, () => DateTime.UtcNow).InitializeAsync(_context, default)).Status);
    }

    [Fact]
    public async Task AvailabilityOverlayIsReconciledIncrementallyBeforeCompositeMenuCheck()
    {
        var api = new FakeApi { Menu = Menu(HttpStatusCode.OK, MenuJson(1, 4, 8), "\"catalog-4-availability-8-schema-1\"") }; var sync = new MenuSynchronizationService(_database, api, () => true, () => DateTime.UtcNow); await sync.InitializeAsync(_context, default);
        api.Availability = Menu(HttpStatusCode.OK, """{"schemaVersion":1,"catalogVersion":4,"availabilityVersion":9,"ingredients":[],"products":[],"variants":[]}""", "\"availability-9-schema-1\""); api.Menu = Menu(HttpStatusCode.NotModified, null, "\"catalog-4-availability-9-schema-1\"");
        var result = await sync.InitializeAsync(_context, default); Assert.Equal(SynchronizationStatus.Current, result.Status); Assert.Equal(9, result.Catalog!.AvailabilityVersion); Assert.Contains("\"availabilityVersion\":9", result.Catalog.PayloadJson); Assert.Equal(2, api.MenuCalls); Assert.Equal(1, api.AvailabilityCalls);
    }

    [Fact]
    public async Task NewSessionNeverInheritsPreviousCartAndRestartRestoresCurrentCart()
    {
        var now = DateTime.UtcNow; var first = await _database.GetOrCreateCartAsync(_context, 1, 0, now); await _database.UpsertCartItemAsync(first, new(Guid.NewGuid(), Guid.NewGuid(), null, "simple", 2, "{}", "hash", 1, 0, 10.005m), now); var reopened = new LocalStateDatabase(Path.Combine(_directory, "local.db3")); await reopened.InitializeAsync(); Assert.Single(await reopened.GetCartItemsAsync(Guid.ParseExact(first.Id, "N")));
        var next = _context with { SessionId = Guid.NewGuid() }; var second = await reopened.GetOrCreateCartAsync(next, 1, 0, now.AddMinutes(1)); Assert.NotEqual(first.Id, second.Id); Assert.Contains(await reopened.GetCartsAsync(), x => x.Id == first.Id && x.Status == "session_mismatch"); await reopened.CloseAsync();
    }

    [Fact]
    public async Task TenantAndDeviceCachesAreStrictlyIsolatedAndResetInvalidatesActiveState()
    {
        await _database.InstallCatalogAsync(_context, Guid.NewGuid(), 1, 0, 1, "a", MenuJson(1, 1, 0), DateTime.UtcNow); var otherTenant = _context with { EstablishmentId = Guid.NewGuid() }; var otherDevice = _context with { DeviceId = Guid.NewGuid() }; Assert.Null(await _database.GetActiveCatalogAsync(otherTenant)); Assert.Null(await _database.GetActiveCatalogAsync(otherDevice)); await _database.InvalidateContextAsync(_context); Assert.Null(await _database.GetActiveCatalogAsync(_context));
    }

    [Fact]
    public async Task DuplicateLostAndOutOfOrderInvalidationsReconcileWithoutStateRegression()
    {
        await _database.InstallCatalogAsync(_context, Guid.NewGuid(), 2, 3, 1, "new", MenuJson(1, 2, 3), DateTime.UtcNow);
        await _database.InstallAvailabilityAsync(_context, 2, 3, 1, "new-a", """{"schemaVersion":1,"catalogVersion":2,"availabilityVersion":3,"ingredients":[],"products":[],"variants":[]}""", DateTime.UtcNow);
        await _database.InstallCatalogAsync(_context, Guid.NewGuid(), 1, 9, 1, "old", MenuJson(1, 1, 9), DateTime.UtcNow.AddSeconds(1));
        await _database.InstallAvailabilityAsync(_context, 2, 2, 1, "old-a", """{"schemaVersion":1,"catalogVersion":2,"availabilityVersion":2,"ingredients":[],"products":[],"variants":[]}""", DateTime.UtcNow.AddSeconds(1));
        Assert.Equal(2, (await _database.GetActiveCatalogAsync(_context))!.CatalogVersion); Assert.Equal(3, (await _database.GetAvailabilityAsync(_context))!.AvailabilityVersion);
        var api = new FakeApi { Menu = Menu(HttpStatusCode.NotModified, null, "new"), Availability = Menu(HttpStatusCode.NotModified, null, "new-a") }; using var coordinator = new ReconciliationCoordinator(new MenuSynchronizationService(_database, api, () => true, () => DateTime.UtcNow));
        await Task.WhenAll(coordinator.ReconcileAsync(_context, ReconciliationTrigger.SignalRInvalidation, default), coordinator.ReconcileAsync(_context, ReconciliationTrigger.SignalRInvalidation, default), coordinator.ReconcileAsync(_context, ReconciliationTrigger.SignalRReconnected, default));
        Assert.Equal(2, (await _database.GetActiveCatalogAsync(_context))!.CatalogVersion); Assert.True(api.MenuCalls >= 3); Assert.True(api.AvailabilityCalls >= 3);
    }

    [Theory]
    [InlineData("10.005", "10.01")]
    [InlineData("10.004", "10.00")]
    public void MoneyUsesDecimalAndAwayFromZero(string input, string expected) => Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Money.Estimate(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void MultiFlavorPricePreservesIntermediatePrecision() => Assert.Equal(33.34m, Money.EqualFlavorPrice([33.33m, 33.34m, 33.34m]));

    [Fact]
    public async Task MediaCacheValidatesChecksumAndEvictsLeastRecentlyUsed()
    {
        var mediaRoot = Path.Combine(_directory, "media"); var space = new FakeSpace(); var cache = new MediaCacheService(_database, mediaRoot, new MediaCacheOptions(6, 0), space, () => DateTime.UtcNow); var first = Guid.NewGuid(); var second = Guid.NewGuid(); var firstBytes = Encoding.UTF8.GetBytes("1234"); var secondBytes = Encoding.UTF8.GetBytes("5678"); await cache.StoreAsync(_context, first, Hash(firstBytes), "image/png", new MemoryStream(firstBytes), default); await cache.StoreAsync(_context, second, Hash(secondBytes), "image/png", new MemoryStream(secondBytes), default); Assert.Null(await cache.TryGetAsync(_context, first, Hash(firstBytes))); Assert.NotNull(await cache.TryGetAsync(_context, second, Hash(secondBytes)));
    }

    [Fact]
    public async Task MediaCacheProtectsCriticalFreeSpaceAndRejectsBadChecksum()
    {
        var cache = new MediaCacheService(_database, Path.Combine(_directory, "media"), new MediaCacheOptions(100, 10), new FakeSpace { Bytes = 5 }, () => DateTime.UtcNow); var bytes = Encoding.UTF8.GetBytes("data"); await Assert.ThrowsAsync<InvalidDataException>(() => cache.StoreAsync(_context, Guid.NewGuid(), new string('0', 64), "image/png", new MemoryStream(bytes), default)); await Assert.ThrowsAsync<IOException>(() => cache.StoreAsync(_context, Guid.NewGuid(), Hash(bytes), "image/png", new MemoryStream(bytes), default));
    }

    private static MenuDownload Menu(HttpStatusCode status, string? json, string? etag) => new(status, json, etag);
    private static string MenuJson(int schema, long catalog, long availability, bool unknown = false) => $$"""{"schemaVersion":{{schema}},"menu":{"catalogRevisionId":"{{Guid.NewGuid()}}","catalogVersion":{{catalog}},"availabilityVersion":{{availability}}},"availability":{"schemaVersion":{{schema}},"catalogVersion":{{catalog}},"availabilityVersion":{{availability}},"ingredients":[],"products":[],"variants":[]}{{(unknown ? ",\"futureField\":true" : "")}}}""";
    private static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed class FakeApi : ITableMenuApi { public MenuDownload Menu { get; set; } = new(HttpStatusCode.ServiceUnavailable, null, null); public MenuDownload Availability { get; set; } = new(HttpStatusCode.NotModified, null, null); public int MenuCalls { get; private set; } public int AvailabilityCalls { get; private set; } public Task<MenuDownload> GetMenuAsync(string? etag, CancellationToken cancellationToken) { MenuCalls++; return Task.FromResult(Menu); } public Task<MenuDownload> GetAvailabilityAsync(long catalogVersion, string? etag, CancellationToken cancellationToken) { AvailabilityCalls++; return Task.FromResult(Availability); } }
    private sealed class FakeSpace : IFreeSpaceProvider { public long Bytes { get; set; } = long.MaxValue; public long AvailableBytes(string path) => Bytes; }
}
