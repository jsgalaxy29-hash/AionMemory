using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aion.Infrastructure.Tests;

public class CloudBackupServiceTests
{
    [Fact]
    public async Task Backup_and_restore_empty_database()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var databasePath = Path.Combine(tempRoot, "aion.db");
        var storageRoot = Path.Combine(tempRoot, "storage");
        var backupFolder = Path.Combine(tempRoot, "backups");
        Directory.CreateDirectory(storageRoot);
        Directory.CreateDirectory(backupFolder);

        var dbOptions = Options.Create(new AionDatabaseOptions
        {
            ConnectionString = $"Data Source={databasePath}",
            EncryptionKey = new string('k', 32)
        });
        var storageOptions = Options.Create(new StorageOptions
        {
            RootPath = storageRoot,
            EncryptPayloads = false
        });
        var backupOptions = Options.Create(new BackupOptions
        {
            BackupFolder = backupFolder,
            RequireIntegrityCheck = true
        });
        var cloudOptions = Options.Create(new CloudBackupOptions
        {
            Enabled = true,
            Bucket = "bucket",
            Endpoint = "https://example.com",
            AccessKeyId = "access",
            SecretAccessKey = "secret",
            Prefix = "tests"
        });

        await using (var connection = new SqliteConnection(dbOptions.Value.ConnectionString))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS sample(id INTEGER PRIMARY KEY, value TEXT);";
            await command.ExecuteNonQueryAsync();
        }

        var backupService = new BackupService(
            dbOptions,
            backupOptions,
            storageOptions,
            NullLogger<BackupService>.Instance,
            new NullSecurityAuditService());
        var store = new InMemoryCloudObjectStore();
        var cloudService = new AionCloudBackupService(
            backupOptions,
            cloudOptions,
            dbOptions,
            storageOptions,
            backupService,
            store,
            NullLogger<AionCloudBackupService>.Instance,
            new NullSecurityAuditService());

        var entry = await cloudService.CreateBackupAsync();
        var restorePath = Path.Combine(tempRoot, "restore.db");
        var result = await cloudService.RestoreAsync(entry.Id, restorePath, dryRun: false);

        Assert.True(File.Exists(restorePath));
        Assert.True(result.Applied);
    }

    [Fact]
    public async Task Restore_refuses_invalid_checksum()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var databasePath = Path.Combine(tempRoot, "aion.db");
        var storageRoot = Path.Combine(tempRoot, "storage");
        var backupFolder = Path.Combine(tempRoot, "backups");
        Directory.CreateDirectory(storageRoot);
        Directory.CreateDirectory(backupFolder);

        var dbOptions = Options.Create(new AionDatabaseOptions
        {
            ConnectionString = $"Data Source={databasePath}",
            EncryptionKey = new string('k', 32)
        });
        var storageOptions = Options.Create(new StorageOptions
        {
            RootPath = storageRoot,
            EncryptPayloads = false
        });
        var backupOptions = Options.Create(new BackupOptions
        {
            BackupFolder = backupFolder,
            RequireIntegrityCheck = true
        });
        var cloudOptions = Options.Create(new CloudBackupOptions
        {
            Enabled = true,
            Bucket = "bucket",
            Endpoint = "https://example.com",
            AccessKeyId = "access",
            SecretAccessKey = "secret",
            Prefix = "tests"
        });

        await using (var connection = new SqliteConnection(dbOptions.Value.ConnectionString))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS sample(id INTEGER PRIMARY KEY, value TEXT);";
            await command.ExecuteNonQueryAsync();
        }

        var backupService = new BackupService(
            dbOptions,
            backupOptions,
            storageOptions,
            NullLogger<BackupService>.Instance,
            new NullSecurityAuditService());
        var store = new InMemoryCloudObjectStore();
        var cloudService = new AionCloudBackupService(
            backupOptions,
            cloudOptions,
            dbOptions,
            storageOptions,
            backupService,
            store,
            NullLogger<AionCloudBackupService>.Instance,
            new NullSecurityAuditService());

        var entry = await cloudService.CreateBackupAsync();
        store.Tamper($"{cloudOptions.Value.Prefix}/{entry.Id}.zip.enc");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cloudService.RestoreAsync(entry.Id, Path.Combine(tempRoot, "restore.db"), dryRun: false));
    }

    private sealed class InMemoryCloudObjectStore : ICloudObjectStore
    {
        private readonly Dictionary<string, (byte[] Data, DateTimeOffset Modified)> _objects = new(StringComparer.Ordinal);

        public Task UploadObjectAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
        {
            using var memory = new MemoryStream();
            content.CopyTo(memory);
            _objects[key] = (memory.ToArray(), DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }

        public Task DownloadObjectAsync(string key, Stream destination, CancellationToken cancellationToken)
        {
            if (!_objects.TryGetValue(key, out var payload))
            {
                throw new FileNotFoundException("Object not found", key);
            }

            destination.Write(payload.Data, 0, payload.Data.Length);
            return Task.CompletedTask;
        }

        public Task DeleteObjectAsync(string key, CancellationToken cancellationToken)
        {
            _objects.Remove(key);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CloudObjectInfo>> ListObjectsAsync(string prefix, CancellationToken cancellationToken)
        {
            var list = _objects
                .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(kvp => new CloudObjectInfo(kvp.Key, kvp.Value.Data.Length, kvp.Value.Modified))
                .ToList();
            return Task.FromResult<IReadOnlyList<CloudObjectInfo>>(list);
        }

        public void Tamper(string key)
        {
            if (_objects.TryGetValue(key, out var payload))
            {
                var data = payload.Data.ToArray();
                data[0] ^= 0xFF;
                _objects[key] = (data, DateTimeOffset.UtcNow);
            }
        }
    }
}
