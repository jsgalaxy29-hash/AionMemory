using System.Security.Cryptography;
using System.Text;
using Aion.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class StorageService : IStorageService
{
    private readonly string _rootPath;
    private readonly StorageOptions _options;
    private readonly ILogger<StorageService> _logger;
    private readonly byte[]? _encryptionKey;

    public StorageService(IOptions<StorageOptions> options, ILogger<StorageService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rootPath = _options.RootPath ?? throw new InvalidOperationException("Storage root path must be configured");

        if (_options.EncryptPayloads)
        {
            _encryptionKey = DeriveKey(_options.EncryptionKey ?? throw new InvalidOperationException("Storage encryption key missing"));
        }

        Directory.CreateDirectory(_rootPath);
    }

    public async Task<StorageDescriptor> SaveAsync(string logicalName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (buffer.Length > _options.MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"The payload exceeds the configured limit of {_options.MaxFileSizeBytes / (1024 * 1024)} MB");
        }

        buffer.Position = 0;
        var hash = ComputeHash(buffer);
        buffer.Position = 0;

        var relativePath = BuildRelativePath(logicalName);
        var fullPath = ResolveFullPath(relativePath);
        var directory = Path.GetDirectoryName(fullPath) ?? _rootPath;
        Directory.CreateDirectory(directory);

        var payload = _options.EncryptPayloads
            ? Encrypt(buffer.ToArray())
            : buffer.ToArray();

        await File.WriteAllBytesAsync(fullPath, payload, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Stored payload at {Path}", relativePath);
        return new StorageDescriptor(relativePath, hash, buffer.Length);
    }

    public async Task<Stream> OpenReadAsync(string storagePath, string? expectedSha256 = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);

        var fullPath = ResolveFullPath(storagePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Storage payload not found", fullPath);
        }

        var payload = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var plaintext = _options.EncryptPayloads ? Decrypt(payload) : payload;

        if (_options.RequireIntegrityCheck && !string.IsNullOrWhiteSpace(expectedSha256))
        {
            var computedHash = ComputeHash(new MemoryStream(plaintext, writable: false));
            if (!string.Equals(computedHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Storage integrity validation failed");
            }
        }

        return new MemoryStream(plaintext, writable: false);
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);

        var fullPath = ResolveFullPath(storagePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogDebug("Deleted payload {Path}", storagePath);
        }

        return Task.CompletedTask;
    }

    private string BuildRelativePath(string logicalName)
    {
        var safeName = Path.GetFileName(logicalName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "payload";
        }

        var extension = Path.GetExtension(safeName);
        var id = Guid.NewGuid().ToString("N");
        return Path.Combine("attachments", id[..2], id[2..4], $"{id}{extension}");
    }

    private string ResolveFullPath(string storagePath)
    {
        var rootedPath = Path.IsPathRooted(storagePath) ? storagePath : Path.Combine(_rootPath, storagePath);
        var fullPath = Path.GetFullPath(rootedPath);

        if (!fullPath.StartsWith(Path.GetFullPath(_rootPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid storage path outside of configured root");
        }

        return fullPath;
    }

    private byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        if (_encryptionKey is null)
        {
            throw new InvalidOperationException("Storage encryption key missing");
        }

        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_encryptionKey);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);
        return payload;
    }

    private byte[] Decrypt(ReadOnlySpan<byte> payload)
    {
        if (_encryptionKey is null)
        {
            throw new InvalidOperationException("Storage encryption key missing");
        }

        var nonce = payload[..12];
        var tag = payload[12..28];
        var cipher = payload[28..];
        var plaintext = new byte[cipher.Length];

        using var aes = new AesGcm(_encryptionKey);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return plaintext;
    }

    private static byte[] DeriveKey(string material)
    {
        try
        {
            return Convert.FromBase64String(material);
        }
        catch (FormatException)
        {
            return SHA256.HashData(Encoding.UTF8.GetBytes(material));
        }
    }

    private static string ComputeHash(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        return Convert.ToHexString(hash);
    }
}
