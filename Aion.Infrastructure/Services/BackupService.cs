using System.Security.Cryptography;
using System.Text.Json;
using Aion.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
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
    private readonly ILogger<BackupService> _logger;
    private readonly byte[] _encryptionKey;

    public BackupService(
        IOptions<AionDatabaseOptions> databaseOptions,
        IOptions<BackupOptions> backupOptions,
        ILogger<BackupService> logger)
    {
        _databaseOptions = databaseOptions.Value;
        _backupOptions = backupOptions.Value;
        _logger = logger;
        _encryptionKey = DeriveKey(_databaseOptions.EncryptionKey);

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

        var timestamp = DateTimeOffset.UtcNow;
        var extension = encrypt ? ".db.enc" : ".db";
        var fileName = $"snapshot-{timestamp:yyyyMMddHHmmss}{extension}";
        var destination = Path.Combine(_backupOptions.BackupFolder!, fileName);

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

        var manifest = new BackupManifest
        {
            FileName = fileName,
            Size = new FileInfo(destination).Length,
            CreatedAt = timestamp,
            Sha256 = await ComputeHashAsync(destination, cancellationToken).ConfigureAwait(false),
            SourcePath = databasePath,
            IsEncrypted = encrypt
        };

        await using (var manifestStream = new FileStream(
                       Path.ChangeExtension(destination, ".json"),
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
}

public sealed class RestoreService : IRestoreService
{
    private readonly BackupOptions _backupOptions;
    private readonly AionDatabaseOptions _databaseOptions;
    private readonly ILogger<RestoreService> _logger;
    private readonly byte[] _encryptionKey;

    public RestoreService(
        IOptions<BackupOptions> backupOptions,
        IOptions<AionDatabaseOptions> databaseOptions,
        ILogger<RestoreService> logger)
    {
        _backupOptions = backupOptions.Value;
        _databaseOptions = databaseOptions.Value;
        _logger = logger;
        _encryptionKey = BackupService.DeriveKey(_databaseOptions.EncryptionKey);
    }

    public async Task RestoreLatestAsync(string? destinationPath = null, CancellationToken cancellationToken = default)
    {
        var manifest = LoadLatestManifest();
        if (manifest is null)
        {
            throw new FileNotFoundException("No backup manifest found", _backupOptions.BackupFolder);
        }

        var backupFile = Path.Combine(_backupOptions.BackupFolder!, manifest.FileName);
        if (!File.Exists(backupFile))
        {
            throw new FileNotFoundException("Backup file missing", backupFile);
        }

        var destination = destinationPath ?? ResolveDatabasePath(_databaseOptions.ConnectionString);
        var tempPath = destination + ".restoring";

        if (manifest.IsEncrypted || backupFile.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
        {
            var payload = await File.ReadAllBytesAsync(backupFile, cancellationToken).ConfigureAwait(false);
            var decrypted = Decrypt(payload);
            await File.WriteAllBytesAsync(tempPath, decrypted, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await using var source = new FileStream(
                backupFile,
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
        _logger.LogInformation("Backup restored from {BackupFile} to {Destination}", backupFile, destination);
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

public sealed class BackupSchedulerService : BackgroundService
{
    private readonly IBackupService _backupService;
    private readonly ILogger<BackupSchedulerService> _logger;
    private readonly TimeSpan _interval;

    public BackupSchedulerService(IBackupService backupService, IOptions<BackupOptions> options, ILogger<BackupSchedulerService> logger)
    {
        _backupService = backupService;
        _logger = logger;
        _interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.BackupIntervalMinutes));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
