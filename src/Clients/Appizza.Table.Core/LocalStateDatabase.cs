using SQLite;

namespace Appizza.Table.Core;

public sealed class LocalStateDatabase(string databasePath)
{
    private readonly SQLiteAsyncConnection _connection = new(databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);

    public async Task InitializeAsync()
    {
        var journalMode = await _connection.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL");
        if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SQLite WAL mode could not be enabled (actual: {journalMode}).");
        var version = await _connection.ExecuteScalarAsync<int>("PRAGMA user_version");
        if (version > LocalContract.DatabaseVersion) throw new NotSupportedException($"SQLite schema {version} is not supported.");
        if (version < 1)
        {
            await _connection.CreateTablesAsync(CreateFlags.None, typeof(CatalogCacheRow), typeof(AvailabilityCacheRow), typeof(LocalCartRow), typeof(LocalCartItemRow), typeof(MediaCacheRow), typeof(SyncStateRow));
            await _connection.ExecuteAsync($"PRAGMA user_version={LocalContract.DatabaseVersion}");
        }
        else if (version < 2)
        {
            await _connection.RunInTransactionAsync(connection =>
            {
                connection.Execute("alter table local_cart add column SimulationId text null"); connection.Execute("alter table local_cart add column SimulationVersion text null"); connection.Execute("alter table local_cart add column SimulationValidUntilUtc datetime null"); connection.Execute("alter table local_cart add column RequiresReview integer not null default 0"); connection.Execute("alter table local_cart add column ClientSubmissionId text null"); connection.Execute("alter table local_cart add column IdempotencyKey text null"); connection.Execute("alter table local_cart add column AuthoritativeResultJson text null"); connection.Execute("alter table local_cart add column SubmittedOrderId text null"); connection.Execute("PRAGMA user_version=2");
            });
        }
    }

    public async Task<CachedCatalog?> GetActiveCatalogAsync(LocalContext context)
    {
        var establishmentId = Key(context.EstablishmentId); var deviceId = Key(context.DeviceId);
        var row = await _connection.Table<CatalogCacheRow>().Where(x => x.EstablishmentId == establishmentId && x.DeviceId == deviceId && x.IsActive && x.SchemaVersion == LocalContract.MenuSchemaVersion).OrderByDescending(x => x.DownloadedAtUtc).FirstOrDefaultAsync();
        return row is null ? null : new CachedCatalog(row.PayloadJson, row.ETag, row.CatalogVersion, row.AvailabilityVersion, row.SchemaVersion);
    }

    public async Task InstallCatalogAsync(LocalContext context, Guid revisionId, long catalogVersion, long availabilityVersion, int schemaVersion, string etag, string payloadJson, DateTime nowUtc)
    {
        if (schemaVersion != LocalContract.MenuSchemaVersion) throw new NotSupportedException($"Menu schema {schemaVersion} is not supported.");
        using var document = System.Text.Json.JsonDocument.Parse(payloadJson);
        var establishmentId = Key(context.EstablishmentId); var deviceId = Key(context.DeviceId);
        var current = await _connection.Table<CatalogCacheRow>().Where(x => x.EstablishmentId == establishmentId && x.DeviceId == deviceId && x.IsActive).OrderByDescending(x => x.CatalogVersion).FirstOrDefaultAsync();
        if (current is not null && current.CatalogVersion > catalogVersion) return;
        await _connection.RunInTransactionAsync(connection =>
        {
            connection.Execute("update catalog_cache set IsActive = 0 where EstablishmentId = ? and DeviceId = ?", establishmentId, deviceId);
            connection.InsertOrReplace(new CatalogCacheRow { Id = $"{Key(context.EstablishmentId)}:{Key(context.DeviceId)}:{catalogVersion}", EstablishmentId = Key(context.EstablishmentId), DeviceId = Key(context.DeviceId), CatalogRevisionId = Key(revisionId), CatalogVersion = catalogVersion, AvailabilityVersion = availabilityVersion, SchemaVersion = schemaVersion, ETag = etag, PayloadJson = payloadJson, IsActive = true, DownloadedAtUtc = nowUtc });
            var obsolete = connection.Table<CatalogCacheRow>().Where(x => x.EstablishmentId == establishmentId && x.DeviceId == deviceId).OrderByDescending(x => x.DownloadedAtUtc).Skip(2).ToList();
            foreach (var row in obsolete) connection.Delete(row);
        });
    }

