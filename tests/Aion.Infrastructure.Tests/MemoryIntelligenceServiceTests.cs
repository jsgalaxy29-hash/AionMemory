using Aion.AI;
using Aion.Domain;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Infrastructure.Tests;

public class MemoryIntelligenceServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_persists_insight()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AionDbContext(options);
        await context.Database.MigrateAsync();

        var service = new MemoryIntelligenceService(context, new TestMemoryAnalyzer(), NullLogger<MemoryIntelligenceService>.Instance);
        var request = new MemoryAnalysisRequest(new[] { new MemoryRecord(Guid.NewGuid(), "Note", "Contenu", "note") }, scope: "unit");

        var insight = await service.AnalyzeAsync(request);

        Assert.Equal("unit", insight.Scope);
        Assert.Equal(1, insight.RecordCount);
        Assert.NotEqual(Guid.Empty, insight.Id);

        var persisted = await context.MemoryInsights.SingleAsync();
        Assert.Equal(insight.Id, persisted.Id);
        Assert.Equal("unit", persisted.Scope);
        Assert.Equal(1, persisted.RecordCount);
        Assert.False(string.IsNullOrWhiteSpace(persisted.Summary));
    }

    [Fact]
    public async Task GetRecentAsync_returns_latest_first()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AionDbContext(options);
        await context.Database.MigrateAsync();

        var analyzer = new TestMemoryAnalyzer();
        var service = new MemoryIntelligenceService(context, analyzer, NullLogger<MemoryIntelligenceService>.Instance);

        var first = await service.AnalyzeAsync(new MemoryAnalysisRequest(new[] { new MemoryRecord(Guid.NewGuid(), "A", "", "note") }, scope: "first"));
        await Task.Delay(5);
        var second = await service.AnalyzeAsync(new MemoryAnalysisRequest(new[] { new MemoryRecord(Guid.NewGuid(), "B", "", "note") }, scope: "second"));

        var recent = await service.GetRecentAsync(1);
        var latest = Assert.Single(recent);
        Assert.Equal(second.Id, latest.Id);
        Assert.Equal("second", latest.Scope);
    }

    private sealed class TestMemoryAnalyzer : IMemoryAnalyzer
    {
        public Task<MemoryAnalysisResult> AnalyzeAsync(MemoryAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            var topics = new[] { new MemoryTopic("demo", Array.Empty<string>()) };
            var links = Array.Empty<MemoryLinkSuggestion>();
            return Task.FromResult(new MemoryAnalysisResult("Résumé test", topics, links, "test"));
        }
    }
}
