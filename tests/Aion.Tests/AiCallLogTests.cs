using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aion.AI;
using Aion.Domain;
using Aion.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Tests;

public sealed class AiCallLogTests
{
    [Fact]
    public async Task Mock_ai_calls_are_logged()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(root);

        var databasePath = Path.Combine(root, "aion_metrics.db");
        var storagePath = Path.Combine(root, "storage");
        var backupPath = Path.Combine(root, "backup");
        var marketplacePath = Path.Combine(root, "marketplace");

        Directory.CreateDirectory(storagePath);
        Directory.CreateDirectory(backupPath);
        Directory.CreateDirectory(marketplacePath);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Aion:Database:ConnectionString"] = $"Data Source={databasePath}",
                ["Aion:Database:EncryptionKey"] = SqliteCipherDevelopmentDefaults.DevelopmentKey,
                ["Aion:Storage:RootPath"] = storagePath,
                ["Aion:Backup:Folder"] = backupPath,
                ["Aion:Marketplace:Folder"] = marketplacePath,
                ["Aion:Ai:Provider"] = AiProviderNames.Mock
            }!)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(NullLoggerProvider.Instance));
        services.AddAionInfrastructure(configuration);
        services.AddAionAi(configuration);

        await using var provider = services.BuildServiceProvider();
        try
        {
            await provider.EnsureAionDatabaseAsync();

            var chat = provider.GetRequiredService<IChatModel>();
            var response = await chat.GenerateAsync("ping");

            Assert.StartsWith("[mock-chat]", response.Content);

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AionDbContext>();
            var logs = await db.AiCallLogs.AsNoTracking().ToListAsync();

            Assert.NotEmpty(logs);
            var last = logs.OrderByDescending(log => log.OccurredAt).First();
            Assert.Equal("Mock", last.Provider);
            Assert.Equal("chat", last.Operation);
            Assert.Equal(AiCallStatus.Success, last.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