    public async Task InstallAvailabilityAsync(LocalContext context, long catalogVersion, long availabilityVersion, int schemaVersion, string etag, string payloadJson, DateTime nowUtc)
    {
        if (schemaVersion != LocalContract.MenuSchemaVersion) throw new NotSupportedException($"Availability schema {schemaVersion} is not supported.");
        var current = await GetAvailabilityAsync(context); if (current is not null && (current.CatalogVersion > catalogVersion || current.CatalogVersion == catalogVersion && current.AvailabilityVersion > availabilityVersion)) return;
        await _connection.InsertOrReplaceAsync(new AvailabilityCacheRow { Id = $"{Key(context.EstablishmentId)}:{Key(context.DeviceId)}", EstablishmentId = Key(context.EstablishmentId), DeviceId = Key(context.DeviceId), CatalogVersion = catalogVersion, AvailabilityVersion = availabilityVersion, SchemaVersion = schemaVersion, ETag = etag, PayloadJson = payloadJson, DownloadedAtUtc = nowUtc });
    }

    public async Task<CachedAvailability?> GetAvailabilityAsync(LocalContext context)
    { var establishmentId = Key(context.EstablishmentId); var deviceId = Key(context.DeviceId); var row = await _connection.Table<AvailabilityCacheRow>().Where(x => x.EstablishmentId == establishmentId && x.DeviceId == deviceId && x.SchemaVersion == LocalContract.MenuSchemaVersion).FirstOrDefaultAsync(); return row is null ? null : new(row.PayloadJson, row.ETag, row.CatalogVersion, row.AvailabilityVersion, row.SchemaVersion); }

    public async Task<CachedCatalog> ApplyAvailabilityAsync(LocalContext context, CachedCatalog catalog, CachedAvailability availability, DateTime nowUtc)
    {
        if (catalog.CatalogVersion != availability.CatalogVersion) throw new InvalidOperationException("Catalog and availability versions are incompatible.");
        var root = System.Text.Json.Nodes.JsonNode.Parse(catalog.PayloadJson)!.AsObject(); var overlay = System.Text.Json.Nodes.JsonNode.Parse(availability.PayloadJson)!;
        root["availability"] = overlay.DeepClone(); root["menu"]!["availabilityVersion"] = availability.AvailabilityVersion;
        var etag = $"\"catalog-{catalog.CatalogVersion}-availability-{availability.AvailabilityVersion}-schema-{catalog.SchemaVersion}\"";
        var rowId = $"{Key(context.EstablishmentId)}:{Key(context.DeviceId)}:{catalog.CatalogVersion}"; var row = await _connection.FindAsync<CatalogCacheRow>(rowId) ?? throw new InvalidOperationException("Active catalog cache row was not found."); row.PayloadJson = root.ToJsonString(); row.AvailabilityVersion = availability.AvailabilityVersion; row.ETag = etag; row.DownloadedAtUtc = nowUtc; await _connection.UpdateAsync(row); return new(row.PayloadJson, row.ETag, row.CatalogVersion, row.AvailabilityVersion, row.SchemaVersion);
    }

    public async Task<LocalCartRow> GetOrCreateCartAsync(LocalContext context, long catalogVersion, long availabilityVersion, DateTime nowUtc)
    {
        if (context.SessionId is not Guid sessionId) throw new InvalidOperationException("A session is required for a cart.");
        await MarkOtherSessionsMismatchedAsync(context, nowUtc);
        var establishmentId = Key(context.EstablishmentId); var deviceId = Key(context.DeviceId); var currentSessionId = Key(sessionId);
        var cart = await _connection.Table<LocalCartRow>().Where(x => x.EstablishmentId == establishmentId && x.DeviceId == deviceId && x.SessionId == currentSessionId && x.Status != "submitted" && x.Status != "session_mismatch").OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefaultAsync();
        if (cart is not null) return cart;
        cart = new LocalCartRow { Id = Key(Guid.NewGuid()), EstablishmentId = Key(context.EstablishmentId), DeviceId = Key(context.DeviceId), SessionId = Key(sessionId), CatalogVersion = catalogVersion, AvailabilityVersion = availabilityVersion, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc }; await _connection.InsertAsync(cart); return cart;
    }

