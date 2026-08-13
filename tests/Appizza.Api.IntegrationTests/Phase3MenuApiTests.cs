using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Appizza.Modules.Catalog;
using Appizza.Modules.Media;
using Appizza.Persistence;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;

namespace Appizza.Api.IntegrationTests;

[Collection(Phase1ApiCollection.Name)]
public sealed class Phase3MenuApiTests(Phase1ApiFixture fixture)
{
    [Fact]
    public async Task MenuWithoutPublishedRevisionIsSafeNotFound()
    {
        var tenant = await fixture.CreateTenantAsync(1, 1); var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]);
        var response = await fixture.GetAsync("api/v1/table-device/menu", device.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.Equal("CATALOG_NOT_PUBLISHED", await fixture.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task SemanticETagReturns304AndAvailabilityHasIndependentETag()
    {
        var context = await CreatePublishedMenuAsync(); var first = await fixture.GetAsync("api/v1/table-device/menu", context.DeviceToken); first.EnsureSuccessStatusCode();
        Assert.Equal("\"catalog-1-availability-0-schema-1\"", first.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.NotModified, (await fixture.GetConditionalAsync("api/v1/table-device/menu", context.DeviceToken, first.Headers.ETag.Tag)).StatusCode);
        var availability = await fixture.GetAsync("api/v1/table-device/menu/availability?catalogVersion=1", context.DeviceToken); availability.EnsureSuccessStatusCode(); Assert.Equal("\"availability-0-schema-1\"", availability.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task AvailabilityChangeDoesNotRequireNewCatalogVersion()
    {
        var context = await CreatePublishedMenuAsync(); var change = await fixture.PostAsync($"api/v1/operations/catalog/availability/products/{context.ProductId}/change", new { available = false, reasonCode = "out_of_stock" }, context.UserToken, true); change.EnsureSuccessStatusCode();
        var availability = await fixture.GetAsync("api/v1/table-device/menu/availability?catalogVersion=1", context.DeviceToken); availability.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await availability.Content.ReadAsStringAsync()); Assert.Equal(1, json.RootElement.GetProperty("catalogVersion").GetInt64()); Assert.Equal(1, json.RootElement.GetProperty("availabilityVersion").GetInt64()); Assert.False(json.RootElement.GetProperty("products")[0].GetProperty("effectiveAvailable").GetBoolean());
    }

    [Fact]
    public async Task DeviceCannotReadOtherTenantProductThroughPublishedMenu()
    {
        var first = await CreatePublishedMenuAsync(); var second = await CreatePublishedMenuAsync();
        var response = await fixture.GetAsync($"api/v1/table-device/menu/products/{second.ProductId}", first.DeviceToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Assert.Equal("PRODUCT_NOT_PUBLISHED", await fixture.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task AuthenticatedMediaContentUsesPublishedOwnershipAndRealSeaweedFs()
    {
        var endpoint = Environment.GetEnvironmentVariable("APPIZZA_TEST_OBJECT_STORAGE_ENDPOINT"); if (string.IsNullOrWhiteSpace(endpoint)) return;
        var tenant = await fixture.CreateTenantAsync(1, 1); byte[] content = [137, 80, 78, 71, 13, 10, 26, 10]; var checksum = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var creation = await fixture.PostAsync("api/v1/operations/media/assets", new { fileName = "menu.png", mimeType = "image/png", fileSize = content.Length, checksumSha256 = checksum }, tenant.AccessToken); creation.EnsureSuccessStatusCode(); using var created = JsonDocument.Parse(await creation.Content.ReadAsStringAsync()); var assetId = created.RootElement.GetProperty("id").GetGuid(); (await fixture.PutContentAsync($"api/v1/operations/media/assets/{assetId}/content", content, "image/png", tenant.AccessToken)).EnsureSuccessStatusCode();
        var product = await fixture.PostAsync("api/v1/operations/catalog/products", new { productType = "simple", name = "Água", description = (string?)null, internalCode = Guid.NewGuid().ToString("N"), primaryCategoryId = (Guid?)null, primaryImageMediaId = assetId, displayOrder = 1, requiresProduction = false, allowsNotes = false, maximumNoteLength = (int?)null, preparationStationId = (Guid?)null }, tenant.AccessToken); product.EnsureSuccessStatusCode(); (await fixture.PostAsync("api/v1/operations/catalog/publish", null, tenant.AccessToken, true)).EnsureSuccessStatusCode(); var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]);
        var download = await fixture.GetAsync($"api/v1/table-device/media-assets/{assetId}/content", device.AccessToken); download.EnsureSuccessStatusCode(); Assert.Equal(content, await download.Content.ReadAsByteArrayAsync()); var other = await CreatePublishedMenuAsync(); Assert.Equal(HttpStatusCode.NotFound, (await fixture.GetAsync($"api/v1/table-device/media-assets/{assetId}/content", other.DeviceToken)).StatusCode);
        await using var db = fixture.CreateDbContext(); var objectKey = await db.Set<MediaAsset>().Where(x => x.Id == assetId).Select(x => x.ObjectKey).SingleAsync(); using var storage = new S3ObjectStorage(new ObjectStorageOptions { Endpoint = endpoint, Bucket = Required("APPIZZA_TEST_OBJECT_STORAGE_BUCKET"), AccessKey = Required("APPIZZA_TEST_OBJECT_STORAGE_ACCESS_KEY"), SecretKey = Required("APPIZZA_TEST_OBJECT_STORAGE_SECRET_KEY"), UsePathStyle = true }); await storage.DeleteAsync(objectKey, CancellationToken.None); await Assert.ThrowsAsync<NoSuchKeyException>(() => storage.GetAsync(objectKey, CancellationToken.None));
    }

    private async Task<PublishedContext> CreatePublishedMenuAsync()
    {
        var tenant = await fixture.CreateTenantAsync(1, 1); var productResponse = await fixture.PostAsync("api/v1/operations/catalog/products", new { productType = "simple", name = "Água", description = (string?)null, internalCode = Guid.NewGuid().ToString("N"), primaryCategoryId = (Guid?)null, primaryImageMediaId = (Guid?)null, displayOrder = 1, requiresProduction = false, allowsNotes = false, maximumNoteLength = (int?)null, preparationStationId = (Guid?)null }, tenant.AccessToken); productResponse.EnsureSuccessStatusCode(); using var productJson = JsonDocument.Parse(await productResponse.Content.ReadAsStringAsync()); var productId = productJson.RootElement.GetProperty("id").GetGuid(); var publish = await fixture.PostAsync("api/v1/operations/catalog/publish", null, tenant.AccessToken, true); publish.EnsureSuccessStatusCode(); var device = await fixture.RegisterAndBindAsync(tenant.AccessToken, tenant.TableIds[0]); return new PublishedContext(tenant.AccessToken, device.AccessToken, productId);
    }

    private sealed record PublishedContext(string UserToken, string DeviceToken, Guid ProductId);
    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} is required.");
}
