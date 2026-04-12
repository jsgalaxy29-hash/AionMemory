using System.Diagnostics.CodeAnalysis;

namespace Aion.Domain;

public sealed record StorageDescriptor(
    string Path,
    string Sha256,
    long Size);

public interface IStorageService
{
    Task<StorageDescriptor> SaveAsync(string logicalName, Stream content, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(
        string storagePath,
        [StringSyntax(StringSyntaxAttribute.Regex, @"^[A-Fa-f0-9]{64}$")]
        string? expectedSha256 = null,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
}
