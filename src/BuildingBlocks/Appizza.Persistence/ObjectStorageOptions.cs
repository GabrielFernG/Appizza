namespace Appizza.Persistence;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    public required string Endpoint { get; init; }
    public required string Bucket { get; init; }
    public required string AccessKey { get; init; }
    public required string SecretKey { get; init; }
    public bool UsePathStyle { get; init; } = true;
}
