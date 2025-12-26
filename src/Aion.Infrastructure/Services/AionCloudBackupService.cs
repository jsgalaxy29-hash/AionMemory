using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Aion.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class AionCloudBackupService : ICloudBackupService
{
    private const string BackupManifestEntryName = "backup-manifest.json";
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly BackupOptions _backupOptions;
    private readonly CloudBackupOptions _cloudOptions;
    private readonly AionDatabaseOptions _databaseOptions;
    private readonly StorageOptions _storageOptions;
    private readonly IBackupService _backupService;
    private readonly ICloudObjectStore _objectStore;
    private readonly ILogger<AionCloudBackupService> _logger;
    private readonly ISecurityAuditService _securityAudit;
    private readonly byte[] _encryptionKey;

    public AionCloudBackupService(
        IOptions<BackupOptions> backupOptions,
        IOptions<CloudBackupOptions> cloudOptions,
        IOptions<AionDatabaseOptions> databaseOptions,
        IOptions<StorageOptions> storageOptions,
        IBackupService backupService,
        ICloudObjectStore objectStore,
        ILogger<AionCloudBackupService> logger,
        ISecurityAuditService securityAudit)
    {
        _backupOptions = backupOptions.Value;
        _cloudOptions = cloudOptions.Value;
        _databaseOptions = databaseOptions.Value;
        _storageOptions = storageOptions.Value;
        _backupService = backupService;
        _objectStore = objectStore;
        _logger = logger;
        _securityAudit = securityAudit;
        _encryptionKey = BackupService.DeriveKey(_databaseOptions.EncryptionKey);
    }

    public async Task<CloudBackupEntry> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        var manifest = await _backupService.CreateBackupAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var snapshotName = Path.GetDirectoryName(manifest.FileName)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(snapshotName))
        {
            throw new InvalidOperationException("Backup manifest snapshot name could not be determined.");
        }

        var backupFolder = _backupOptions.BackupFolder ?? throw new InvalidOperationException("Backup folder must be configured.");
        var snapshotRoot = Path.Combine(backupFolder, snapshotName);
        var manifestPath = Path.Combine(backupFolder, $"{snapshotName}.json");
        var tempRoot = Path.Combine(backupFolder, $"cloud-temp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var zipPath = Path.Combine(tempRoot, $"cloud-{snapshotName}.zip");
            await CreateArchiveAsync(zipPath, manifest, snapshotRoot, manifestPath, cancellationToken).ConfigureAwait(false);

            var encryptedPath = zipPath + ".enc";
            await EncryptArchiveAsync(zipPath, encryptedPath, cancellationToken).ConfigureAwait(false);

            var checksum = await BackupService.ComputeHashAsync(encryptedPath, cancellationToken).ConfigureAwait(false);
            var entry = new CloudBackupEntry
            {
                Id = snapshotName,
                FileName = Path.GetFileName(encryptedPath),
                CreatedAt = manifest.CreatedAt,
                Size = new FileInfo(encryptedPath).Length,
                Sha256 = checksum
            };

            var archiveKey = BuildObjectKey($"{entry.Id}.zip.enc");
            var manifestKey = BuildObjectKey($"{entry.Id}.manifest.json");

            await using (var archiveStream = new FileStream(encryptedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
            {
                await _objectStore.UploadObjectAsync(archiveKey, archiveStream, "application/octet-stream", cancellationToken).ConfigureAwait(false);
            }

            await using (var manifestStream = new MemoryStream())
            {
                await JsonSerializer.SerializeAsync(manifestStream, entry, ManifestSerializerOptions, cancellationToken).ConfigureAwait(false);
                manifestStream.Position = 0;
                await _objectStore.UploadObjectAsync(manifestKey, manifestStream, "application/json", cancellationToken).ConfigureAwait(false);
            }

            await EnforceRetentionAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Cloud backup {BackupId} uploaded ({Size} bytes).", entry.Id, entry.Size);
            await _securityAudit.LogAsync(new SecurityAuditEvent(
                SecurityAuditCategory.Backup,
                "cloud.backup.created",
                metadata: new Dictionary<string, object?>
                {
                    ["backupId"] = entry.Id,
                    ["size"] = entry.Size
                }), cancellationToken).ConfigureAwait(false);

            return entry;
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    public async Task<IReadOnlyList<CloudBackupEntry>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        var prefix = BuildObjectKey(string.Empty);
        var objects = await _objectStore.ListObjectsAsync(prefix, cancellationToken).ConfigureAwait(false);
        var manifests = objects.Where(o => o.Key.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase)).ToList();
        var results = new List<CloudBackupEntry>();

        foreach (var manifest in manifests)
        {
            await using var manifestStream = new MemoryStream();
            await _objectStore.DownloadObjectAsync(manifest.Key, manifestStream, cancellationToken).ConfigureAwait(false);
            manifestStream.Position = 0;
            var entry = await JsonSerializer.DeserializeAsync<CloudBackupEntry>(manifestStream, ManifestSerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            if (entry is not null)
            {
                results.Add(entry);
            }
        }

        return results.OrderByDescending(e => e.CreatedAt).ToList();
    }

    public async Task<CloudRestorePreview> RestoreLatestAsync(string destinationPath, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        var backups = await ListBackupsAsync(cancellationToken).ConfigureAwait(false);
        var latest = backups.OrderByDescending(b => b.CreatedAt).FirstOrDefault();
        if (latest is null)
        {
            throw new FileNotFoundException("No cloud backups found.");
        }

        return await RestoreAsync(latest.Id, destinationPath, dryRun, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CloudRestorePreview> RestoreAsync(string backupId, string destinationPath, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        var entry = await GetBackupAsync(backupId, cancellationToken).ConfigureAwait(false);

        var tempRoot = Path.Combine(_backupOptions.BackupFolder ?? Path.GetTempPath(), $"cloud-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var encryptedPath = Path.Combine(tempRoot, "cloud.zip.enc");
        var zipPath = Path.Combine(tempRoot, "cloud.zip");

        try
        {
            var archiveKey = BuildObjectKey($"{entry.Id}.zip.enc");
            await using (var archiveStream = new FileStream(encryptedPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await _objectStore.DownloadObjectAsync(archiveKey, archiveStream, cancellationToken).ConfigureAwait(false);
            }

            var checksum = await BackupService.ComputeHashAsync(encryptedPath, cancellationToken).ConfigureAwait(false);
            if (_backupOptions.RequireIntegrityCheck &&
                !string.Equals(checksum, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Backup integrity check failed: hash mismatch");
            }

            await DecryptArchiveAsync(encryptedPath, zipPath, cancellationToken).ConfigureAwait(false);

            var entries = await ReadArchiveEntriesAsync(zipPath, cancellationToken).ConfigureAwait(false);
            if (dryRun)
            {
                return new CloudRestorePreview
                {
                    Backup = entry,
                    Entries = entries,
                    Applied = false
                };
            }

            var extractedRoot = Path.Combine(tempRoot, "extracted");
            Directory.CreateDirectory(extractedRoot);
            ZipFile.ExtractToDirectory(zipPath, extractedRoot);

            var manifestPath = Path.Combine(extractedRoot, BackupManifestEntryName);
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException("Backup manifest missing from archive.", manifestPath);
            }

            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestJson, BackupService.ManifestSerializerOptions)
                ?? throw new InvalidOperationException("Backup manifest could not be parsed.");

            await RestoreDatabaseAsync(manifest, extractedRoot, destinationPath, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(manifest.StorageArchivePath))
            {
                await RestoreStorageAsync(manifest, extractedRoot, cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Cloud backup {BackupId} restored to {Destination}", entry.Id, destinationPath);
            await _securityAudit.LogAsync(new SecurityAuditEvent(
                SecurityAuditCategory.Restore,
                "cloud.backup.restored",
                metadata: new Dictionary<string, object?>
                {
                    ["backupId"] = entry.Id,
                    ["destination"] = destinationPath,
                    ["storageRestored"] = !string.IsNullOrWhiteSpace(manifest.StorageArchivePath)
                }), cancellationToken).ConfigureAwait(false);

            return new CloudRestorePreview
            {
                Backup = entry,
                Entries = entries,
                Applied = true
            };
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private async Task<CloudBackupEntry> GetBackupAsync(string backupId, CancellationToken cancellationToken)
    {
        var manifestKey = BuildObjectKey($"{backupId}.manifest.json");
        await using var manifestStream = new MemoryStream();
        await _objectStore.DownloadObjectAsync(manifestKey, manifestStream, cancellationToken).ConfigureAwait(false);
        manifestStream.Position = 0;
        var entry = await JsonSerializer.DeserializeAsync<CloudBackupEntry>(manifestStream, ManifestSerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        if (entry is null)
        {
            throw new InvalidOperationException("Cloud backup manifest could not be read.");
        }

        return entry;
    }

    private static async Task CreateArchiveAsync(
        string zipPath,
        BackupManifest manifest,
        string snapshotRoot,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        await using var archiveStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true);
        AddFileToArchive(archive, Path.Combine(snapshotRoot, Path.GetFileName(manifest.FileName)), manifest.FileName);

        if (!string.IsNullOrWhiteSpace(manifest.StorageArchivePath))
        {
            var storagePath = Path.Combine(snapshotRoot, Path.GetFileName(manifest.StorageArchivePath));
            AddFileToArchive(archive, storagePath, manifest.StorageArchivePath);
        }

        AddFileToArchive(archive, manifestPath, BackupManifestEntryName);
        await archiveStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddFileToArchive(ZipArchive archive, string sourcePath, string entryName)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Expected backup file missing.", sourcePath);
        }

        var entry = archive.CreateEntry(entryName.Replace('\\', '/'), CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        sourceStream.CopyTo(entryStream);
    }

    private async Task EncryptArchiveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();
        await output.WriteAsync(aes.IV, cancellationToken).ConfigureAwait(false);

        await using var cryptoStream = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write);
        await input.CopyToAsync(cryptoStream, cancellationToken).ConfigureAwait(false);
        await cryptoStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DecryptArchiveAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        var iv = new byte[16];
        var read = await input.ReadAsync(iv, cancellationToken).ConfigureAwait(false);
        if (read != iv.Length)
        {
            throw new InvalidOperationException("Encrypted archive header missing.");
        }

        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.IV = iv;

        await using var cryptoStream = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read);
        await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await cryptoStream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> ReadArchiveEntriesAsync(string zipPath, CancellationToken cancellationToken)
    {
        var entries = new List<string>();
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            entries.Add(entry.FullName);
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return entries;
    }

    private async Task RestoreDatabaseAsync(BackupManifest manifest, string extractedRoot, string destinationPath, CancellationToken cancellationToken)
    {
        var sourcePath = Path.Combine(extractedRoot, manifest.FileName);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Backup database file missing.", sourcePath);
        }

        var tempPath = destinationPath + ".restoring";
        await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var dest = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
        }

        var computedHash = await BackupService.ComputeHashAsync(tempPath, cancellationToken).ConfigureAwait(false);
        if (_backupOptions.RequireIntegrityCheck &&
            !string.Equals(computedHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(tempPath);
            throw new InvalidOperationException("Backup integrity check failed: hash mismatch");
        }

        var backupExisting = destinationPath + ".bak";
        if (File.Exists(destinationPath))
        {
            File.Move(destinationPath, backupExisting, overwrite: true);
        }

        File.Move(tempPath, destinationPath, overwrite: true);
    }

    private async Task RestoreStorageAsync(BackupManifest manifest, string extractedRoot, CancellationToken cancellationToken)
    {
        var archivePath = Path.Combine(extractedRoot, manifest.StorageArchivePath!);
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("Storage archive missing", archivePath);
        }

        if (_backupOptions.RequireIntegrityCheck && !string.IsNullOrWhiteSpace(manifest.StorageSha256))
        {
            var archiveHash = await BackupService.ComputeHashAsync(archivePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(archiveHash, manifest.StorageSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Storage archive integrity check failed: hash mismatch");
            }
        }

        var destinationRoot = _storageOptions.RootPath ?? manifest.StorageRoot ?? throw new InvalidOperationException("Storage root path must be configured");
        var restoringRoot = destinationRoot + ".restoring";
        if (Directory.Exists(restoringRoot))
        {
            Directory.Delete(restoringRoot, recursive: true);
        }

        Directory.CreateDirectory(restoringRoot);
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            foreach (var entry in archive.Entries)
            {
                var targetPath = Path.Combine(restoringRoot, entry.FullName);
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                await using var entryStream = entry.Open();
                await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await entryStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }
        }

        var backupExistingRoot = destinationRoot + ".bak";
        if (Directory.Exists(backupExistingRoot))
        {
            Directory.Delete(backupExistingRoot, recursive: true);
        }

        if (Directory.Exists(destinationRoot))
        {
            Directory.Move(destinationRoot, backupExistingRoot);
        }

        Directory.Move(restoringRoot, destinationRoot);
    }

    private async Task EnforceRetentionAsync(CancellationToken cancellationToken)
    {
        var backups = (await ListBackupsAsync(cancellationToken).ConfigureAwait(false))
            .OrderByDescending(b => b.CreatedAt)
            .ToList();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Abs(_cloudOptions.RetentionDays));
        var removals = new List<CloudBackupEntry>();

        for (var i = 0; i < backups.Count; i++)
        {
            var backup = backups[i];
            if (backup.CreatedAt < cutoff || i >= _cloudOptions.MaxBackups)
            {
                removals.Add(backup);
            }
        }

        foreach (var backup in removals)
        {
            var archiveKey = BuildObjectKey($"{backup.Id}.zip.enc");
            var manifestKey = BuildObjectKey($"{backup.Id}.manifest.json");
            await _objectStore.DeleteObjectAsync(archiveKey, cancellationToken).ConfigureAwait(false);
            await _objectStore.DeleteObjectAsync(manifestKey, cancellationToken).ConfigureAwait(false);
        }
    }

    private string BuildObjectKey(string objectName)
    {
        var prefix = _cloudOptions.Prefix ?? "aion-backups";
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return $"{prefix.TrimEnd('/')}/";
        }

        return $"{prefix.TrimEnd('/')}/{objectName}";
    }

    private void EnsureEnabled()
    {
        if (!_cloudOptions.Enabled)
        {
            throw new InvalidOperationException("Cloud backups are not enabled.");
        }
    }
}
