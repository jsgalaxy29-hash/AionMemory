using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Aion.AI;
using Aion.AI.Adapters;
using Aion.AI.Providers.Mistral;
using Aion.AI.Providers.OpenAI;
using Aion.Composition;
using Aion.AppHost.Services;
using Aion.Domain;
using CommunityToolkit.Maui;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace Aion.AppHost;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Configuration
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.OpenAI.json", optional: true)
            .AddJsonFile("appsettings.Mistral.json", optional: true)
#if DEBUG
            .AddUserSecrets<App>(optional: true)
#endif
            .AddEnvironmentVariables();

        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        ConfigureOptions(builder);
        ConfigureServices(builder.Services, builder.Configuration);

        var app = builder.Build();
        _ = app.Services.GetRequiredService<OfflineActionReplayCoordinator>();
        var firstRunState = app.Services.GetRequiredService<FirstRunState>();
        if (firstRunState.IsCompleted)
        {
            var initializer = app.Services.GetRequiredService<IAppInitializationService>();
            initializer.Warmup();
        }

        return app;
    }

    private static void ConfigureOptions(MauiAppBuilder builder)
    {
        var baseDirectory = FileSystem.AppDataDirectory;
        var defaultDatabasePath = Path.Combine(baseDirectory, "aion.db");
        var databasePath = FirstRunState.GetDatabasePathOrDefault(defaultDatabasePath);
        var storagePath = Path.Combine(baseDirectory, "storage");
        var marketplacePath = Path.Combine(baseDirectory, "marketplace");
        var backupPath = Path.Combine(storagePath, "backup");

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        Directory.CreateDirectory(storagePath);
        Directory.CreateDirectory(marketplacePath);
        Directory.CreateDirectory(backupPath);

        builder.Services.Configure<AionDatabaseOptions>(options =>
        {
            var sqliteBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private
            };

            options.ConnectionString = sqliteBuilder.ToString();
            options.EncryptionKey = ResolveDatabaseKey(builder.Configuration);
        });

        builder.Services.Configure<StorageOptions>(options =>
        {
            options.RootPath = storagePath;
            options.EncryptionKey = ResolveStorageKey(builder.Configuration);
        });
        builder.Services.Configure<MarketplaceOptions>(options => options.MarketplaceFolder = marketplacePath);
        builder.Services.Configure<BackupOptions>(options => options.BackupFolder = backupPath);
    }

    private static string ResolveDatabaseKey(IConfiguration configuration)
        => SecureStorageKeyResolver.ResolveAsync(configuration, "aion_db_key", "AION_DB_KEY").GetAwaiter().GetResult();

    private static string ResolveStorageKey(IConfiguration configuration)
        => SecureStorageKeyResolver.ResolveAsync(configuration, "aion_storage_key", "AION_STORAGE_KEY").GetAwaiter().GetResult();

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAionInfrastructure(configuration);
        services.AddAionAi(configuration);

        services.AddAiAdapters();
        services.AddAionOpenAi();
        services.AddAionMistral();
        services.AddScoped<ITableDefinitionService, TableDefinitionService>();
        services.AddSingleton<IAppInitializationService, AppInitializationService>();
        services.AddScoped<IRecordQueryService, RecordQueryService>();
        services.AddScoped<IModuleViewService, ModuleViewService>();
        services.AddScoped<UiState>();
        services.AddScoped<IWorkspaceContextAccessor, WorkspaceContext>();
        services.AddScoped<IWorkspaceContext>(sp => sp.GetRequiredService<IWorkspaceContextAccessor>());
        services.AddScoped<WorkspaceSelectionState>();
        services.AddSingleton<IConnectivityService, MauiConnectivityService>();
        services.AddSingleton<OfflineActionReplayCoordinator>();
        services.AddSingleton<IExtensionState, PreferencesExtensionState>();
        services.AddSingleton<AccessibilityState>();
        services.AddSingleton<FirstRunState>();
        services.AddSingleton<INotificationService, NotificationService>();
#if ANDROID
        services.AddSingleton<INotificationPlatformService, Aion.AppHost.Platforms.Android.AndroidNotificationPlatformService>();
#elif IOS
        services.AddSingleton<INotificationPlatformService, Aion.AppHost.Platforms.iOS.IosNotificationPlatformService>();
#elif MACCATALYST
        services.AddSingleton<INotificationPlatformService, Aion.AppHost.Platforms.MacCatalyst.MacCatalystNotificationPlatformService>();
#elif WINDOWS
        services.AddSingleton<INotificationPlatformService, Aion.AppHost.Platforms.Windows.WindowsNotificationPlatformService>();
#else
        services.AddSingleton<INotificationPlatformService, NullNotificationPlatformService>();
#endif
    }
}

internal static class SecureStorageKeyResolver
{
    public static async Task<string> ResolveAsync(IConfiguration configuration, string storageKeyName, string environmentVariableName)
    {
        var configured = configuration[environmentVariableName];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var stored = await SecureStorage.Default.GetAsync(storageKeyName).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            return stored;
        }

        var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await SecureStorage.Default.SetAsync(storageKeyName, generated).ConfigureAwait(false);
        return generated;
    }
}
