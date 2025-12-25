using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using Aion.Domain;
using Aion.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aion.Infrastructure.Tests;

public class DatabaseLifecycleTests
{
    [Fact]
    public async Task EnsureAionDatabaseAsync_creates_and_migrates_missing_database()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var databasePath = Path.Combine(root, "aion_lifecycle.db");
        var provider = BuildProvider(root, databasePath);

        try
        {
            await provider.EnsureAionDatabaseAsync();

            await using var scope = provider.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AionDbContext>();
            Assert.True(File.Exists(databasePath));
            Assert.True(await context.Database.CanConnectAsync());

            var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
            Assert.NotEmpty(appliedMigrations);
            Assert.True(await TableExistsAsync(context.Database.GetDbConnection(), "Modules"));
        }
        finally
        {
            await provider.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SqliteEncryptionInterceptor_requires_non_empty_key()
    {
        var options = Options.Create(new AionDatabaseOptions
        {
            ConnectionString = "DataSource=:memory:",
            EncryptionKey = string.Empty
        });

        var builder = new DbContextOptionsBuilder<AionDbContext>();
        SqliteConnectionFactory.ConfigureBuilder(builder, options);

        await using var context = new AionDbContext(builder.Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Database.OpenConnectionAsync());
    }

    [Fact]
    public void Options_validation_fails_when_database_directory_is_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Aion:Database:ConnectionString"] = $"Data Source={Path.Combine(root, "missing", "aion.db")}",
                ["Aion:Database:EncryptionKey"] = SqliteCipherDevelopmentDefaults.DevelopmentKey,
                ["Aion:Storage:RootPath"] = Path.Combine(root, "storage"),
                ["Aion:Backup:Folder"] = Path.Combine(root, "backup"),
                ["Aion:Marketplace:Folder"] = Path.Combine(root, "marketplace")
            }!)
            .Build();

        Directory.CreateDirectory(Path.Combine(root, "storage"));
        Directory.CreateDirectory(Path.Combine(root, "backup"));
        Directory.CreateDirectory(Path.Combine(root, "marketplace"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAionInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AionDatabaseOptions>>().Value);

        Directory.Delete(root, recursive: true);
    }

    private static ServiceProvider BuildProvider(string root, string databasePath, string? encryptionKey = null)
    {
        var key = string.IsNullOrWhiteSpace(encryptionKey) ? SqliteCipherDevelopmentDefaults.DevelopmentKey : encryptionKey;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Aion:Database:ConnectionString"] = $"Data Source={databasePath}",
                ["Aion:Database:EncryptionKey"] = key,
                ["Aion:Storage:RootPath"] = Path.Combine(root, "storage"),
                ["Aion:Backup:Folder"] = Path.Combine(root, "backup"),
                ["Aion:Marketplace:Folder"] = Path.Combine(root, "marketplace")
            }!)
            .Build();

        Directory.CreateDirectory(Path.Combine(root, "storage"));
        Directory.CreateDirectory(Path.Combine(root, "backup"));
        Directory.CreateDirectory(Path.Combine(root, "marketplace"));

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(NullLoggerProvider.Instance));
        services.AddAionInfrastructure(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task EnsureAionDatabaseAsync_fails_when_encryption_key_is_incorrect()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var databasePath = Path.Combine(root, "aion_protected.db");
        var correctProvider = BuildProvider(root, databasePath);
        try
        {
            await correctProvider.EnsureAionDatabaseAsync();
        }
        finally
        {
            await correctProvider.DisposeAsync();
        }

        var wrongKey = new string('x', 48);
        var failingProvider = BuildProvider(root, databasePath, wrongKey);
        try
        {
            await Assert.ThrowsAsync<SqliteException>(() => failingProvider.EnsureAionDatabaseAsync());
        }
        finally
        {
            await failingProvider.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = $name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        var result = await command.ExecuteScalarAsync();

        if (shouldClose)
        {
            await connection.CloseAsync();
        }

        return result is string;
    }
}
