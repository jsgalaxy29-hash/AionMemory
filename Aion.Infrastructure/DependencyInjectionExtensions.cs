using System.IO;
using Aion.Domain;
using Aion.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        });
        databaseOptions.Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "The database connection string cannot be empty.");
        databaseOptions.Validate(o => !string.IsNullOrWhiteSpace(o.EncryptionKey), "The database encryption key cannot be empty.");
        databaseOptions.ValidateOnStart();

        var storageOptions = services.AddOptions<StorageOptions>();
        storageOptions.PostConfigure(options =>
        {
            options.RootPath = ChooseValue(options.RootPath, configuration["Aion:Storage:RootPath"]);
            EnsureDirectoryExists(options.RootPath);
        });
        storageOptions.Validate(o => !string.IsNullOrWhiteSpace(o.RootPath), "A storage root path is required.");
        storageOptions.Validate(o => Directory.Exists(o.RootPath), "The configured storage root path must exist.");
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
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<ICloudBackupService, CloudBackupService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ModuleDesignerService>();

        return services;
    }

    public static async Task EnsureAionDatabaseAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AionDbContext>();
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
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
}
