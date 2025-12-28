using Aion.AI;
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
    private readonly ICurrentUserService _currentUserService;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly Guid _currentUserId = Guid.NewGuid();

    public SqliteInMemoryFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _loggerFactory = NullLoggerFactory.Instance;
        _currentUserService = new FixedCurrentUserService(_currentUserId);
        _workspaceContext = new TestWorkspaceContext();
        Options = new DbContextOptionsBuilder<AionDbContext>()
            .UseSqlite(_connection)
            .UseLoggerFactory(_loggerFactory)
            .Options;
    }

    public DbContextOptions<AionDbContext> Options { get; }
    public Guid CurrentUserId => _currentUserId;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var context = new AionDbContext(Options, _workspaceContext);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    public AionDbContext CreateContext() => new(Options, _workspaceContext);

    public AionDataEngine CreateDataEngine(ISearchService? search = null, IEmbeddingProvider? embeddingProvider = null, ICurrentUserService? currentUserService = null)
    {
        var logger = _loggerFactory.CreateLogger<AionDataEngine>();
        return new AionDataEngine(
            CreateContext(),
            logger,
            search ?? new NullSearchService(),
            new OperationScopeFactory(),
            new NullAutomationRuleEngine(),
            currentUserService ?? _currentUserService,
            embeddingProvider);
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

file sealed class NullAutomationRuleEngine : IAutomationRuleEngine
{
    public Task<IReadOnlyCollection<AutomationExecution>> ExecuteAsync(AutomationEvent automationEvent, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<AutomationExecution>>(Array.Empty<AutomationExecution>());
}

file sealed class TestWorkspaceContext : IWorkspaceContext
{
    public Guid WorkspaceId { get; } = TenancyDefaults.DefaultWorkspaceId;
}

file sealed class FixedCurrentUserService : ICurrentUserService
{
    private readonly Guid _userId;

    public FixedCurrentUserService(Guid userId)
    {
        _userId = userId;
    }

    public Guid GetCurrentUserId() => _userId;
}
