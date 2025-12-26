using System.Data;
using System.IO;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Aion.Infrastructure.Extensions;
using Aion.Infrastructure.ModuleBuilder;
using Aion.Infrastructure.Observability;
using Aion.Infrastructure.Services;
using Aion.Infrastructure.Services.Automation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddAionInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var dataDirectory = GetDefaultDataDirectory();
        var defaultStorageRoot = Path.Combine(dataDirectory, "storage");
        var defaultMarketplaceFolder = Path.Combine(dataDirectory, "marketplace");
        var defaultBackupFolder = Path.Combine(defaultStorageRoot, "backup");
        var allowDevKey = IsDevelopmentEnvironment(configuration);

        var databaseOptions = services.AddOptions<AionDatabaseOptions>();
        databaseOptions.PostConfigure(options =>
        {
            options.ConnectionString = ChooseValue(options.ConnectionString, configuration["ConnectionStrings:Aion"], configuration["Aion:Database:ConnectionString"]);
            options.EncryptionKey = ChooseValue(options.EncryptionKey, configuration["Aion:Database:EncryptionKey"], configuration["AION_DB_KEY"]);

            // Ensure dev/test environments always have a working SQLCipher configuration
            // even when configuration files are minimal.
            if (allowDevKey)
            {
                SqliteCipherDevelopmentDefaults.ApplyDefaults(options, directory: dataDirectory);
            }
        });
        databaseOptions.Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "The database connection string cannot be empty.");
        databaseOptions.Validate(o => !string.IsNullOrWhiteSpace(o.EncryptionKey), "The database encryption key cannot be empty.");
        databaseOptions.Validate(o => IsDataSourceConfigured(o.ConnectionString), "The SQLite data source path must be configured in the connection string.");
        databaseOptions.Validate(o => DatabaseDirectoryExists(o.ConnectionString), "The SQLite data source directory must exist.");
        databaseOptions.Validate(o => o.EncryptionKey?.Length >= 32, "The database encryption key must contain at least 32 characters.");
        databaseOptions.Validate(
            o => allowDevKey || !string.Equals(o.EncryptionKey, SqliteCipherDevelopmentDefaults.DevelopmentKey, StringComparison.Ordinal),
            "The development SQLCipher key must not be used outside development/test environments.");
        databaseOptions.ValidateOnStart();

        var storageOptions = services.AddOptions<StorageOptions>();
        storageOptions.PostConfigure(options =>
        {
            options.RootPath = EnsureDirectoryPath(ChooseValue(options.RootPath, configuration["Aion:Storage:RootPath"], defaultStorageRoot));
            options.EncryptionKey = allowDevKey
                ? ChooseValue(options.EncryptionKey, configuration["Aion:Storage:EncryptionKey"], configuration["Aion:Database:EncryptionKey"], configuration["AION_DB_KEY"], SqliteCipherDevelopmentDefaults.DevelopmentKey)
                : ChooseValue(options.EncryptionKey, configuration["Aion:Storage:EncryptionKey"], configuration["Aion:Database:EncryptionKey"], configuration["AION_DB_KEY"]);
        });
        storageOptions.Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "A storage root path is required.");
        storageOptions.Validate(o => Directory.Exists(o.RootPath), "The configured storage root path must exist.");
        storageOptions.Validate(o => !o.EncryptPayloads || !string.IsNullOrWhiteSpace(o.EncryptionKey), "The file storage encryption key cannot be empty when encryption is enabled.");
        storageOptions.Validate(o => !o.EncryptPayloads || o.EncryptionKey?.Length >= 32, "The file storage encryption key must contain at least 32 characters when encryption is enabled.");
        storageOptions.Validate(
            o => allowDevKey || !string.Equals(o.EncryptionKey, SqliteCipherDevelopmentDefaults.DevelopmentKey, StringComparison.Ordinal),
            "The development storage key must not be used outside development/test environments.");
        storageOptions.Validate(o => o.MaxFileSizeBytes > 0, "The maximum file size must be greater than zero.");
        storageOptions.Validate(o => o.MaxTotalBytes > 0, "The storage quota must be greater than zero.");
        storageOptions.ValidateOnStart();

        var marketplaceOptions = services.AddOptions<MarketplaceOptions>();
        marketplaceOptions.PostConfigure(options =>
        {
            var fallbackFolder = options.MarketplaceFolder ?? defaultMarketplaceFolder;
            options.MarketplaceFolder = EnsureDirectoryPath(ChooseValue(options.MarketplaceFolder, configuration["Aion:Marketplace:Folder"], fallbackFolder));
        });
        marketplaceOptions.Validate(o => !string.IsNullOrWhiteSpace(o.MarketplaceFolder), "The marketplace folder cannot be empty.");
        marketplaceOptions.Validate(o => Directory.Exists(o.MarketplaceFolder), "The configured marketplace folder must exist.");
        marketplaceOptions.ValidateOnStart();

        var extensionOptions = services.AddOptions<ExtensionOptions>()
            .Bind(configuration.GetSection("Aion:Extensions"));
        extensionOptions.PostConfigure(options =>
        {
            options.ExtensionsRootPath = EnsureDirectoryPath(ChooseValue(options.ExtensionsRootPath, configuration["Aion:Extensions:RootPath"], AppContext.BaseDirectory));
        });
        extensionOptions.Validate(o => !string.IsNullOrWhiteSpace(o.ExtensionsRootPath), "The extensions root path cannot be empty.");
        extensionOptions.Validate(o => Directory.Exists(o.ExtensionsRootPath), "The configured extensions root path must exist.");
        extensionOptions.ValidateOnStart();

        var backupOptions = services.AddOptions<BackupOptions>();
        backupOptions.PostConfigure(options =>
        {
            var fallback = options.BackupFolder ?? defaultBackupFolder;
            options.BackupFolder = EnsureDirectoryPath(ChooseValue(options.BackupFolder, configuration["Aion:Backup:Folder"], fallback));
        });
        backupOptions.Validate(o => !string.IsNullOrWhiteSpace(o.BackupFolder), "The backup folder cannot be empty.");
        backupOptions.Validate(o => Directory.Exists(o.BackupFolder), "The configured backup folder must exist.");
        backupOptions.Validate(o => o.MaxDatabaseSizeBytes > 0, "The maximum database backup size must be greater than zero.");
        backupOptions.Validate(o => o.BackupIntervalMinutes > 0, "The backup interval must be greater than zero minutes.");
        backupOptions.ValidateOnStart();

        services.AddScoped<IWorkspaceContext, DefaultWorkspaceContext>();

        services.AddDbContext<AionDbContext>((serviceProvider, dbOptions) =>
        {
            var databaseOptions = serviceProvider.GetRequiredService<IOptions<AionDatabaseOptions>>();
            SqliteConnectionFactory.ConfigureBuilder(dbOptions, databaseOptions);
        });

        services.AddScoped<IStorageService, StorageService>();
        services.AddScoped<IFileStorageService, FileStorageService>();
        services.AddScoped<IMetadataService, MetadataService>();
        services.AddScoped<ITenancyService, TenancyService>();
        services.AddSingleton<IOperationScopeFactory, OperationScopeFactory>();
        services.AddScoped<IAionDataEngine, AionDataEngine>();
        services.AddScoped<IDataEngine, AuthorizedDataEngine>();
        services.AddScoped<IAutomationRuleEngine>(sp => new AutomationRuleEngine(
            sp.GetRequiredService<AionDbContext>(),
            sp.GetRequiredService<INoteService>(),
            sp.GetRequiredService<IAgendaService>(),
            () => sp.GetRequiredService<IAionDataEngine>(),
            sp.GetRequiredService<ILogger<AutomationRuleEngine>>()));
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
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
        services.AddScoped<IMemoryIntelligenceService, MemoryIntelligenceService>();
        services.AddScoped<ISearchService, SemanticSearchService>();
        services.AddScoped<ISyncEngine, SyncEngine>();
        services.AddScoped<SyncOutboxService>();
        services.AddScoped<IDataExportService, DataExportService>();
        services.AddScoped<IDataImportService, DataImportService>();
        services.AddScoped<IModuleValidator, ModuleValidator>();
        services.AddScoped<IModuleApplier, ModuleApplier>();
        services.AddScoped<ModuleBuilderService>();
        services.AddScoped<ICloudBackupService, CloudBackupService>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IRestoreService, RestoreService>();
        services.AddScoped<ILogService, LogService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddHostedService<BackupCleanupService>();
        services.AddHostedService<BackupSchedulerService>();
        services.AddScoped<DemoModuleSeeder>();
        services.TryAddSingleton<IExtensionState, DefaultExtensionState>();
        services.AddSingleton<IExtensionCatalog, ExtensionCatalog>();

        return services;
    }

    public static async Task EnsureAionDatabaseAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DependencyInjectionExtensions));
        var context = scope.ServiceProvider.GetRequiredService<AionDbContext>();

        await InitializeDatabaseAsync(context, logger, cancellationToken).ConfigureAwait(false);

        var demoSeeder = scope.ServiceProvider.GetRequiredService<DemoModuleSeeder>();
        await demoSeeder.EnsureDemoDataAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InitializeDatabaseAsync(AionDbContext context, ILogger logger, CancellationToken cancellationToken)
    {
        var dataSource = GetDatabaseLabel(context);
        logger.LogInformation("Opening SQLite database at {DatabasePath}", dataSource);

        var connection = context.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var integrityReport = await DatabaseIntegrityVerifier.VerifyAsync(connection, cancellationToken).ConfigureAwait(false);
            if (!integrityReport.IsValid)
            {
                logger.LogError(
                    "Database integrity check failed for data source {DatabasePath}: {Issues}",
                    dataSource,
                    string.Join(" | ", integrityReport.Issues));
                throw new InvalidOperationException("Database integrity check failed. Run the recovery export tool before retrying startup.");
            }

            await ApplyMigrationsAsync(context, logger, cancellationToken).ConfigureAwait(false);
            await ValidateSchemaAsync(context, logger, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (shouldClose)
            {
                await context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
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
            logger.LogCritical(ex, "Database migrations failed during startup for data source {DatabasePath}.", GetDatabaseLabel(context));
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
        logger.LogCritical("Database schema validation failed. Required table 'Modules' is missing after applying migrations: {AppliedMigrations}", appliedMigrations);
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

    private static string EnsureDirectoryPath(string? path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Directory.CreateDirectory(path);
        return path;
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

    private static string GetDefaultDataDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string ChooseValue(string? current, params string?[] candidates)
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

        return string.Empty;
    }

    private static bool IsDevelopmentEnvironment(IConfiguration configuration)
    {
        var environment = configuration["DOTNET_ENVIRONMENT"] ?? configuration["ASPNETCORE_ENVIRONMENT"];
        if (string.IsNullOrWhiteSpace(environment))
        {
            return true;
        }

        return string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "Dev", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "Test", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment, "Testing", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDatabaseLabel(AionDbContext context)
    {
        var connectionString = context.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "(unknown data source)";
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);
        builder.Remove("Password");
        builder.Remove("Pwd");

        return string.IsNullOrWhiteSpace(builder.DataSource) ? "(in-memory)" : Path.GetFullPath(builder.DataSource);
    }
}
