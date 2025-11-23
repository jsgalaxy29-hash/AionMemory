using System.Security.Cryptography;
using Aion.AI;
using Aion.Domain;
using Aion.Infrastructure;
using CommunityToolkit.Maui;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace AionMemory;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        builder.Configuration.AddEnvironmentVariables();

        ConfigureOptions(builder);
        ConfigureServices(builder.Services, builder.Configuration);

        var app = builder.Build();
        app.Services.EnsureAionDatabaseAsync().GetAwaiter().GetResult();

        return app;
    }

    private static void ConfigureOptions(MauiAppBuilder builder)
    {
        var baseDirectory = FileSystem.AppDataDirectory;
        var databasePath = Path.Combine(baseDirectory, "aion.db");
        var storagePath = Path.Combine(baseDirectory, "storage");
        var marketplacePath = Path.Combine(baseDirectory, "marketplace");
        var backupPath = Path.Combine(storagePath, "backup");

        Directory.CreateDirectory(storagePath);
        Directory.CreateDirectory(marketplacePath);
        Directory.CreateDirectory(backupPath);

        builder.Services.Configure<AionDatabaseOptions>(options =>
        {
            var sqliteBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate
            };

            options.ConnectionString = sqliteBuilder.ToString();
            options.EncryptionKey = ResolveDatabaseKey(builder.Configuration);
        });

        builder.Services.Configure<StorageOptions>(options => options.RootPath = storagePath);
        builder.Services.Configure<MarketplaceOptions>(options => options.MarketplaceFolder = marketplacePath);
        builder.Services.Configure<BackupOptions>(options => options.BackupFolder = backupPath);
    }

    private static string ResolveDatabaseKey(IConfiguration configuration)
    {
        var configured = configuration["AION_DB_KEY"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var keyTask = SecureStorage.Default.GetAsync("aion_db_key");
        keyTask.Wait();
        var stored = keyTask.Result;
        if (!string.IsNullOrWhiteSpace(stored))
        {
            return stored;
        }

        var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        SecureStorage.Default.SetAsync("aion_db_key", generated).Wait();
        return generated;
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAionInfrastructure(configuration);
        services.AddAionAi(configuration);
    }
}