using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Appizza.Modules.Media;
using Appizza.Persistence;

namespace Appizza.Infrastructure.IntegrationTests;

#pragma warning disable CA1859 // The test intentionally exercises the IObjectStorage contract.

public sealed class ObjectStorageFoundationTests
{
    [Fact]
    public async Task S3CompatibleStorageRoundTripLeavesNoTestObject()
    {
        if (!IsEnabled())
        {
            return;
        }

        var options = ReadOptions();
        var clientConfiguration = new AmazonS3Config
        {
            ServiceURL = options.Endpoint,
            ForcePathStyle = options.UsePathStyle,
            AuthenticationRegion = "us-east-1"
        };

        using var client = new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            clientConfiguration);
        var buckets = await client.ListBucketsAsync(CancellationToken.None);
        if (!buckets.Buckets.Any(bucket => bucket.BucketName == options.Bucket))
        {
            await client.PutBucketAsync(
                new PutBucketRequest { BucketName = options.Bucket },
                CancellationToken.None);
        }

        using var concreteStorage = new S3ObjectStorage(options);
        IObjectStorage storage = concreteStorage;
        var objectKey = $"foundation-tests/{Guid.NewGuid():N}.txt";
        var expected = "appizza-foundation-object-storage";

        try
        {
            await using var upload = new MemoryStream(Encoding.UTF8.GetBytes(expected));
            await storage.PutAsync(objectKey, upload, "text/plain", CancellationToken.None);

            await using var stored = await storage.GetAsync(objectKey, CancellationToken.None);
            using var reader = new StreamReader(stored.Content, Encoding.UTF8);
            Assert.Equal(expected, await reader.ReadToEndAsync(CancellationToken.None));
            Assert.Equal("text/plain", stored.ContentType);
        }
        finally
        {
            await storage.DeleteAsync(objectKey, CancellationToken.None);
        }

        var exception = await Assert.ThrowsAsync<AmazonS3Exception>(() =>
            client.GetObjectMetadataAsync(options.Bucket, objectKey, CancellationToken.None));
        Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
    }

    private static bool IsEnabled() => string.Equals(
        Environment.GetEnvironmentVariable("APPIZZA_RUN_OBJECT_STORAGE_TESTS"),
        "true",
        StringComparison.OrdinalIgnoreCase);

    private static ObjectStorageOptions ReadOptions() => new()
    {
        Endpoint = Required("APPIZZA_OBJECT_STORAGE_ENDPOINT"),
        Bucket = Required("APPIZZA_OBJECT_STORAGE_BUCKET"),
        AccessKey = Required("APPIZZA_OBJECT_STORAGE_ACCESS_KEY"),
        SecretKey = Required("APPIZZA_OBJECT_STORAGE_SECRET_KEY"),
        UsePathStyle = true
    };

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} must be configured for the object storage test.");
}
