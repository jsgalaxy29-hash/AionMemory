using System.IO;
using System.Data;
using Aion.Domain;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddAionInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var databaseOptions = services.AddOptions<AionDatabaseOptions>();
        databaseOptions.PostConfigure(options =>
        {
            options.ConnectionString = ChooseValue(options.ConnectionString, configuration["ConnectionStrings:Aion"], configuration["Aion:Database:ConnectionString"]);
            options.EncryptionKey = ChooseValue(options.EncryptionKey, configuration["Aion:Database:EncryptionKey"], configuration["AION_DB_KEY"]);

            // Ensure dev/test environments always have a working SQLCipher configuration
            // even when configuration files are minimal.
            SqliteCipherDevelopmentDefaults.ApplyDefaults(options);
        });
        databaseOptions.Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "The database connection string cannot be empty.");
        databaseOptions.Validate(o => !string.IsNullOrWhiteSpace(o.EncryptionKey), "The database encryption key cannot be empty.");
        databaseOptions.Validate(o => IsDataSourceConfigured(o.ConnectionString), "The SQLite data source path must be configured in the connection string.");
        databaseOptions.Validate(o => DatabaseDirectoryExists(o.ConnectionString), "The SQLite data source directory must exist.");
        databaseOptions.Validate(o => o.EncryptionKey?.Length >= 32, "The database encryption key must contain at least 32 characters.");
        databaseOptions.ValidateOnStart();

        var storageOptions = services.AddOptions<StorageOptions>();
        storageOptions.PostConfigure(options =>
        {
            options.RootPath = ChooseValue(options.RootPath, configuration["Aion:Storage:RootPath"]);
            options.EncryptionKey = ChooseValue(options.EncryptionKey, configuration["Aion:Storage:EncryptionKey"], configuration["Aion:Database:EncryptionKey"], configuration["AION_DB_KEY"]);
            EnsureDirectoryExists(options.RootPath);
        });
        storageOptions.Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "A storage root path is required.");
        storageOptions.Validate(o => Directory.Exists(o.RootPath), "The configured storage root path must exist.");
        storageOptions.Validate(o => !string.IsNullOrWhiteSpace(o.EncryptionKey), "The file storage encryption key cannot be empty.");
        storageOptions.Validate(o => o.EncryptionKey?.Length >= 32, "The file storage encryption key must contain at least 32 characters.");
        storageOptions.Validate(o => o.MaxFileSizeBytes > 0, "The maximum file size must be greater than zero.");
        storageOptions.Validate(o => o.MaxTotalBytes > 0, "The storage quota must be greater than zero.");
        storageOptions.ValidateOnStart();

        var marketplaceOptions = services.AddOptions<MarketplaceOptions>();
        marketplaceOptions.PostConfigure(options =>
        {
            options.MarketplaceFolder = ChooseValue(options.MarketplaceFolder, configuration["Aion:Marketplace:Folder"]);
            EnsureDirectoryExists(options.MarketplaceFolder);
        });
        marketplaceOptions.Validate(o => !string.IsNullOrWhiteSpace(o.MarketplaceFolder), "The marketplace folder cannot be empty.");
        marketplaceOptions.Validate(o => Directory.Exists(o.MarketplaceFolder), "The configured marketplace folder must exist.");
        marketplaceOptions.ValidateOnStart();

        var backupOptions = services.AddOptions<BackupOptions>();
        backupOptions.PostConfigure(options =>
        {
            options.BackupFolder = ChooseValue(options.BackupFolder, configuration["Aion:Backup:Folder"]);
            EnsureDirectoryExists(options.BackupFolder);
        });
        backupOptions.Validate(o => !string.IsNullOrWhiteSpace(o.BackupFolder), "The backup folder cannot be empty.");
        backupOptions.Validate(o => Directory.Exists(o.BackupFolder), "The configured backup folder must exist.");
        backupOptions.Validate(o => o.MaxDatabaseSizeBytes > 0, "The maximum database backup size must be greater than zero.");
        backupOptions.Validate(o => o.BackupIntervalMinutes > 0, "The backup interval must be greater than zero minutes.");
        backupOptions.ValidateOnStart();

        services.AddDbContext<AionDbContext>((serviceProvider, dbOptions) =>
        {
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<AionDatabaseOptions>>();
            SqliteConnectionFactory.ConfigureBuilder(dbOptions, databaseOptions);
        });

        services.AddScoped<IFileStorageService, FileStorageService>();
        services.AddScoped<IMetadataService, MetadataService>();
        services.AddScoped<IAionDataEngine, AionDataEngine>();
        services.AddScoped<IDataEngine>(sp => sp.GetRequiredService<IAionDataEngine>());
        services.AddScoped<IAionNoteService, NoteService>();
        services.AddScoped<INoteService>(sp => sp.GetRequiredService<IAionNoteService>());
        services.AddScoped<IAionAgendaService, AgendaService>();
        services.AddScoped<IAgendaService>(sp => sp.GetRequiredService<IAionAgendaService>());
        services.AddScoped<IAutomationOrchestrator, AutomationOrchestrator>();
        services.AddScoped<IAionAutomationService, AutomationService>();
        services.AddScoped<IAutomationService>(sp => sp.GetRequiredService<IAionAutomationService>());
        services.AddScoped<IAionTemplateMarketplaceService, TemplateService>();
        services.AddScoped<ITemplateService>(sp => sp.GetRequiredService<IAionTemplateMarketplaceService>());
        services.AddScoped<IAionLifeLogService, LifeService>();
        services.AddScoped<ILifeService>(sp => sp.GetRequiredService<IAionLifeLogService>());
        services.AddScoped<IAionPredictionService, PredictService>();
        services.AddScoped<IPredictService>(sp => sp.GetRequiredService<IAionPredictionService>());
        services.AddScoped<IAionPersonaEngine, PersonaEngine>();
        services.AddScoped<IPersonaEngine>(sp => sp.GetRequiredService<IAionPersonaEngine>());
        services.AddScoped<ISearchService, SemanticSearchService>();
        services.AddScoped<ICloudBackupService, CloudBackupService>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IRestoreService, RestoreService>();
        services.AddScoped<ILogService, LogService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddHostedService<BackupCleanupService>();
        services.AddHostedService<BackupSchedulerService>();
        services.AddScoped<ModuleDesignerService>();
        services.AddScoped<DemoModuleSeeder>();

        return services;
    }

    public static async Task EnsureAionDatabaseAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DependencyInjectionExtensions));
        var context = scope.ServiceProvider.GetRequiredService<AionDbContext>();

        await ApplyMigrationsAsync(context, logger, cancellationToken).ConfigureAwait(false);
        await ValidateSchemaAsync(context, logger, cancellationToken).ConfigureAwait(false);

        var demoSeeder = scope.ServiceProvider.GetRequiredService<DemoModuleSeeder>();
        await demoSeeder.EnsureDemoDataAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyMigrationsAsync(AionDbContext context, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migrations failed during startup.");
            throw;
        }
    }

    private static async Task ValidateSchemaAsync(AionDbContext context, ILogger logger, CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(context, "Modules", cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false);
        logger.LogError("Database schema validation failed. Required table 'Modules' is missing after applying migrations: {AppliedMigrations}", appliedMigrations);
        throw new InvalidOperationException("Database schema validation failed; required tables were not created after applying migrations.");
    }

    private static async Task<bool> TableExistsAsync(AionDbContext context, string tableName, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = $name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (shouldClose)
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }

        return result is string;
    }

    private static string ChooseValue(string current, params string?[] candidates)
    {
        if (!string.IsNullOrWhiteSpace(current))
        {
            return current;
        }

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return current;
    }

    private static void EnsureDirectoryExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    private static bool IsDataSourceConfigured(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);
        return !string.IsNullOrWhiteSpace(builder.DataSource);
    }

    private static bool DatabaseDirectoryExists(string? connectionString)
    {
        if (!IsDataSourceConfigured(connectionString))
        {
            return false;
        }

        var builder = new SqliteConnectionStringBuilder(connectionString!);
        var fullPath = Path.GetFullPath(builder.DataSource);
        var directory = Path.GetDirectoryName(fullPath);
        return directory is not null && Directory.Exists(directory);
    }
}
