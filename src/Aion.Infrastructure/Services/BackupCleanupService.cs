using System.Text.Json;
using Aion.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class BackupCleanupService : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly BackupOptions _options;
    private readonly ILogger<BackupCleanupService> _logger;

    public BackupCleanupService(IOptions<BackupOptions> options, ILogger<BackupCleanupService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableBackgroundServices)
        {
            _logger.LogInformation("Backup cleanup background task disabled on this platform/configuration.");
            return;
        }

        await RunCleanupAsync(stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken).ConfigureAwait(false);
            await RunCleanupAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BackupFolder) || !Directory.Exists(_options.BackupFolder))
        {
            _logger.LogWarning("Backup folder not configured; skipping cleanup");
            return;
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Abs(_options.RetentionDays));
        var manifests = Directory.EnumerateFiles(_options.BackupFolder, "*.json")
            .Select(path => ReadManifest(path))
            .Where(m => m.Manifest is not null)
            .Select(m => m.Manifest!)
            .OrderByDescending(m => m.CreatedAt)
            .ToList();

        var removalQueue = new List<BackupManifest>();
        for (var i = 0; i < manifests.Count; i++)
        {
            var manifest = manifests[i];
            if (manifest.CreatedAt < cutoff || i >= _options.MaxBackups)
            {
                removalQueue.Add(manifest);
            }
        }

        foreach (var manifest in removalQueue)
        {
            var backupPath = Path.Combine(_options.BackupFolder, manifest.FileName);
            var manifestPath = Path.ChangeExtension(backupPath, ".json");
            TryDelete(manifestPath);
            TryDelete(backupPath);
        }

        _logger.LogInformation("Backup cleanup completed. Removed {Count} backups", removalQueue.Count);
        await Task.CompletedTask;
    }

    private (BackupManifest? Manifest, string Path) ReadManifest(string manifestPath)
    {
        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<BackupManifest>(json, SerializerOptions);
            if (manifest is null)
            {
                _logger.LogWarning("Skipping malformed manifest {ManifestPath}", manifestPath);
            }

            return (manifest, manifestPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse manifest {ManifestPath}", manifestPath);
            return (null, manifestPath);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to delete stale backup artifact {Path}", path);
        }
    }
}
