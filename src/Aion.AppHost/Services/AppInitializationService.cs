using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.AppHost.Services;

public sealed class AppInitializationService : IAppInitializationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AppInitializationService> _logger;
    private readonly Lazy<Task> _initializationTask;

    public AppInitializationService(IServiceProvider serviceProvider, ILogger<AppInitializationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _initializationTask = new Lazy<Task>(() => InitializeInternalAsync(CancellationToken.None));
    }

    public void Warmup()
    {
        _ = _initializationTask.Value;
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        var task = _initializationTask.Value;
        if (task.IsCompleted)
        {
            await task.ConfigureAwait(false);
            return;
        }

        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InitializeInternalAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var serviceProvider = scope.ServiceProvider;

        var options = serviceProvider.GetRequiredService<IOptions<BackupOptions>>().Value;
        if (options.AutoRestoreLatest)
        {
            await RestoreFromBackupAsync(serviceProvider, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Ensuring database is initialized.");
        try
        {
            await serviceProvider.EnsureAionDatabaseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Database initialization failed during app startup.");
            throw new InvalidOperationException(
                "La migration de la base de données a échoué. Consultez les journaux et relancez l'application.",
                ex);
        }
        var tenancyService = serviceProvider.GetRequiredService<ITenancyService>();
        await tenancyService.EnsureDefaultsAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("App initialization completed.");
    }

    private static async Task RestoreFromBackupAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(typeof(AppInitializationService));
        var databaseOptions = serviceProvider.GetRequiredService<IOptions<AionDatabaseOptions>>().Value;
        var connectionBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(databaseOptions.ConnectionString);
        var destination = Path.GetFullPath(connectionBuilder.DataSource);

        var backupService = serviceProvider.GetRequiredService<ICloudBackupService>();
        await backupService.RestoreAsync(destination, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Latest backup restored to {Destination}", destination);
    }
}
