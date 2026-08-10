using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Appizza.Modules.Media;

namespace Appizza.Persistence;

public sealed class S3ObjectStorage : IObjectStorage, IDisposable
{
    private readonly string _bucket;
    private readonly AmazonS3Client _client;

    public S3ObjectStorage(ObjectStorageOptions options)
    {
        _bucket = options.Bucket;
        var configuration = new AmazonS3Config
        {
            ServiceURL = options.Endpoint,
            ForcePathStyle = options.UsePathStyle,
            AuthenticationRegion = "us-east-1"
        };
        _client = new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            configuration);
    }

    public async Task PutAsync(
        string objectKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType
        }, cancellationToken);
    }

    public async Task<StoredObject> GetAsync(string objectKey, CancellationToken cancellationToken)
    {
        var response = await _client.GetObjectAsync(_bucket, objectKey, cancellationToken);
        return new StoredObject(response.ResponseStream, response.Headers.ContentType, response.ContentLength);
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
        _client.DeleteObjectAsync(_bucket, objectKey, cancellationToken);

    public void Dispose() => _client.Dispose();
}
