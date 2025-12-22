using Aion.Domain;
using Aion.Infrastructure;
using Aion.Infrastructure.Observability;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aion.Tests.Fixtures;

public sealed class SqliteInMemoryFixture : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly ILoggerFactory _loggerFactory;

    public SqliteInMemoryFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _loggerFactory = NullLoggerFactory.Instance;
        Options = new DbContextOptionsBuilder<AionDbContext>()
            .UseSqlite(_connection)
            .UseLoggerFactory(_loggerFactory)
            .Options;
    }

    public DbContextOptions<AionDbContext> Options { get; }

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var context = new AionDbContext(Options);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    public AionDbContext CreateContext() => new(Options);

    public AionDataEngine CreateDataEngine(ISearchService? search = null)
    {
        var logger = _loggerFactory.CreateLogger<AionDataEngine>();
        return new AionDataEngine(CreateContext(), logger, search ?? new NullSearchService(), new OperationScopeFactory());
    }
}

file sealed class NullSearchService : ISearchService
{
    public Task<IEnumerable<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<SearchHit>>(Array.Empty<SearchHit>());

    public Task IndexNoteAsync(S_Note note, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task IndexRecordAsync(F_Record record, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task IndexFileAsync(F_File file, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RemoveAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
