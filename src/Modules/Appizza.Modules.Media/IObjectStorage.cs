namespace Appizza.Modules.Media;

public interface IObjectStorage
{
    Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken);
    Task<StoredObject> GetAsync(string objectKey, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed record StoredObject(Stream Content, string ContentType, long ContentLength) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