    public async Task UpsertCartItemAsync(LocalCartRow cart, CartItemInput item, DateTime nowUtc)
    {
        if (cart.Status != "active") throw new InvalidOperationException("Only an active cart can be edited.");
        await _connection.InsertOrReplaceAsync(new LocalCartItemRow { Id = Key(item.Id), CartId = cart.Id, ProductId = Key(item.ProductId), ProductVariantId = item.ProductVariantId is Guid variant ? Key(variant) : null, ProductType = item.ProductType, Quantity = item.Quantity, ConfigurationJson = item.ConfigurationJson, ConfigurationVersion = item.ConfigurationVersion, SourceCatalogVersion = item.CatalogVersion, SourceAvailabilityVersion = item.AvailabilityVersion, EstimatedUnitAmount = item.EstimatedUnitAmount, EstimatedTotalAmount = Money.Estimate(item.EstimatedUnitAmount * item.Quantity), ValidationState = item.ValidationState, CreatedAtUtc = nowUtc, UpdatedAtUtc = nowUtc });
        cart.UpdatedAtUtc = nowUtc; await _connection.UpdateAsync(cart);
    }

    public Task<List<LocalCartItemRow>> GetCartItemsAsync(Guid cartId) { var id = Key(cartId); return _connection.Table<LocalCartItemRow>().Where(x => x.CartId == id).ToListAsync(); }
    public Task<List<LocalCartRow>> GetCartsAsync() => _connection.Table<LocalCartRow>().ToListAsync();

    public async Task RecordSimulationAsync(Guid cartId, Guid simulationId, string simulationVersion, DateTime validUntilUtc, bool requiresReview, string resultJson, DateTime nowUtc)
    { var row = await _connection.FindAsync<LocalCartRow>(Key(cartId)) ?? throw new InvalidOperationException("Cart not found."); row.SimulationId = Key(simulationId); row.SimulationVersion = simulationVersion; row.SimulationValidUntilUtc = validUntilUtc; row.RequiresReview = requiresReview; row.AuthoritativeResultJson = resultJson; row.Status = requiresReview ? "requires_review" : "active"; row.UpdatedAtUtc = nowUtc; await _connection.UpdateAsync(row); }
    public async Task<(Guid ClientSubmissionId, Guid IdempotencyKey)> BeginSubmissionAsync(Guid cartId, Guid clientSubmissionId, Guid idempotencyKey, DateTime nowUtc)
    {
        (Guid ClientSubmissionId, Guid IdempotencyKey) result = default;
        await _connection.RunInTransactionAsync(connection =>
        {
            var row = connection.Find<LocalCartRow>(Key(cartId)) ?? throw new InvalidOperationException("Cart not found.");
            if (row.SimulationId is null) throw new InvalidOperationException("Simulation is required.");
            if (row.ClientSubmissionId is null) row.ClientSubmissionId = Key(clientSubmissionId);
            if (row.IdempotencyKey is null) row.IdempotencyKey = Key(idempotencyKey);
            row.Status = "submitting"; row.UpdatedAtUtc = nowUtc; connection.Update(row);
            result = (Guid.ParseExact(row.ClientSubmissionId, "N"), Guid.ParseExact(row.IdempotencyKey, "N"));
        });
        return result;
    }
    public async Task MarkSubmissionUnknownAsync(Guid cartId, DateTime nowUtc) { var row = await _connection.FindAsync<LocalCartRow>(Key(cartId)) ?? throw new InvalidOperationException("Cart not found."); row.Status = "submission_unknown"; row.UpdatedAtUtc = nowUtc; await _connection.UpdateAsync(row); }
    public async Task MarkSubmittedAsync(Guid cartId, Guid orderId, string responseJson, DateTime nowUtc) { var row = await _connection.FindAsync<LocalCartRow>(Key(cartId)) ?? throw new InvalidOperationException("Cart not found."); row.Status = "submitted"; row.SubmittedOrderId = Key(orderId); row.AuthoritativeResultJson = responseJson; row.UpdatedAtUtc = nowUtc; await _connection.UpdateAsync(row); }
    public async Task<LocalSubmissionState> GetSubmissionStateAsync(Guid cartId) { var row = await _connection.FindAsync<LocalCartRow>(Key(cartId)) ?? throw new InvalidOperationException("Cart not found."); return new(cartId, row.Status, Parse(row.SimulationId), row.SimulationVersion, row.SimulationValidUntilUtc, row.RequiresReview, Parse(row.ClientSubmissionId), Parse(row.IdempotencyKey), row.AuthoritativeResultJson, Parse(row.SubmittedOrderId)); }

