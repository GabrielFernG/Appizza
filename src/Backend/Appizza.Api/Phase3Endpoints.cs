using System.Security.Claims;
using System.Text.Json;
using Appizza.Modules.Catalog;
using Appizza.Modules.Devices;
using Appizza.Modules.Media;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api;

public static class Phase3Endpoints
{
    public static IEndpointRouteBuilder MapPhase3Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/table-device").RequireAuthorization();
        group.MapGet("/menu", GetMenu);
        group.MapGet("/menu/availability", GetAvailability);
        group.MapGet("/menu/products/{productId:guid}", GetProduct);
        group.MapGet("/menu/combos/{productId:guid}", GetCombo);
        group.MapGet("/media-assets/{assetId:guid}/content", GetMediaContent);
        return app;
    }

    private static async Task<IResult> GetMenu(HttpRequest request, HttpResponse response, ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct)
    {
        var validation = await ValidateDevice(principal, db, ct); if (validation.Error is not null) return validation.Error; var tenant = validation.Device!.EstablishmentId!.Value;
        var published = await Published(db, tenant, ct); if (published is null) return Error(404, "CATALOG_NOT_PUBLISHED", "Não existe catálogo publicado.");
        var (state, revision) = published.Value; var etag = PublishedMenuContract.MenuETag(state.CatalogVersion, state.AvailabilityVersion); response.Headers.ETag = etag; response.Headers.CacheControl = "private, no-cache";
        if (request.Headers.IfNoneMatch.Any(value => value == etag)) return Results.StatusCode(StatusCodes.Status304NotModified);
        using var snapshot = JsonDocument.Parse(revision.Snapshot); var availability = await Availability(db, tenant, state, ct); var assetIds = ReferencedMediaIds(snapshot.RootElement); var media = await db.Set<MediaAsset>().Where(x => assetIds.Contains(x.Id) && x.EstablishmentId == tenant && x.Status == "ready").OrderBy(x => x.Id).Select(x => new PublishedMediaManifestItem(x.Id, x.MimeType, x.FileSize, x.ChecksumSha256, $"{x.Id:N}:{x.ChecksumSha256}")).ToListAsync(ct);
        var configurationVersions = Elements(snapshot.RootElement, "products").ToDictionary(product => GetGuid(product, "id"), product => SemanticConfigurationHash.Compute(BuildProductConfiguration(snapshot.RootElement, product, GetGuid(product, "id"))));
        return Results.Ok(new { schemaVersion = PublishedMenuContract.SchemaVersion, menu = new PublishedMenuHeader(revision.Id, state.CatalogVersion, state.AvailabilityVersion, PublishedMenuContract.SchemaVersion, revision.PublishedAt!.Value), catalog = snapshot.RootElement.Clone(), availability, configurationVersions, mediaManifest = media, settings = new { currencyCode = "BRL", priceDisplayMode = "estimated", maximumPracticalFlavorCount = PublishedMenuContract.DefaultPracticalFlavorLimit } });
    }

    private static async Task<IResult> GetAvailability(long? catalogVersion, HttpRequest request, HttpResponse response, ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct)
    {
        var validation = await ValidateDevice(principal, db, ct); if (validation.Error is not null) return validation.Error; var tenant = validation.Device!.EstablishmentId!.Value; var state = await db.Set<CatalogState>().SingleOrDefaultAsync(x => x.EstablishmentId == tenant, ct); if (state?.CurrentPublishedRevisionId is null) return Error(404, "CATALOG_NOT_PUBLISHED", "Não existe catálogo publicado."); if (catalogVersion is not null && catalogVersion != state.CatalogVersion) return Error(409, "CATALOG_VERSION_MISMATCH", "A revisão estrutural mudou."); var etag = PublishedMenuContract.AvailabilityETag(state.AvailabilityVersion); response.Headers.ETag = etag; response.Headers.CacheControl = "private, no-cache"; if (request.Headers.IfNoneMatch.Any(value => value == etag)) return Results.StatusCode(StatusCodes.Status304NotModified); return Results.Ok(await Availability(db, tenant, state, ct));
    }

    private static Task<IResult> GetProduct(Guid productId, ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct) => GetConfiguration(productId, false, principal, db, ct);
    private static Task<IResult> GetCombo(Guid productId, ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct) => GetConfiguration(productId, true, principal, db, ct);

    private static async Task<IResult> GetConfiguration(Guid productId, bool comboOnly, ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct)
    {
        var validation = await ValidateDevice(principal, db, ct); if (validation.Error is not null) return validation.Error; var tenant = validation.Device!.EstablishmentId!.Value; var published = await Published(db, tenant, ct); if (published is null) return Error(404, "CATALOG_NOT_PUBLISHED", "Não existe catálogo publicado."); var (state, revision) = published.Value; using var snapshot = JsonDocument.Parse(revision.Snapshot); var product = FindById(snapshot.RootElement, "products", productId); if (product is null) return Error(404, "PRODUCT_NOT_PUBLISHED", "Produto não pertence à revisão publicada."); if (comboOnly && (!TryProperty(product.Value, "productType", out var type) || type.GetString() != "combo")) return Error(404, "PRODUCT_NOT_PUBLISHED", "Combo não pertence à revisão publicada."); var configuration = BuildProductConfiguration(snapshot.RootElement, product.Value, productId); return Results.Ok(new { schemaVersion = PublishedMenuContract.SchemaVersion, catalogVersion = state.CatalogVersion, availabilityVersion = state.AvailabilityVersion, configurationVersion = SemanticConfigurationHash.Compute(configuration), configuration });
    }

    private static async Task<IResult> GetMediaContent(Guid assetId, HttpRequest request, HttpResponse response, ClaimsPrincipal principal, AppizzaDbContext db, IObjectStorage storage, CancellationToken ct)
    {
        var validation = await ValidateDevice(principal, db, ct); if (validation.Error is not null) return validation.Error; var tenant = validation.Device!.EstablishmentId!.Value; var published = await Published(db, tenant, ct); if (published is null) return Error(404, "MEDIA_ASSET_NOT_PUBLISHED", "Mídia não pertence ao menu publicado."); using var snapshot = JsonDocument.Parse(published.Value.Revision.Snapshot); if (!ReferencedMediaIds(snapshot.RootElement).Contains(assetId)) return Error(404, "MEDIA_ASSET_NOT_PUBLISHED", "Mídia não pertence ao menu publicado."); var asset = await db.Set<MediaAsset>().SingleOrDefaultAsync(x => x.Id == assetId && x.EstablishmentId == tenant && x.Status == "ready", ct); if (asset is null) return Error(404, "MEDIA_ASSET_NOT_FOUND", "Mídia indisponível."); var etag = $"\"sha256-{asset.ChecksumSha256}\""; response.Headers.ETag = etag; response.Headers.CacheControl = "private, no-cache"; if (request.Headers.IfNoneMatch.Any(value => value == etag)) return Results.StatusCode(StatusCodes.Status304NotModified); await using var stored = await storage.GetAsync(asset.ObjectKey, ct); var content = new byte[stored.ContentLength]; await stored.Content.ReadExactlyAsync(content, ct); return Results.File(content, stored.ContentType, asset.FileName);
    }

    private static async Task<PublishedAvailability> Availability(AppizzaDbContext db, Guid tenant, CatalogState state, CancellationToken ct) => new(state.CatalogVersion, state.AvailabilityVersion, PublishedMenuContract.SchemaVersion,
        await db.Set<IngredientAvailability>().Where(x => x.EstablishmentId == tenant).OrderBy(x => x.IngredientId).Select(x => new PublishedAvailabilityItem(x.IngredientId, x.ExplicitlyAvailable, x.EffectivelyAvailable, x.Reason)).ToListAsync(ct),
        await db.Set<ProductAvailability>().Where(x => x.EstablishmentId == tenant).OrderBy(x => x.ProductId).Select(x => new PublishedAvailabilityItem(x.ProductId, x.ExplicitlyAvailable, x.EffectivelyAvailable, x.DerivedReason)).ToListAsync(ct),
        await db.Set<ProductVariantAvailability>().Where(x => x.EstablishmentId == tenant).OrderBy(x => x.ProductVariantId).Select(x => new PublishedAvailabilityItem(x.ProductVariantId, x.ExplicitlyAvailable, x.EffectivelyAvailable, x.DerivedReason)).ToListAsync(ct));

    private static async Task<(CatalogState State, CatalogRevision Revision)?> Published(AppizzaDbContext db, Guid tenant, CancellationToken ct)
    { var state = await db.Set<CatalogState>().AsNoTracking().SingleOrDefaultAsync(x => x.EstablishmentId == tenant, ct); if (state?.CurrentPublishedRevisionId is not Guid revisionId) return null; var revision = await db.Set<CatalogRevision>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == revisionId && x.EstablishmentId == tenant && x.Status == "published", ct); return revision is null ? null : (state, revision); }

    private static JsonElement BuildProductConfiguration(JsonElement root, JsonElement product, Guid productId)
    { using var document = JsonDocument.Parse(JsonSerializer.Serialize(new { product, variants = Filter(root, "variants", "productId", productId), productIngredients = Filter(root, "productIngredients", "productId", productId), customization = TryProperty(root, "customization", out var customization) ? customization : default, pizza = TryProperty(root, "pizza", out var pizza) ? pizza : default, combos = TryProperty(root, "combos", out var combos) ? combos : default })); return document.RootElement.Clone(); }
    private static List<JsonElement> Filter(JsonElement root, string collection, string property, Guid id) => TryProperty(root, collection, out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().Where(item => TryProperty(item, property, out var value) && Guid.TryParse(value.GetString(), out var parsed) && parsed == id).Select(item => item.Clone()).ToList() : [];
    private static JsonElement[] Elements(JsonElement root, string collection) => TryProperty(root, collection, out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().Select(x => x.Clone()).ToArray() : [];
    private static Guid GetGuid(JsonElement element, string property) => Guid.Parse(element.EnumerateObject().Single(x => x.Name.Equals(property, StringComparison.OrdinalIgnoreCase)).Value.GetString()!);
    private static JsonElement? FindById(JsonElement root, string collection, Guid id) { if (!TryProperty(root, collection, out var array) || array.ValueKind != JsonValueKind.Array) return null; foreach (var item in array.EnumerateArray()) if (TryProperty(item, "id", out var value) && Guid.TryParse(value.GetString(), out var parsed) && parsed == id) return item; return null; }
    private static HashSet<Guid> ReferencedMediaIds(JsonElement root) { var result = new HashSet<Guid>(); Visit(root, null, result); return result; }
    private static void Visit(JsonElement element, string? name, HashSet<Guid> result) { if (element.ValueKind == JsonValueKind.Object) foreach (var property in element.EnumerateObject()) Visit(property.Value, property.Name, result); else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Visit(item, name, result); else if (element.ValueKind == JsonValueKind.String && name?.Contains("media", StringComparison.OrdinalIgnoreCase) == true && Guid.TryParse(element.GetString(), out var id)) result.Add(id); }
    private static bool TryProperty(JsonElement element, string name, out JsonElement value) { if (element.ValueKind == JsonValueKind.Object) foreach (var property in element.EnumerateObject()) if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; } value = default; return false; }
    private static async Task<(Device? Device, IResult? Error)> ValidateDevice(ClaimsPrincipal principal, AppizzaDbContext db, CancellationToken ct) { if (!principal.IsTokenType("device")) return (null, Error(403, "INVALID_TOKEN_TYPE", "Token de dispositivo necessário.")); var id = principal.RequiredGuid("sub"); var tenant = principal.RequiredGuid("establishment_id"); var device = await db.Set<Device>().SingleOrDefaultAsync(x => x.Id == id && x.EstablishmentId == tenant, ct); if (device is null) return (null, Results.NotFound()); if (device.Status == "blocked") return (null, Error(403, "DEVICE_BLOCKED", "Dispositivo bloqueado.")); if (device.Status != "active" || device.CredentialVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) != principal.FindFirstValue("credential_version")) return (null, Error(403, "DEVICE_CREDENTIAL_REVOKED", "Credencial revogada.")); return (device, null); }
    private static IResult Error(int status, string code, string detail) => Results.Problem(statusCode: status, title: detail, detail: detail, extensions: new Dictionary<string, object?> { ["errorCode"] = code });
}
