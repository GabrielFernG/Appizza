using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using Appizza.Modules.Catalog;
using Appizza.Modules.Media;
using Appizza.Persistence;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase2CatalogApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task CatalogResourcesAreIsolatedByEstablishment()
    {
        var first = await fixture.CreateTenantAsync(1, 1);
        var second = await fixture.CreateTenantAsync(1, 1);
        var created = await fixture.PostAsync("api/v1/operations/catalog/categories", new { name = "Pizzas", description = (string?)null, parentCategoryId = (Guid?)null, imageMediaId = (Guid?)null, displayOrder = 1 }, second.AccessToken);
        created.EnsureSuccessStatusCode();

        using var list = await fixture.GetJsonAsync("api/v1/operations/catalog/categories", first.AccessToken);
        Assert.Empty(list.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task ConcurrentPublicationCreatesOneVersionAndOneOutboxEvent()
    {
        var tenant = await fixture.CreateTenantAsync(1, 1);
        var category = await fixture.PostAsync("api/v1/operations/catalog/categories", new { name = "Bebidas", description = (string?)null, parentCategoryId = (Guid?)null, imageMediaId = (Guid?)null, displayOrder = 1 }, tenant.AccessToken);
        category.EnsureSuccessStatusCode();
        var product = await fixture.PostAsync("api/v1/operations/catalog/products", new { productType = "simple", name = "Água", description = (string?)null, internalCode = "AGUA", primaryCategoryId = (Guid?)null, primaryImageMediaId = (Guid?)null, displayOrder = 1, requiresProduction = false, allowsNotes = false, maximumNoteLength = (int?)null, preparationStationId = (Guid?)null }, tenant.AccessToken);
        product.EnsureSuccessStatusCode();

        var responses = await fixture.ConcurrentAsync(
            () => fixture.PostAsync("api/v1/operations/catalog/publish", null, tenant.AccessToken, true),
            () => fixture.PostAsync("api/v1/operations/catalog/publish", null, tenant.AccessToken, true));

        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        var noChanges = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("CATALOG_NO_CHANGES_TO_PUBLISH", await fixture.ErrorCodeAsync(noChanges));
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Set<CatalogRevision>().CountAsync(x => x.EstablishmentId == tenant.EstablishmentId && x.Status == "published"));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EstablishmentId == tenant.EstablishmentId && x.EventType == "catalog-published.v1"));
        Assert.Equal(1, await db.Set<CatalogState>().Where(x => x.EstablishmentId == tenant.EstablishmentId).Select(x => x.CatalogVersion).SingleAsync());
    }

    [Fact]
    public async Task RequiredIngredientAvailabilityIsDerivedAndNoOpDoesNotIncrementVersion()
    {
        var tenant = await fixture.CreateTenantAsync(1, 1);
        Guid productId; Guid ingredientId;
        await using (var db = fixture.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow; productId = Guid.NewGuid(); ingredientId = Guid.NewGuid();
            db.Add(new Product { Id = productId, EstablishmentId = tenant.EstablishmentId, ProductType = "simple", Name = "Produto", Status = "active", CreatedAt = now, UpdatedAt = now });
            db.Add(new Ingredient { Id = ingredientId, EstablishmentId = tenant.EstablishmentId, Name = "Obrigatório", Status = "active", CreatedAt = now, UpdatedAt = now });
            db.Add(new ProductIngredient { Id = Guid.NewGuid(), ProductId = productId, IngredientId = ingredientId, IncludedByDefault = true, RequiredForRecipe = true, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }

        var changed = await fixture.PostAsync($"api/v1/operations/catalog/availability/ingredients/{ingredientId}/change", new { available = false, reasonCode = "out_of_stock" }, tenant.AccessToken, true);
        changed.EnsureSuccessStatusCode();
        var unchanged = await fixture.PostAsync($"api/v1/operations/catalog/availability/ingredients/{ingredientId}/change", new { available = false, reasonCode = "out_of_stock" }, tenant.AccessToken, true);
        unchanged.EnsureSuccessStatusCode();

        await using var verification = fixture.CreateDbContext();
        var state = await verification.Set<CatalogState>().SingleAsync(x => x.EstablishmentId == tenant.EstablishmentId);
        Assert.Equal(1, state.AvailabilityVersion);
        Assert.False(await verification.Set<ProductAvailability>().Where(x => x.ProductId == productId).Select(x => x.EffectivelyAvailable).SingleAsync());
    }

    [Fact]
    public async Task PublicationSnapshotDoesNotContainOperationalAvailability()
    {
        var tenant = await fixture.CreateTenantAsync(1, 1);
        var response = await fixture.PostAsync("api/v1/operations/catalog/publish", null, tenant.AccessToken, true);
        response.EnsureSuccessStatusCode();
        await using var db = fixture.CreateDbContext();
        var snapshot = await db.Set<CatalogRevision>().Where(x => x.EstablishmentId == tenant.EstablishmentId && x.Status == "published").Select(x => x.Snapshot).SingleAsync();
        Assert.DoesNotContain("availability", snapshot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MediaApiUploadsReadsAndIsolatesAssetAgainstRealSeaweedFs()
    {
        var endpoint = Environment.GetEnvironmentVariable("APPIZZA_TEST_OBJECT_STORAGE_ENDPOINT"); if (string.IsNullOrWhiteSpace(endpoint)) return;
        var tenant = await fixture.CreateTenantAsync(1, 1); var other = await fixture.CreateTenantAsync(1, 1); byte[] content = [137, 80, 78, 71, 13, 10, 26, 10]; var checksum = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var creation = await fixture.PostAsync("api/v1/operations/media/assets", new { fileName = "phase2.png", mimeType = "image/png", fileSize = content.Length, checksumSha256 = checksum }, tenant.AccessToken); creation.EnsureSuccessStatusCode(); using var created = JsonDocument.Parse(await creation.Content.ReadAsStringAsync()); var assetId = created.RootElement.GetProperty("id").GetGuid();
        var upload = await fixture.PutContentAsync($"api/v1/operations/media/assets/{assetId}/content", content, "image/png", tenant.AccessToken); upload.EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.NotFound, (await fixture.GetAsync($"api/v1/operations/media/assets/{assetId}", other.AccessToken)).StatusCode);
        var download = await fixture.GetAsync($"api/v1/operations/media/assets/{assetId}/content", tenant.AccessToken); download.EnsureSuccessStatusCode(); Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());
        await using var db = fixture.CreateDbContext(); var asset = await db.Set<MediaAsset>().SingleAsync(x => x.Id == assetId); Assert.Equal("ready", asset.Status);
        using var storage = new S3ObjectStorage(new ObjectStorageOptions { Endpoint = endpoint, Bucket = RequiredEnvironment("APPIZZA_TEST_OBJECT_STORAGE_BUCKET"), AccessKey = RequiredEnvironment("APPIZZA_TEST_OBJECT_STORAGE_ACCESS_KEY"), SecretKey = RequiredEnvironment("APPIZZA_TEST_OBJECT_STORAGE_SECRET_KEY"), UsePathStyle = true }); await storage.DeleteAsync(asset.ObjectKey, CancellationToken.None); await Assert.ThrowsAsync<NoSuchKeyException>(() => storage.GetAsync(asset.ObjectKey, CancellationToken.None));
    }

    private static string RequiredEnvironment(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} is required.");
}
