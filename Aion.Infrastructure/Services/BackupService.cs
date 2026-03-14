using System.Collections.Generic;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Aion.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class BackupService : IBackupService
{
    internal static readonly JsonSerializerOptions ManifestSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AionDatabaseOptions _databaseOptions;
    private readonly BackupOptions _backupOptions;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<BackupService> _logger;
    private readonly ISecurityAuditService _securityAudit;
    private readonly byte[] _encryptionKey;
    private readonly string _storageRoot;

    public BackupService(
        IOptions<AionDatabaseOptions> databaseOptions,
        IOptions<BackupOptions> backupOptions,
        IOptions<StorageOptions> storageOptions,
        ILogger<BackupService> logger,
        ISecurityAuditService securityAudit)
    {
        _databaseOptions = databaseOptions.Value;
        _backupOptions = backupOptions.Value;
        _storageOptions = storageOptions.Value;
        _logger = logger;
        _securityAudit = securityAudit;
        _encryptionKey = DeriveKey(_databaseOptions.EncryptionKey);
        _storageRoot = _storageOptions.RootPath ?? throw new InvalidOperationException("Storage root path must be configured");

        if (string.IsNullOrWhiteSpace(_backupOptions.BackupFolder))
        {
            throw new InvalidOperationException("Backup folder must be configured");
        }

        Directory.CreateDirectory(_backupOptions.BackupFolder);
    }

    public async Task<BackupManifest> CreateBackupAsync(bool encrypt = false, CancellationToken cancellationToken = default)
    {
        var databasePath = ResolveDatabasePath(_databaseOptions.ConnectionString);
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("Database file not found", databasePath);
        }

        var databaseInfo = new FileInfo(databasePath);
        if (databaseInfo.Length > _backupOptions.MaxDatabaseSizeBytes)
        {
            throw new InvalidOperationException($"Database exceeds the maximum backup size of {_backupOptions.MaxDatabaseSizeBytes / (1024 * 1024)} MB");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var snapshotName = $"snapshot-{timestamp:yyyyMMddHHmmss}";
        var snapshotFolder = Path.Combine(_backupOptions.BackupFolder!, snapshotName);
        Directory.CreateDirectory(snapshotFolder);

        var extension = encrypt ? ".db.enc" : ".db";
        var databaseFileName = $"database{extension}";
        var destination = Path.Combine(snapshotFolder, databaseFileName);

        if (encrypt)
        {
            var data = await File.ReadAllBytesAsync(databasePath, cancellationToken).ConfigureAwait(false);
            var encrypted = Encrypt(data);
            await File.WriteAllBytesAsync(destination, encrypted, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await using var source = new FileStream(
                databasePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var dest = new FileStream(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
        }

        var storageArchiveName = "storage.zip";
        var storageArchivePath = Path.Combine(snapshotFolder, storageArchiveName);
        await CreateStorageArchiveAsync(storageArchivePath, cancellationToken).ConfigureAwait(false);

        var manifest = new BackupManifest
        {
            FileName = Path.Combine(snapshotName, databaseFileName),
            Size = new FileInfo(destination).Length,
            CreatedAt = timestamp,
            Sha256 = await ComputeHashAsync(destination, cancellationToken).ConfigureAwait(false),
            SourcePath = databasePath,
            IsEncrypted = encrypt,
            StorageArchivePath = Path.Combine(snapshotName, storageArchiveName),
            StorageSize = new FileInfo(storageArchivePath).Length,
            StorageSha256 = await ComputeHashAsync(storageArchivePath, cancellationToken).ConfigureAwait(false),
            StorageRoot = _storageRoot
        };

        var manifestPath = Path.Combine(_backupOptions.BackupFolder!, $"{snapshotName}.json");
        await using (var manifestStream = new FileStream(
                       manifestPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(manifestStream, manifest, ManifestSerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogInformation("Backup created at {Destination} (encrypted={Encrypted})", destination, encrypt);
        await _securityAudit.LogAsync(new SecurityAuditEvent(
            SecurityAuditCategory.Backup,
            "backup.created",
            metadata: new Dictionary<string, object?>
            {
                ["fileName"] = Path.GetFileName(manifest.FileName),
                ["snapshot"] = snapshotName,
                ["encrypted"] = manifest.IsEncrypted,
                ["size"] = manifest.Size,
                ["storageSize"] = manifest.StorageSize
            }), cancellationToken).ConfigureAwait(false);
        return manifest;
    }

    private static string ResolveDatabasePath(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new InvalidOperationException("The SQLite data source must be configured");
        }

        return Path.GetFullPath(builder.DataSource);
    }

    private byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
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

    internal static byte[] DeriveKey(string material)
    {
        try
        {
            return Convert.FromBase64String(material);
        }
        catch (FormatException)
        {
            return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material));
        }
    }

    internal static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private async Task CreateStorageArchiveAsync(string archivePath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        await using var archiveStream = new FileStream(
            archivePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous);

        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true);
        var backupFolder = Path.GetFullPath(_backupOptions.BackupFolder!);
        var storageRoot = Path.GetFullPath(_storageRoot);

        if (!Directory.Exists(storageRoot))
        {
            Directory.CreateDirectory(storageRoot);
        }

        foreach (var file in Directory.EnumerateFiles(storageRoot, "*", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(file);
            if (fullPath.StartsWith(backupFolder, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var entryName = Path.GetRelativePath(storageRoot, fullPath).Replace('\\', '/');
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            await using var source = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var dest = entry.Open();
            await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
        }

        await archiveStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class RestoreService : IRestoreService
{
    private readonly BackupOptions _backupOptions;
    private readonly AionDatabaseOptions _databaseOptions;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<RestoreService> _logger;
    private readonly ISecurityAuditService _securityAudit;
    private readonly byte[] _encryptionKey;

    public RestoreService(
        IOptions<BackupOptions> backupOptions,
        IOptions<AionDatabaseOptions> databaseOptions,
        IOptions<StorageOptions> storageOptions,
        ILogger<RestoreService> logger,
        ISecurityAuditService securityAudit)
    {
        _backupOptions = backupOptions.Value;
        _databaseOptions = databaseOptions.Value;
        _storageOptions = storageOptions.Value;
        _logger = logger;
        _securityAudit = securityAudit;
        _encryptionKey = BackupService.DeriveKey(_databaseOptions.EncryptionKey);
    }

    public async Task RestoreLatestAsync(string? destinationPath = null, CancellationToken cancellationToken = default)
    {
        var manifest = LoadLatestManifest();
        if (manifest is null)
        {
            throw new FileNotFoundException("No backup manifest found", _backupOptions.BackupFolder);
        }

        var databaseBackupFile = Path.Combine(_backupOptions.BackupFolder!, manifest.FileName);
        if (!File.Exists(databaseBackupFile))
        {
            throw new FileNotFoundException("Backup file missing", databaseBackupFile);
        }

        var destination = destinationPath ?? ResolveDatabasePath(_databaseOptions.ConnectionString);
        var tempPath = destination + ".restoring";

        if (manifest.IsEncrypted || databaseBackupFile.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
        {
            var payload = await File.ReadAllBytesAsync(databaseBackupFile, cancellationToken).ConfigureAwait(false);
            var decrypted = Decrypt(payload);
            await File.WriteAllBytesAsync(tempPath, decrypted, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await using var source = new FileStream(
                databaseBackupFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var dest = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
        }

        var computedHash = await BackupService.ComputeHashAsync(tempPath, cancellationToken).ConfigureAwait(false);
        if (_backupOptions.RequireIntegrityCheck &&
            !string.Equals(computedHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(tempPath);
            throw new InvalidOperationException("Backup integrity check failed: hash mismatch");
        }

        var backupExisting = destination + ".bak";
        if (File.Exists(destination))
        {
            File.Move(destination, backupExisting, overwrite: true);
        }

        File.Move(tempPath, destination, overwrite: true);

        if (!string.IsNullOrWhiteSpace(manifest.StorageArchivePath))
        {
            await RestoreStorageAsync(manifest, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Backup restored from {BackupFile} to {Destination}", databaseBackupFile, destination);
        await _securityAudit.LogAsync(new SecurityAuditEvent(
            SecurityAuditCategory.Restore,
            "backup.restored",
            metadata: new Dictionary<string, object?>
            {
                ["fileName"] = Path.GetFileName(manifest.FileName),
                ["encrypted"] = manifest.IsEncrypted,
                ["storageRestored"] = !string.IsNullOrWhiteSpace(manifest.StorageArchivePath)
            }), cancellationToken).ConfigureAwait(false);
    }

    private BackupManifest? LoadLatestManifest()
    {
        var manifestFile = Directory.GetFiles(_backupOptions.BackupFolder!, "*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (manifestFile is null)
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(manifestFile);
            return JsonSerializer.Deserialize<BackupManifest>(json, BackupService.ManifestSerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read backup manifest {Manifest}", manifestFile);
            return null;
        }
    }

    private byte[] Decrypt(ReadOnlySpan<byte> payload)
    {
        var nonce = payload[..12];
        var tag = payload[12..28];
        var cipher = payload[28..];
        var plaintext = new byte[cipher.Length];

        using var aes = new AesGcm(_encryptionKey);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return plaintext;
    }

    private static string ResolveDatabasePath(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new InvalidOperationException("The SQLite data source must be configured");
        }

        return Path.GetFullPath(builder.DataSource);
    }

    private async Task RestoreStorageAsync(BackupManifest manifest, CancellationToken cancellationToken)
    {
        var archivePath = Path.Combine(_backupOptions.BackupFolder!, manifest.StorageArchivePath!);
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
        _logger.LogInformation("Storage restored to {Destination}", destinationRoot);
    }
}

public sealed class LogService : ILogService
{
    private readonly ILogger<LogService> _logger;

    public LogService(ILogger<LogService> logger)
    {
        _logger = logger;
    }

    public void LogInformation(string message, IDictionary<string, object?>? properties = null)
        => _logger.LogInformation("{Message} {@Properties}", message, properties ?? new Dictionary<string, object?>());

    public void LogWarning(string message, IDictionary<string, object?>? properties = null)
        => _logger.LogWarning("{Message} {@Properties}", message, properties ?? new Dictionary<string, object?>());

    public void LogError(Exception exception, string message, IDictionary<string, object?>? properties = null)
        => _logger.LogError(exception, "{Message} {@Properties}", message, properties ?? new Dictionary<string, object?>());
}

public sealed class BackupSchedulerService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly IBackupService _backupService;
    private readonly ILogger<BackupSchedulerService> _logger;
    private readonly BackupOptions _options;
    private readonly TimeSpan _interval;

    public BackupSchedulerService(IBackupService backupService, IOptions<BackupOptions> options, ILogger<BackupSchedulerService> logger)
    {
        _backupService = backupService;
        _logger = logger;
        _options = options.Value;
        _interval = TimeSpan.FromMinutes(Math.Max(1, _options.BackupIntervalMinutes));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableBackgroundServices)
        {
            _logger.LogInformation("Automated backups disabled on this platform/configuration.");
            return;
        }

        await RunBackupAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunBackupAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunBackupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _backupService.CreateBackupAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automated backup failed");
        }
    }
}
