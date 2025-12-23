using Aion.Domain;
using Aion.Infrastructure;
using Aion.Infrastructure.Observability;
using Aion.Infrastructure.Services;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aion.Benchmarks;

[SimpleJob(RuntimeMoniker.Net100, baseline: true)]
[MemoryDiagnoser]
public class DataEngineBenchmarks : IAsyncDisposable
{
    private SqliteConnection? _connection;
    private AionDbContext? _context;
    private AionDataEngine? _engine;
    private Guid _insertTableId;
    private Guid _queryTableId;
    private QuerySpec _pageQuery = default!;
    private string _searchQuery = string.Empty;
    private IReadOnlyList<IDictionary<string, object?>> _insertBatch = Array.Empty<IDictionary<string, object?>>();

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync().ConfigureAwait(false);

        var options = new DbContextOptionsBuilder<AionDbContext>()
            .UseSqlite(_connection)
            .UseLoggerFactory(NullLoggerFactory.Instance)
            .Options;

        _context = new AionDbContext(options);
        await _context.Database.MigrateAsync().ConfigureAwait(false);

        _engine = new AionDataEngine(_context, NullLogger<AionDataEngine>.Instance, new NullSearchService(), new OperationScopeFactory(), new NullAutomationRuleEngine());

        await CreateTablesAsync().ConfigureAwait(false);
        await SeedQueryDataAsync().ConfigureAwait(false);
        PrepareInsertBatch();
    }

    [IterationSetup(Targets = nameof(Insert_10k_records))]
    public async Task ResetInsertTableAsync()
    {
        if (_context is null)
        {
            return;
        }

        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM RecordSearch WHERE EntityTypeId = {0}; DELETE FROM RecordIndexes WHERE EntityTypeId = {0}; DELETE FROM Records WHERE EntityTypeId = {0};",
            _insertTableId).ConfigureAwait(false);
    }

    [Benchmark(Description = "Insert 10k records")]
    public async Task Insert_10k_records()
    {
        ArgumentNullException.ThrowIfNull(_engine);

        foreach (var payload in _insertBatch)
        {
            await _engine.InsertAsync(_insertTableId, payload).ConfigureAwait(false);
        }
    }

    [Benchmark(Description = "Paginated query (50 items)")]
    public async Task Query_paginated()
    {
        ArgumentNullException.ThrowIfNull(_engine);
        _ = await _engine.QueryAsync(_queryTableId, _pageQuery).ConfigureAwait(false);
    }

    [Benchmark(Description = "FTS search (MATCH)")]
    public async Task Search_match()
    {
        ArgumentNullException.ThrowIfNull(_engine);
        _ = await _engine.SearchAsync(_queryTableId, _searchQuery, new SearchOptions { Take = 20, Skip = 0 }).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync().ConfigureAwait(false);
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task CreateTablesAsync()
    {
        ArgumentNullException.ThrowIfNull(_engine);

        var baseFields = new List<SFieldDefinition>
        {
            new()
            {
                Name = "title",
                Label = "Title",
                DataType = FieldDataType.Text,
                IsRequired = true,
                IsSearchable = true,
                IsSortable = true,
                IsIndexed = true
            },
            new()
            {
                Name = "priority",
                Label = "Priority",
                DataType = FieldDataType.Number,
                IsIndexed = true,
                IsSortable = true
            },
            new()
            {
                Name = "content",
                Label = "Content",
                DataType = FieldDataType.Note,
                IsSearchable = true
            }
        };

        var insertTable = new STable
        {
            Name = "bench_inserts",
            DisplayName = "Bench Inserts",
            Description = "Benchmark insert throughput",
            SupportsSoftDelete = false,
            Fields = baseFields.Select(f => CloneField(f)).ToList()
        };

        var queryTable = new STable
        {
            Name = "bench_queries",
            DisplayName = "Bench Queries",
            Description = "Benchmark query/search",
            SupportsSoftDelete = false,
            Fields = baseFields.Select(f => CloneField(f)).ToList()
        };

        var insertResult = await _engine.CreateTableAsync(insertTable).ConfigureAwait(false);
        var queryResult = await _engine.CreateTableAsync(queryTable).ConfigureAwait(false);

        _insertTableId = insertResult.Id;
        _queryTableId = queryResult.Id;
    }

    private async Task SeedQueryDataAsync()
    {
        ArgumentNullException.ThrowIfNull(_engine);

        var batch = Enumerable.Range(0, 5000)
            .Select(CreatePayload)
            .ToList();

        foreach (var payload in batch)
        {
            await _engine.InsertAsync(_queryTableId, payload).ConfigureAwait(false);
        }

        _pageQuery = new QuerySpec
        {
            OrderBy = "priority",
            Descending = true,
            Skip = 200,
            Take = 50
        };

        _searchQuery = "note";
    }

    private void PrepareInsertBatch()
        => _insertBatch = Enumerable.Range(0, 10_000)
            .Select(CreatePayload)
            .ToList();

    private static IDictionary<string, object?> CreatePayload(int index)
        => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["title"] = $"Record {index}",
            ["priority"] = index % 10,
            ["content"] = $"This is a sample note payload number {index} used for benchmarks."
        };

    private static SFieldDefinition CloneField(SFieldDefinition field)
        => new()
        {
            Name = field.Name,
            Label = field.Label,
            DataType = field.DataType,
            IsRequired = field.IsRequired,
            IsSearchable = field.IsSearchable,
            IsSortable = field.IsSortable,
            IsIndexed = field.IsIndexed
        };

    private sealed class NullSearchService : ISearchService
    {
        public Task<IEnumerable<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<SearchHit>>(Array.Empty<SearchHit>());

        public Task IndexNoteAsync(S_Note note, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task IndexRecordAsync(F_Record record, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task IndexFileAsync(F_File file, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemoveAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NullAutomationRuleEngine : IAutomationRuleEngine
    {
        public Task<IReadOnlyCollection<AutomationExecution>> ExecuteAsync(AutomationEvent automationEvent, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<AutomationExecution>>(Array.Empty<AutomationExecution>());
    }
}
