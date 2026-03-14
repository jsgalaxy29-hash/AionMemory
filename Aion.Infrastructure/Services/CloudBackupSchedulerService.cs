using Aion.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class CloudBackupSchedulerService : BackgroundService
{
    private readonly ICloudBackupService _backupService;
    private readonly BackupOptions _backupOptions;
    private readonly CloudBackupOptions _cloudOptions;
    private readonly ILogger<CloudBackupSchedulerService> _logger;
    private readonly TimeSpan _interval;

    public CloudBackupSchedulerService(
        ICloudBackupService backupService,
        IOptions<BackupOptions> backupOptions,
        IOptions<CloudBackupOptions> cloudOptions,
        ILogger<CloudBackupSchedulerService> logger)
    {
        _backupService = backupService;
        _backupOptions = backupOptions.Value;
        _cloudOptions = cloudOptions.Value;
        _logger = logger;
        _interval = TimeSpan.FromMinutes(Math.Max(1, _backupOptions.BackupIntervalMinutes));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_backupOptions.EnableBackgroundServices || !_cloudOptions.Enabled)
        {
            _logger.LogInformation("Cloud backup scheduler disabled.");
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
            await _backupService.CreateBackupAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloud backup scheduling failed");
        }
    }
}
