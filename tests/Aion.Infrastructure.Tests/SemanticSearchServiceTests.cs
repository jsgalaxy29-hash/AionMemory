using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Infrastructure.Tests;

public class SemanticSearchServiceTests
{
    [Fact]
    public async Task Search_async_skips_missing_keyword_indexes()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AionDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AionDbContext(options, new TestWorkspaceContext());
        await context.Database.MigrateAsync();
        await context.Database.ExecuteSqlRawAsync(@"DROP TABLE IF EXISTS NoteSearch; DROP TABLE IF EXISTS RecordSearch; DROP TABLE IF EXISTS FileSearch;");

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var indexService = new RecordSearchIndexService(context, NullLogger<RecordSearchIndexService>.Instance);
        var service = new SemanticSearchService(context, NullLogger<SemanticSearchService>.Instance, serviceProvider, indexService);

        var results = await service.SearchAsync("hello");

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_async_applies_migrations_when_keyword_indexes_are_absent()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AionDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AionDbContext(options, new TestWorkspaceContext());

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var indexService = new RecordSearchIndexService(context, NullLogger<RecordSearchIndexService>.Instance);
        var service = new SemanticSearchService(context, NullLogger<SemanticSearchService>.Instance, serviceProvider, indexService);

        var results = await service.SearchAsync("hello");

        Assert.Empty(results);
        Assert.Equal(0, await context.NoteSearch.CountAsync());
    }
}
