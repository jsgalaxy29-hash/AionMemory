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
        services.PostConfigure<AionDatabaseOptions>(options =>
        {
            options.ConnectionString = ChooseValue(options.ConnectionString, configuration["ConnectionStrings:Aion"], configuration["Aion:Database:ConnectionString"]);
            options.EncryptionKey = ChooseValue(options.EncryptionKey, configuration["Aion:Database:EncryptionKey"], configuration["AION_DB_KEY"]);
        });

        services.PostConfigure<StorageOptions>(options =>
        {
            options.RootPath = ChooseValue(options.RootPath, configuration["Aion:Storage:RootPath"]);
        });

        services.PostConfigure<MarketplaceOptions>(options =>
        {
            options.MarketplaceFolder = ChooseValue(options.MarketplaceFolder, configuration["Aion:Marketplace:Folder"]);
        });

        services.PostConfigure<BackupOptions>(options =>
        {
            options.BackupFolder = ChooseValue(options.BackupFolder, configuration["Aion:Backup:Folder"]);
        });

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
}
