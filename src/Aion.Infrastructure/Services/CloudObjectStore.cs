using System;

namespace Aion.Infrastructure.Services;

public sealed record CloudObjectInfo(string Key, long Size, DateTimeOffset LastModified);

public interface ICloudObjectStore
{
    Task UploadObjectAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);
    Task DownloadObjectAsync(string key, Stream destination, CancellationToken cancellationToken);
    Task DeleteObjectAsync(string key, CancellationToken cancellationToken);
    Task<IReadOnlyList<CloudObjectInfo>> ListObjectsAsync(string prefix, CancellationToken cancellationToken);
}