    public async Task ReconcileActiveCartAsync(LocalContext context, MenuPresentation menu, string availabilityJson, DateTime nowUtc)
    {
        if (context.SessionId is not Guid sessionId) return; var establishmentId = Key(context.EstablishmentId); var deviceId = Key(context.DeviceId); var currentSessionId = Key(sessionId);
        var cart = await _connection.Table<LocalCartRow>().Where(x => x.EstablishmentId == establishmentId && x.DeviceId == deviceId && x.SessionId == currentSessionId && x.Status == "active").FirstOrDefaultAsync(); if (cart is null) return;
        var products = menu.Categories.SelectMany(x => x.Products).ToDictionary(x => x.Id); var items = await _connection.Table<LocalCartItemRow>().Where(x => x.CartId == cart.Id).ToListAsync();
        foreach (var item in items)
        {
            var productId = Guid.ParseExact(item.ProductId, "N"); if (!products.TryGetValue(productId, out var product)) item.ValidationState = "catalog_item_removed";
            else if (!product.ConfigurationVersion.Equals(item.ConfigurationVersion, StringComparison.Ordinal)) item.ValidationState = "configuration_changed";
            else item.ValidationState = PublishedMenuReader.ReconcileSelection(item.ConfigurationJson, availabilityJson).IsValid && product.Available ? "valid_estimate" : "availability_changed";
            item.UpdatedAtUtc = nowUtc; await _connection.UpdateAsync(item);
        }
        cart.CatalogVersion = menu.CatalogVersion; cart.AvailabilityVersion = menu.AvailabilityVersion; cart.UpdatedAtUtc = nowUtc; await _connection.UpdateAsync(cart);
    }

    public async Task MarkOtherSessionsMismatchedAsync(LocalContext context, DateTime nowUtc)
    {
        var session = context.SessionId is Guid id ? Key(id) : ""; await _connection.ExecuteAsync("update local_cart set Status = 'session_mismatch', UpdatedAtUtc = ? where EstablishmentId = ? and DeviceId = ? and SessionId <> ? and Status not in ('submitted', 'session_mismatch')", nowUtc, Key(context.EstablishmentId), Key(context.DeviceId), session);
        var cutoff = nowUtc.AddDays(-LocalContract.OldCartRetentionDays); var expired = await _connection.Table<LocalCartRow>().Where(x => x.Status != "active" && x.UpdatedAtUtc < cutoff).ToListAsync(); foreach (var cart in expired) { await _connection.ExecuteAsync("delete from local_cart_item where CartId = ?", cart.Id); await _connection.DeleteAsync(cart); }
    }

    public Task InvalidateContextAsync(LocalContext context) => _connection.RunInTransactionAsync(connection => { connection.Execute("update catalog_cache set IsActive = 0 where EstablishmentId = ? and DeviceId = ?", Key(context.EstablishmentId), Key(context.DeviceId)); connection.Execute("update local_cart set Status = 'session_mismatch' where EstablishmentId = ? and DeviceId = ? and Status = 'active'", Key(context.EstablishmentId), Key(context.DeviceId)); });
    public SQLiteAsyncConnection Connection => _connection;
    public Task CloseAsync() => _connection.CloseAsync();
    private static string Key(Guid value) => value.ToString("N");
    private static Guid? Parse(string? value) => Guid.TryParseExact(value, "N", out var id) ? id : null;
}

public static class Money
{
    public static decimal Estimate(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    public static decimal EqualFlavorPrice(IEnumerable<decimal> prices) { var values = prices.ToArray(); if (values.Length == 0) throw new ArgumentException("At least one flavor is required.", nameof(prices)); return Estimate(values.Sum() / values.Length); }
}
