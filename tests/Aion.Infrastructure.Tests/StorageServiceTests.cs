using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aion.Infrastructure.Tests;

public class StorageServiceTests
{
    [Fact]
    public async Task Save_and_load_roundtrip()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var options = Options.Create(new StorageOptions
        {
            RootPath = tempRoot,
            EncryptionKey = new string('k', 32),
            EncryptPayloads = true
        });

        var service = new StorageService(options, NullLogger<StorageService>.Instance);

        await using var payload = new MemoryStream(Encoding.UTF8.GetBytes("hello world"));
        var descriptor = await service.SaveAsync("sample.txt", payload);

        await using var stream = await service.OpenReadAsync(descriptor.Path, descriptor.Sha256);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var content = await reader.ReadToEndAsync();

        Assert.Equal("hello world", content);
        Assert.Equal(payload.Length, descriptor.Size);
        Assert.False(Path.IsPathRooted(descriptor.Path));
    }

    [Fact]
    public async Task OpenReadAsync_detects_integrity_issues()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var options = Options.Create(new StorageOptions
        {
            RootPath = tempRoot,
            EncryptionKey = new string('k', 32),
            EncryptPayloads = false,
            RequireIntegrityCheck = true
        });

        var service = new StorageService(options, NullLogger<StorageService>.Instance);
        var descriptor = await service.SaveAsync("sample.bin", new MemoryStream(new byte[] { 1, 2, 3 }));

        var fullPath = Path.Combine(tempRoot, descriptor.Path);
        var bytes = await File.ReadAllBytesAsync(fullPath);
        bytes[0] ^= 0xFF;
        await File.WriteAllBytesAsync(fullPath, bytes);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.OpenReadAsync(descriptor.Path, descriptor.Sha256));
    }
}
