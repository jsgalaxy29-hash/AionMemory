using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aion.AI;
using Aion.Domain;
using Aion.Infrastructure.Services.Automation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class MetadataService : IMetadataService
{
    private readonly AionDbContext _db;
    private readonly ILogger<MetadataService> _logger;

    public MetadataService(AionDbContext db, ILogger<MetadataService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<S_EntityType> AddEntityTypeAsync(Guid moduleId, S_EntityType entityType, CancellationToken cancellationToken = default)
    {
        var module = await _db.Modules.FirstOrDefaultAsync(m => m.Id == moduleId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Module {moduleId} not found");

        entityType.ModuleId = moduleId;
        await _db.EntityTypes.AddAsync(entityType, cancellationToken).ConfigureAwait(false);
        TouchModule(module);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Entity type {Name} added to module {Module}", entityType.Name, moduleId);
        return entityType;
    }

    public async Task<S_Module> CreateModuleAsync(S_Module module, CancellationToken cancellationToken = default)
    {
        InitializeModuleMetadata(module);
        await _db.Modules.AddAsync(module, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Module {Name} created", module.Name);
        return module;
    }

    public async Task<IEnumerable<S_Module>> GetModulesAsync(CancellationToken cancellationToken = default)
        => await _db.Modules
            .Include(m => m.EntityTypes).ThenInclude(e => e.Fields)
            .Include(m => m.Reports)
            .Include(m => m.AutomationRules).ThenInclude(r => r.Actions)
            .Include(m => m.AutomationRules).ThenInclude(r => r.Conditions)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private static void InitializeModuleMetadata(S_Module module)
    {
        var now = DateTimeOffset.UtcNow;
        module.ModifiedAt = module.ModifiedAt == default ? now : module.ModifiedAt;
        module.Version = module.Version <= 0 ? 1 : module.Version;
    }

    private static void TouchModule(S_Module module)
    {
        var now = DateTimeOffset.UtcNow;
        module.ModifiedAt = now;
        module.Version = Math.Max(1, module.Version + 1);
    }
}

public sealed class AionDataEngine : IAionDataEngine, IDataEngine
{
    private const double FullTextWeight = 0.65;
    private const double SemanticWeight = 0.35;
    private static readonly JsonSerializerOptions EmbeddingSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly AionDbContext _db;
    private readonly ILogger<AionDataEngine> _logger;
    private readonly ISearchService _search;
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly IOperationScopeFactory _operationScopeFactory;
    private readonly IAutomationRuleEngine _automationRuleEngine;
    private readonly ICurrentUserService _currentUserService;

    public AionDataEngine(
        AionDbContext db,
        ILogger<AionDataEngine> logger,
        ISearchService search,
        IOperationScopeFactory operationScopeFactory,
        IAutomationRuleEngine automationRuleEngine,
        ICurrentUserService currentUserService,
        IEmbeddingProvider? embeddingProvider = null)
    {
        _db = db;
        _logger = logger;
        _search = search;
        _operationScopeFactory = operationScopeFactory;
        _automationRuleEngine = automationRuleEngine;
        _currentUserService = currentUserService;
        _embeddingProvider = embeddingProvider;
    }

    private OperationMetricsScope BeginOperation(string operationName, Guid? tableId = null, Guid? recordId = null)
    {
        var operationScope = _operationScopeFactory.Start(operationName);
        var scope = new Dictionary<string, object?>
        {
            ["Operation"] = operationName,
            ["CorrelationId"] = operationScope.Context.CorrelationId,
            ["OperationId"] = operationScope.Context.OperationId
        };

        if (tableId.HasValue)
        {
            scope["TableId"] = tableId.Value;
        }

        if (recordId.HasValue)
        {
            scope["RecordId"] = recordId.Value;
        }

        var logScope = _logger.BeginScope(scope) ?? NullScope.Instance;
        return new OperationMetricsScope(operationScope, logScope, operationName);
    }

    private sealed class OperationMetricsScope : IDisposable
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly IOperationScope _operationScope;
        private readonly IDisposable _logScope;

        public OperationMetricsScope(IOperationScope operationScope, IDisposable logScope, string operationName)
        {
            _operationScope = operationScope;
            _logScope = logScope;
            OperationName = operationName;
        }

        public string OperationName { get; }
        public OperationContext Context => _operationScope.Context;
        public TimeSpan Elapsed => _stopwatch.Elapsed;

        public void Dispose()
        {
            _stopwatch.Stop();
            _logScope.Dispose();
            _operationScope.Dispose();
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }

    private async Task TriggerAutomationAsync(AutomationEvent automationEvent, CancellationToken cancellationToken)
    {
        if (AutomationExecutionContext.IsSuppressed)
        {
            return;
        }

        await _automationRuleEngine.ExecuteAsync(automationEvent, cancellationToken).ConfigureAwait(false);
    }

    public async Task<STable> CreateTableAsync(STable table, CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation("DataEngine.CreateTable", table.Id);

        var added = EnsureBasicViews(table);
        NormalizeTableDefinition(table);
        ValidateTableDefinition(table);

        await _db.Tables.AddAsync(table, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Table {Table} created with {FieldCount} fields in {ElapsedMs}ms", table.Name, table.Fields.Count, operation.Elapsed.TotalMilliseconds);
        if (added > 0)
        {
            _logger.LogInformation("{ViewCount} default view(s) created for table {Table} in {ElapsedMs}ms", added, table.Name, operation.Elapsed.TotalMilliseconds);
        }
        return table;
    }

    public async Task<IEnumerable<SViewDefinition>> GenerateSimpleViewsAsync(Guid tableId, CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation("DataEngine.GenerateViews", tableId);

        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        var added = EnsureBasicViews(table);
        if (added > 0)
        {
            NormalizeTableDefinition(table);
            _db.Tables.Update(table);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Generated {Count} simple view(s) for table {Table} in {ElapsedMs}ms", added, table.Name, operation.Elapsed.TotalMilliseconds);
        }

        return table.Views;
    }

    public async Task<STable?> GetTableAsync(Guid tableId, CancellationToken cancellationToken = default)
        => await _db.Tables
            .Include(t => t.Fields)
            .Include(t => t.Views)
            .FirstOrDefaultAsync(t => t.Id == tableId, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IEnumerable<STable>> GetTablesAsync(CancellationToken cancellationToken = default)
        => await _db.Tables
            .Include(t => t.Fields)
            .Include(t => t.Views)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<F_Record?> GetAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default)
    {
        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        return await FilterByTable(_db.Records.AsNoTracking(), table)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ResolvedRecord?> GetResolvedAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default)
    {
        var record = await GetAsync(tableId, id, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        return await BuildResolvedRecordAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public Task<F_Record> InsertAsync(Guid tableId, string dataJson, CancellationToken cancellationToken = default)
        => InsertAsync(tableId, ParseJsonPayload(dataJson), cancellationToken);

    public async Task<F_Record> InsertAsync(Guid tableId, IDictionary<string, object?> data, CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation("DataEngine.Insert", tableId);

        var (table, validated) = await ValidateRecordAsync(tableId, data, null, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;

        var record = new F_Record
        {
            TableId = tableId,
            DataJson = validated.DataJson,
            CreatedAt = now,
            ModifiedAt = now,
            Version = 1
        };

        await _db.Records.AddAsync(record, cancellationToken).ConfigureAwait(false);
        await UpsertRecordIndexesAsync(table, record, validated.Values, cancellationToken).ConfigureAwait(false);
        await UpsertRecordEmbeddingAsync(table, record, validated.Values, cancellationToken).ConfigureAwait(false);
        await AddAuditEntryAsync(tableId, record.Id, ChangeType.Create, record.DataJson, null, record.Version, now, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Record {RecordId} inserted for table {TableId} in {ElapsedMs}ms", record.Id, tableId, operation.Elapsed.TotalMilliseconds);
        await _search.IndexRecordAsync(record, cancellationToken).ConfigureAwait(false);

        var automationPayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["tableId"] = tableId,
            ["recordId"] = record.Id,
            ["data"] = validated.Values
        };

        var automationEvent = new AutomationEvent("record.created", AutomationTriggerType.OnCreate, automationPayload, null, table.Id);
        await TriggerAutomationAsync(automationEvent, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public Task<F_Record> UpdateAsync(Guid tableId, Guid id, string dataJson, CancellationToken cancellationToken = default)
        => UpdateAsync(tableId, id, ParseJsonPayload(dataJson), cancellationToken);

    public async Task<F_Record> UpdateAsync(Guid tableId, Guid id, IDictionary<string, object?> data, CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation("DataEngine.Update", tableId, id);

        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        var record = await FilterByTable(_db.Records, table).FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Record {id} not found for table {tableId}");

        var previousDataJson = record.DataJson;
        var validated = await ValidateRecordAsync(table, data, id, cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        record.DataJson = validated.DataJson;
        record.ModifiedAt = now;
        record.UpdatedAt = now;
        record.Version += 1;
        _db.Records.Update(record);
        await UpsertRecordIndexesAsync(table, record, validated.Values, cancellationToken).ConfigureAwait(false);
        await UpsertRecordEmbeddingAsync(table, record, validated.Values, cancellationToken).ConfigureAwait(false);
        await AddAuditEntryAsync(tableId, id, ChangeType.Update, record.DataJson, previousDataJson, record.Version, now, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Record {RecordId} updated for table {TableId} in {ElapsedMs}ms", id, tableId, operation.Elapsed.TotalMilliseconds);
        await _search.IndexRecordAsync(record, cancellationToken).ConfigureAwait(false);

        var automationPayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["tableId"] = tableId,
            ["recordId"] = record.Id,
            ["data"] = validated.Values
        };
        var automationEvent = new AutomationEvent("record.updated", AutomationTriggerType.OnUpdate, automationPayload, null, table.Id);
        await TriggerAutomationAsync(automationEvent, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task DeleteAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation("DataEngine.Delete", tableId, id);

        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        var record = await FilterByTable(_db.Records, table).FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        var snapshotJson = record.DataJson;
        var now = DateTimeOffset.UtcNow;
        var nextVersion = record.Version + 1;

        if (table.SupportsSoftDelete)
        {
            record.DeletedAt = now;
            record.ModifiedAt = now;
            record.Version = nextVersion;
            _db.Records.Update(record);
        }
        else
        {
            _db.Records.Remove(record);
        }

        await AddAuditEntryAsync(tableId, id, ChangeType.Delete, snapshotJson, snapshotJson, nextVersion, now, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Record {RecordId} deleted for table {TableId} in {ElapsedMs}ms", id, tableId, operation.Elapsed.TotalMilliseconds);
        await _search.RemoveAsync("Record", id, cancellationToken).ConfigureAwait(false);

        var automationPayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["tableId"] = tableId,
            ["recordId"] = id,
            ["data"] = ParseJsonPayload(snapshotJson)
        };
        var automationEvent = new AutomationEvent("record.deleted", AutomationTriggerType.OnDelete, automationPayload, null, table.Id);
        await TriggerAutomationAsync(automationEvent, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<ChangeSet>> GetHistoryAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation("DataEngine.GetHistory", tableId, recordId);

        _ = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        var audits = await _db.RecordAudits.AsNoTracking()
            .Where(a => a.TableId == tableId && a.RecordId == recordId)
            .OrderBy(a => a.Version)
            .ThenBy(a => a.ChangedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var history = audits
            .Select(a => new ChangeSet(a.TableId, a.RecordId, a.ChangeType, a.Version, a.ChangedAt, a.DataJson, a.PreviousDataJson))
            .ToList();

        _logger.LogInformation(
            "History returned {Count} change(s) for record {RecordId} in table {TableId} in {ElapsedMs}ms",
            history.Count,
            recordId,
            tableId,
            operation.Elapsed.TotalMilliseconds);

        return history;
    }

    public async Task<int> CountAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default)
    {
        spec ??= new QuerySpec();
        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        var query = BuildQueryable(table, spec);
        return await query.CountAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<F_Record>> QueryAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation("DataEngine.Query", tableId);

        spec ??= new QuerySpec();
        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        var query = BuildQueryable(table, spec);

        if (spec.Skip.HasValue)
        {
            query = query.Skip(spec.Skip.Value);
        }

        if (spec.Take.HasValue)
        {
            query = query.Take(spec.Take.Value);
        }

        var results = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Query returned {Count} record(s) for table {TableId} in {ElapsedMs}ms", results.Count, tableId, operation.Elapsed.TotalMilliseconds);
        return results;
    }

    public async Task<IEnumerable<ResolvedRecord>> QueryResolvedAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation("DataEngine.QueryResolved", tableId);

        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        var results = await QueryAsync(tableId, spec, cancellationToken).ConfigureAwait(false);
        var resolved = new List<ResolvedRecord>();

        foreach (var record in results)
        {
            var resolvedRecord = await BuildResolvedRecordAsync(record, table, cancellationToken).ConfigureAwait(false);
            resolved.Add(resolvedRecord);
        }

        _logger.LogInformation("Resolved query returned {Count} record(s) for table {TableId} in {ElapsedMs}ms", resolved.Count, tableId, operation.Elapsed.TotalMilliseconds);
        return resolved;
    }

    public async Task<IEnumerable<RecordSearchHit>> SearchAsync(Guid tableId, string query, SearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<RecordSearchHit>();
        }

        using var operation = BeginOperation("DataEngine.Search", tableId);

        var validated = NormalizeSearchOptions(options);
        _ = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        var hits = await ExecuteFullTextSearchAsync(tableId, query, validated, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Search returned {Count} result(s) for table {TableId} in {ElapsedMs}ms",
            hits.Count,
            tableId,
            operation.Elapsed.TotalMilliseconds);
        return hits.Select(h => new RecordSearchHit(h.RecordId, h.Score, h.Snippet));
    }

    public async Task<IEnumerable<RecordSearchHit>> SearchSmartAsync(Guid tableId, string query, SearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<RecordSearchHit>();
        }

        using var operation = BeginOperation("DataEngine.SearchSmart", tableId);

        var validated = NormalizeSearchOptions(options);
        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        var expandedOptions = validated with
        {
            Skip = 0,
            Take = Math.Min(SearchOptions.MaxPageSize, Math.Max(validated.Take * 3, validated.Take))
        };

        var fullTextHits = await ExecuteFullTextSearchAsync(tableId, query, expandedOptions, cancellationToken).ConfigureAwait(false);
        var semanticScores = await ComputeSemanticScoresAsync(table.Id, query, cancellationToken).ConfigureAwait(false);
        var combined = await CombineSearchSignalsAsync(table.Id, fullTextHits, semanticScores, validated, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Smart search returned {Count} result(s) for table {TableId} in {ElapsedMs}ms",
            combined.Count,
            tableId,
            operation.Elapsed.TotalMilliseconds);
        return combined;
    }

    public async Task<KnowledgeEdge> LinkRecordsAsync(
        Guid fromTableId,
        Guid fromRecordId,
        Guid toTableId,
        Guid toRecordId,
        KnowledgeRelationType relationType,
        CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation("DataEngine.LinkRecords", fromTableId, fromRecordId);

        var fromTable = await GetTableAsync(fromTableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {fromTableId} not found");
        var toTable = await GetTableAsync(toTableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {toTableId} not found");

        var fromRecord = await FilterByTable(_db.Records.AsNoTracking(), fromTable)
            .FirstOrDefaultAsync(r => r.Id == fromRecordId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Record {fromRecordId} not found for table {fromTableId}");

        var toRecord = await FilterByTable(_db.Records.AsNoTracking(), toTable)
            .FirstOrDefaultAsync(r => r.Id == toRecordId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Record {toRecordId} not found for table {toTableId}");

        var fromNode = await UpsertKnowledgeNodeAsync(fromTable, fromRecord, cancellationToken).ConfigureAwait(false);
        var toNode = await UpsertKnowledgeNodeAsync(toTable, toRecord, cancellationToken).ConfigureAwait(false);

        var existing = await _db.KnowledgeEdges.FirstOrDefaultAsync(
            e => e.FromNodeId == fromNode.Id && e.ToNodeId == toNode.Id && e.RelationType == relationType,
            cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        var edge = new KnowledgeEdge
        {
            FromNodeId = fromNode.Id,
            ToNodeId = toNode.Id,
            RelationType = relationType,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _db.KnowledgeEdges.AddAsync(edge, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Knowledge edge {EdgeId} created from {FromRecord} to {ToRecord} with relation {Relation} in {ElapsedMs}ms",
            edge.Id,
            fromRecordId,
            toRecordId,
            relationType,
            operation.Elapsed.TotalMilliseconds);

        return edge;
    }

    public async Task<KnowledgeGraphSlice> GetKnowledgeGraphAsync(
        Guid tableId,
        Guid recordId,
        int depth = 1,
        CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation("DataEngine.GetKnowledgeGraph", tableId, recordId);

        if (depth < 0)
        {
            throw new InvalidOperationException("Depth must be zero or greater.");
        }

        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        var record = await FilterByTable(_db.Records.AsNoTracking(), table)
            .FirstOrDefaultAsync(r => r.Id == recordId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Record {recordId} not found for table {tableId}");

        var rootNode = await UpsertKnowledgeNodeAsync(table, record, cancellationToken).ConfigureAwait(false);

        var nodes = new Dictionary<Guid, KnowledgeNode>
        {
            [rootNode.Id] = rootNode
        };
        var edges = new Dictionary<Guid, KnowledgeEdge>();
        var frontier = new Queue<(KnowledgeNode Node, int Depth)>();
        frontier.Enqueue((rootNode, 0));

        while (frontier.Count > 0)
        {
            var (current, currentDepth) = frontier.Dequeue();
            if (currentDepth >= depth)
            {
                continue;
            }

            var connected = await _db.KnowledgeEdges.AsNoTracking()
                .Where(e => e.FromNodeId == current.Id || e.ToNodeId == current.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var edge in connected)
            {
                if (!edges.ContainsKey(edge.Id))
                {
                    edges[edge.Id] = edge;
                }

                var neighborId = edge.FromNodeId == current.Id ? edge.ToNodeId : edge.FromNodeId;
                if (nodes.ContainsKey(neighborId))
                {
                    continue;
                }

                var neighbor = await _db.KnowledgeNodes.AsNoTracking()
                    .FirstOrDefaultAsync(n => n.Id == neighborId, cancellationToken)
                    .ConfigureAwait(false);

                if (neighbor is null)
                {
                    continue;
                }

                nodes[neighbor.Id] = neighbor;
                frontier.Enqueue((neighbor, currentDepth + 1));
            }
        }

        var slice = new KnowledgeGraphSlice(rootNode, nodes.Values.ToList(), edges.Values.ToList());
        _logger.LogInformation(
            "Knowledge graph slice for record {RecordId} returned {NodeCount} node(s) and {EdgeCount} edge(s) in {ElapsedMs}ms",
            recordId,
            nodes.Count,
            edges.Count,
            operation.Elapsed.TotalMilliseconds);

        return slice;
    }

    private sealed record FullTextHit(Guid RecordId, double Score, string Snippet, string Content);

    private async Task<List<FullTextHit>> ExecuteFullTextSearchAsync(Guid tableId, string query, SearchOptions options, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var command = BuildSearchCommand(connection, query, tableId, options);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<FullTextHit>();

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var recordId = reader.GetGuid(0);
                var score = reader.IsDBNull(1) ? 0d : reader.GetDouble(1);
                var snippet = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var content = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);

                if (string.IsNullOrWhiteSpace(snippet))
                {
                    snippet = BuildFallbackSnippet(content, options.HighlightBefore, options.HighlightAfter);
                }

                results.Add(new FullTextHit(recordId, score, snippet, content));
            }

            return results;
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static SearchOptions NormalizeSearchOptions(SearchOptions? options)
    {
        var normalized = options ?? new SearchOptions();
        var take = Math.Clamp(normalized.Take, 1, SearchOptions.MaxPageSize);
        var defaults = new SearchOptions();
        return normalized with
        {
            Take = take,
            Skip = Math.Max(0, normalized.Skip),
            HighlightBefore = string.IsNullOrEmpty(normalized.HighlightBefore) ? defaults.HighlightBefore : normalized.HighlightBefore,
            HighlightAfter = string.IsNullOrEmpty(normalized.HighlightAfter) ? defaults.HighlightAfter : normalized.HighlightAfter,
            SnippetTokens = normalized.SnippetTokens <= 0 ? SearchOptions.DefaultSnippetTokens : normalized.SnippetTokens
        };
    }

    private static DbCommand BuildSearchCommand(DbConnection connection, string query, Guid tableId, SearchOptions options)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
SELECT RecordId,
       COALESCE(1.0 / (bm25(RecordSearch) + 1), 0) AS Score,
       snippet(RecordSearch, 2, $before, $after, ' … ', $tokens) AS Snippet,
       Content
FROM RecordSearch
WHERE EntityTypeId = $tableId AND RecordSearch MATCH $query
ORDER BY Score DESC, RecordId
LIMIT $take OFFSET $skip;
""";

        AddParameter(command, "$tableId", tableId);
        AddParameter(command, "$query", query);
        AddParameter(command, "$before", options.HighlightBefore);
        AddParameter(command, "$after", options.HighlightAfter);
        AddParameter(command, "$tokens", options.SnippetTokens);
        AddParameter(command, "$take", options.Take);
        AddParameter(command, "$skip", options.Skip);

        return command;
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string BuildFallbackSnippet(string content, string before, string after)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        const int limit = 160;
        var snippet = content.Length <= limit ? content : content[..limit] + "…";
        return $"{before}{snippet}{after}";
    }

    private async Task<List<RecordSearchHit>> CombineSearchSignalsAsync(
        Guid tableId,
        IReadOnlyList<FullTextHit> fullTextHits,
        IReadOnlyDictionary<Guid, double> semanticScores,
        SearchOptions options,
        CancellationToken cancellationToken)
    {
        var combined = new Dictionary<Guid, (double Score, string Snippet)>();

        foreach (var hit in fullTextHits)
        {
            var semanticScore = semanticScores.TryGetValue(hit.RecordId, out var semantic) ? semantic : 0d;
            combined[hit.RecordId] = (CombineScores(hit.Score, semanticScore), hit.Snippet);
        }

        var semanticOnlyIds = semanticScores.Keys.Except(combined.Keys).ToList();
        if (semanticOnlyIds.Count > 0)
        {
            var contents = await LoadRecordContentsAsync(tableId, semanticOnlyIds, cancellationToken).ConfigureAwait(false);
            foreach (var recordId in semanticOnlyIds)
            {
                var snippet = contents.TryGetValue(recordId, out var content)
                    ? BuildFallbackSnippet(content, options.HighlightBefore, options.HighlightAfter)
                    : string.Empty;
                combined[recordId] = (CombineScores(0, semanticScores[recordId]), snippet);
            }
        }

        return combined
            .OrderByDescending(kv => kv.Value.Score)
            .ThenBy(kv => kv.Key)
            .Skip(options.Skip)
            .Take(options.Take)
            .Select(kv => new RecordSearchHit(kv.Key, kv.Value.Score, kv.Value.Snippet))
            .ToList();
    }

    private async Task<Dictionary<Guid, string>> LoadRecordContentsAsync(Guid tableId, IReadOnlyCollection<Guid> recordIds, CancellationToken cancellationToken)
    {
        if (recordIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var ids = recordIds.ToList();
        return await _db.RecordSearch.AsNoTracking()
            .Where(r => r.TableId == tableId && ids.Contains(r.RecordId))
            .ToDictionaryAsync(r => r.RecordId, r => r.Content, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Dictionary<Guid, double>> ComputeSemanticScoresAsync(Guid tableId, string query, CancellationToken cancellationToken)
    {
        if (_embeddingProvider is null)
        {
            return new Dictionary<Guid, double>();
        }

        float[] queryEmbedding;
        try
        {
            queryEmbedding = (await _embeddingProvider.EmbedAsync(query, cancellationToken).ConfigureAwait(false)).Vector;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Smart search falling back to FTS only for table {TableId}", tableId);
            return new Dictionary<Guid, double>();
        }

        var entries = await _db.Embeddings.AsNoTracking()
            .Where(e => e.TableId == tableId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var scores = new Dictionary<Guid, double>();
        foreach (var entry in entries)
        {
            var vector = ParseEmbedding(entry.Vector);
            if (vector is null)
            {
                continue;
            }

            var similarity = ComputeCosineSimilarity(queryEmbedding, vector);
            if (similarity > 0)
            {
                scores[entry.RecordId] = similarity;
            }
        }

        return scores;
    }

    private async Task UpsertRecordEmbeddingAsync(STable table, F_Record record, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken)
    {
        var serialized = await TryGenerateEmbeddingAsync(table, values, cancellationToken).ConfigureAwait(false);
        if (serialized is null)
        {
            return;
        }

        var existing = await _db.Embeddings.FirstOrDefaultAsync(e => e.RecordId == record.Id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await _db.Embeddings.AddAsync(new F_RecordEmbedding
            {
                TableId = table.Id,
                RecordId = record.Id,
                Vector = serialized
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        existing.TableId = table.Id;
        existing.Vector = serialized;
        _db.Embeddings.Update(existing);
    }

    private async Task<string?> TryGenerateEmbeddingAsync(STable table, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken)
    {
        if (_embeddingProvider is null)
        {
            return null;
        }

        var document = BuildEmbeddingDocument(table, values);
        if (string.IsNullOrWhiteSpace(document))
        {
            return null;
        }

        try
        {
            var embedding = await _embeddingProvider.EmbedAsync(document, cancellationToken).ConfigureAwait(false);
            return SerializeEmbedding(embedding.Vector);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to generate embedding for table {TableId}", table.Id);
            return null;
        }
    }

    private static string BuildEmbeddingDocument(STable table, IReadOnlyDictionary<string, object?> values)
    {
        var parts = new List<string>
        {
            string.IsNullOrWhiteSpace(table.DisplayName) ? table.Name : table.DisplayName
        };

        foreach (var field in table.Fields.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!values.TryGetValue(field.Name, out var value) || value is null)
            {
                continue;
            }

            parts.Add($"{(string.IsNullOrWhiteSpace(field.Label) ? field.Name : field.Label)}: {value}");
        }

        return string.Join(Environment.NewLine, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string SerializeEmbedding(float[] vector)
        => JsonSerializer.Serialize(vector, EmbeddingSerializerOptions);

    private static float[]? ParseEmbedding(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<float[]>(serialized, EmbeddingSerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double CombineScores(double fullTextScore, double semanticScore)
    {
        var positiveSemantic = Math.Max(0, semanticScore);
        if (fullTextScore <= 0)
        {
            return positiveSemantic;
        }

        if (positiveSemantic <= 0)
        {
            return fullTextScore;
        }

        return (fullTextScore * FullTextWeight) + (positiveSemantic * SemanticWeight);
    }

    private static double ComputeCosineSimilarity(IReadOnlyList<float> vector1, IReadOnlyList<float> vector2)
    {
        if (vector1.Count == 0 || vector2.Count == 0 || vector1.Count != vector2.Count)
        {
            return 0;
        }

        double dot = 0;
        double normA = 0;
        double normB = 0;

        for (var i = 0; i < vector1.Count; i++)
        {
            dot += vector1[i] * vector2[i];
            normA += vector1[i] * vector1[i];
            normB += vector2[i] * vector2[i];
        }

        if (normA <= 0 || normB <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private async Task AddAuditEntryAsync(Guid tableId, Guid recordId, ChangeType changeType, string dataJson, string? previousDataJson, long version, DateTimeOffset changedAt, CancellationToken cancellationToken)
    {
        var audit = new F_RecordAudit
        {
            TableId = tableId,
            RecordId = recordId,
            UserId = _currentUserService.GetCurrentUserId(),
            ChangeType = changeType,
            Version = version,
            DataJson = dataJson,
            PreviousDataJson = previousDataJson,
            ChangedAt = changedAt
        };

        await _db.RecordAudits.AddAsync(audit, cancellationToken).ConfigureAwait(false);
    }

    private static IQueryable<F_Record> FilterByTable(IQueryable<F_Record> query, STable table)
    {
        query = query.Where(r => r.TableId == table.Id);

        if (table.SupportsSoftDelete)
        {
            query = query.Where(r => r.DeletedAt == null);
        }

        return query;
    }

    private IQueryable<F_Record> BuildQueryable(STable table, QuerySpec spec)
    {
        var query = FilterByTable(_db.Records.AsQueryable(), table);
        query = ApplyViewFilters(query, table, spec.View);
        query = ApplyStructuredFilters(query, table, spec.Filters);
        query = ApplyFullTextFilter(query, table.Id, spec.FullText);
        query = ApplyOrdering(query, table, spec);
        return query;
    }

    private IQueryable<F_Record> ApplyViewFilters(IQueryable<F_Record> query, STable table, string? viewName)
    {
        var equals = ResolveViewFilter(viewName, table);
        if (equals is null)
        {
            return query;
        }

        var filters = equals
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .Select(kv => new QueryFilter(kv.Key, QueryFilterOperator.Equals, kv.Value));

        return ApplyStructuredFilters(query, table, filters);
    }

    private IQueryable<F_Record> ApplyStructuredFilters(IQueryable<F_Record> query, STable table, IEnumerable<QueryFilter>? filters)
    {
        if (filters is null)
        {
            return query;
        }

        foreach (var filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter.Field))
            {
                continue;
            }

            var field = table.Fields.FirstOrDefault(f => string.Equals(f.Name, filter.Field, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Field '{filter.Field}' is not defined for table {table.Name}");

            query = ApplyFilter(query, table, field, filter);
        }

        return query;
    }

    private IQueryable<F_Record> ApplyFilter(IQueryable<F_Record> query, STable table, SFieldDefinition field, QueryFilter filter)
    {
        var indexes = _db.RecordIndexes
            .Where(i => i.TableId == table.Id && i.FieldName == field.Name);

        switch (field.DataType)
        {
            case FieldDataType.Number or FieldDataType.Decimal or FieldDataType.Int:
            {
                if (filter.Value is null)
                {
                    return query;
                }

                var numeric = Convert.ToDecimal(filter.Value, CultureInfo.InvariantCulture);
                indexes = filter.Operator switch
                {
                    QueryFilterOperator.Equals => indexes.Where(i => i.NumberValue == numeric),
                    QueryFilterOperator.GreaterThan => indexes.Where(i => i.NumberValue > numeric),
                    QueryFilterOperator.GreaterThanOrEqual => indexes.Where(i => i.NumberValue >= numeric),
                    QueryFilterOperator.LessThan => indexes.Where(i => i.NumberValue < numeric),
                    QueryFilterOperator.LessThanOrEqual => indexes.Where(i => i.NumberValue <= numeric),
                    _ => throw new InvalidOperationException($"Operator {filter.Operator} is not supported for numeric fields")
                };
                break;
            }
            case FieldDataType.Boolean:
            {
                if (filter.Value is null)
                {
                    return query;
                }

                var boolean = Convert.ToBoolean(filter.Value, CultureInfo.InvariantCulture);
                indexes = filter.Operator == QueryFilterOperator.Equals
                    ? indexes.Where(i => i.BoolValue == boolean)
                    : throw new InvalidOperationException("Only Equals is supported for boolean filters");
                break;
            }
            case FieldDataType.Date or FieldDataType.DateTime:
            {
                if (filter.Value is null)
                {
                    return query;
                }

                var parsed = ParseDateValue(filter.Value);
                indexes = filter.Operator switch
                {
                    QueryFilterOperator.Equals => indexes.Where(i => i.DateValue == parsed),
                    QueryFilterOperator.GreaterThan => indexes.Where(i => i.DateValue > parsed),
                    QueryFilterOperator.GreaterThanOrEqual => indexes.Where(i => i.DateValue >= parsed),
                    QueryFilterOperator.LessThan => indexes.Where(i => i.DateValue < parsed),
                    QueryFilterOperator.LessThanOrEqual => indexes.Where(i => i.DateValue <= parsed),
                    _ => throw new InvalidOperationException($"Operator {filter.Operator} is not supported for date filters")
                };
                break;
            }
            default:
            {
                var stringValue = filter.Value?.ToString();
                if (string.IsNullOrWhiteSpace(stringValue))
                {
                    return query;
                }

                indexes = filter.Operator switch
                {
                    QueryFilterOperator.Equals => indexes.Where(i => i.StringValue == stringValue),
                    QueryFilterOperator.Contains => indexes.Where(i => i.StringValue != null && EF.Functions.Like(i.StringValue, $"%{EscapeLike(stringValue)}%")),
                    _ => throw new InvalidOperationException($"Operator {filter.Operator} is not supported for text filters")
                };
                break;
            }
        }

        return query.Where(r => indexes.Any(i => i.RecordId == r.Id));
    }

    private static string EscapeLike(string value)
        => value.Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);

    private static DateTimeOffset ParseDateValue(object value)
    {
        if (value is JsonElement element)
        {
            value = ExtractValue(element) ?? value;
        }

        if (value is DateTimeOffset dto)
        {
            return dto.ToUniversalTime();
        }

        if (value is DateTime dt)
        {
            return new DateTimeOffset(dt.ToUniversalTime());
        }

        if (value is string s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        throw new InvalidOperationException("Invalid date filter value");
    }

    private IQueryable<F_Record> ApplyFullTextFilter(IQueryable<F_Record> query, Guid tableId, string? fullText)
    {
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return query;
        }

        var fts = _db.RecordSearch.FromSqlRaw(
            "SELECT RecordId, EntityTypeId, Content FROM RecordSearch WHERE EntityTypeId = {0} AND RecordSearch MATCH {1}",
            tableId,
            fullText);

        return query.Where(r => fts.Any(s => s.RecordId == r.Id));
    }

    private IQueryable<F_Record> ApplyOrdering(IQueryable<F_Record> query, STable table, QuerySpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.OrderBy))
        {
            return query.OrderByDescending(r => r.CreatedAt);
        }

        var field = table.Fields.FirstOrDefault(f => string.Equals(f.Name, spec.OrderBy, StringComparison.OrdinalIgnoreCase));
        if (field is null || !field.IsSortable)
        {
            return query.OrderByDescending(r => r.CreatedAt);
        }

        var indexes = _db.RecordIndexes.Where(i => i.TableId == table.Id && i.FieldName == field.Name);

        return field.DataType switch
        {
            FieldDataType.Number or FieldDataType.Decimal => spec.Descending
                ? query.OrderByDescending(r => indexes.Where(i => i.RecordId == r.Id).Select(i => i.NumberValue).FirstOrDefault())
                : query.OrderBy(r => indexes.Where(i => i.RecordId == r.Id).Select(i => i.NumberValue).FirstOrDefault()),
            FieldDataType.Boolean => spec.Descending
                ? query.OrderByDescending(r => indexes.Where(i => i.RecordId == r.Id).Select(i => i.BoolValue).FirstOrDefault())
                : query.OrderBy(r => indexes.Where(i => i.RecordId == r.Id).Select(i => i.BoolValue).FirstOrDefault()),
            FieldDataType.Date or FieldDataType.DateTime => spec.Descending
                ? query.OrderByDescending(r => indexes.Where(i => i.RecordId == r.Id).Select(i => i.DateValue).FirstOrDefault())
                : query.OrderBy(r => indexes.Where(i => i.RecordId == r.Id).Select(i => i.DateValue).FirstOrDefault()),
            _ => spec.Descending
                ? query.OrderByDescending(r => indexes.Where(i => i.RecordId == r.Id).Select(i => i.StringValue).FirstOrDefault())
                : query.OrderBy(r => indexes.Where(i => i.RecordId == r.Id).Select(i => i.StringValue).FirstOrDefault())
        };
    }

    private static void NormalizeTableDefinition(STable table)
    {
        foreach (var field in table.Fields)
        {
            field.TableId = table.Id;
        }

        foreach (var view in table.Views)
        {
            view.TableId = table.Id;
        }
    }

    private static void ValidateTableDefinition(STable table)
    {
        if (string.IsNullOrWhiteSpace(table.Name))
        {
            throw new InvalidOperationException("Table name is required");
        }

        var duplicate = table.Fields
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Field name '{duplicate.Key}' is duplicated in table {table.Name}");
        }

        foreach (var view in table.Views)
        {
            ValidateViewDefinition(view, table);
        }
    }

    private static void ValidateViewDefinition(SViewDefinition view, STable table)
    {
        if (string.IsNullOrWhiteSpace(view.QueryDefinition))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(view.QueryDefinition);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    EnsureFieldExists(table, property.Name);
                }
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid view definition for view {view.Name}", ex);
        }
    }

    private static void EnsureFieldExists(STable? table, string fieldName)
    {
        if (table is null)
        {
            return;
        }

        if (!table.Fields.Any(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Field '{fieldName}' is not defined for table {table.Name}");
        }
    }

    private sealed record ValidatedRecord(string DataJson, IReadOnlyDictionary<string, object?> Values);

    private async Task<(STable Table, ValidatedRecord Validated)> ValidateRecordAsync(Guid tableId, IDictionary<string, object?> values, Guid? recordId, CancellationToken cancellationToken)
    {
        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        var validated = await ValidateRecordAsync(table, values, recordId, cancellationToken).ConfigureAwait(false);
        return (table, validated);
    }

    private async Task<ValidatedRecord> ValidateRecordAsync(STable table, IDictionary<string, object?> values, Guid? recordId, CancellationToken cancellationToken)
    {
        var normalized = await NormalizePayloadAsync(table, values, cancellationToken).ConfigureAwait(false);
        await EnforceConstraintsAsync(table, normalized, recordId, cancellationToken).ConfigureAwait(false);
        return new ValidatedRecord(SerializeValues(normalized), normalized);
    }

    private static object? ExtractValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number when element.TryGetDouble(out var d) => d,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };

    private static void ValidateFieldValue(SFieldDefinition field, object? value)
    {
        if (value is null)
        {
            if (field.IsRequired)
            {
                throw new InvalidOperationException($"Field '{field.Name}' is required");
            }
            return;
        }

        switch (field.DataType)
        {
            case FieldDataType.Text:
            case FieldDataType.Note:
            case FieldDataType.Tags:
            case FieldDataType.Json:
            case FieldDataType.File:
                if (value is not string)
                {
                    throw new InvalidOperationException($"Field '{field.Name}' expects text content");
                }
                break;
            case FieldDataType.Lookup:
                if (value is not string && value is not Guid)
                {
                    throw new InvalidOperationException($"Field '{field.Name}' expects a lookup identifier");
                }
                break;
            case FieldDataType.Number:
                if (value is not long && value is not int)
                {
                    throw new InvalidOperationException($"Field '{field.Name}' expects an integer number");
                }
                break;
            case FieldDataType.Decimal:
                if (value is not double && value is not decimal && value is not float)
                {
                    throw new InvalidOperationException($"Field '{field.Name}' expects a decimal number");
                }
                break;
            case FieldDataType.Boolean:
                if (value is not bool)
                {
                    throw new InvalidOperationException($"Field '{field.Name}' expects a boolean value");
                }
                break;
            case FieldDataType.Date:
            case FieldDataType.DateTime:
                if (value is not string dateString || !DateTimeOffset.TryParse(dateString, out _))
                {
                    throw new InvalidOperationException($"Field '{field.Name}' expects an ISO-8601 date/time string");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field.DataType), $"Unsupported data type {field.DataType}");
        }

        if (value is string str)
        {
            if (field.MinLength.HasValue && str.Length < field.MinLength.Value)
            {
                throw new InvalidOperationException($"Field '{field.Name}' must be at least {field.MinLength} characters");
            }

            if (field.MaxLength.HasValue && str.Length > field.MaxLength.Value)
            {
                throw new InvalidOperationException($"Field '{field.Name}' must be at most {field.MaxLength} characters");
            }

            if (!string.IsNullOrWhiteSpace(field.ValidationPattern) && !Regex.IsMatch(str, field.ValidationPattern))
            {
                throw new InvalidOperationException($"Field '{field.Name}' does not match the expected pattern");
            }

            if (!string.IsNullOrWhiteSpace(field.EnumValues))
            {
                var allowed = field.EnumValues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!allowed.Contains(str, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Field '{field.Name}' must be one of: {string.Join(", ", allowed)}");
                }
            }
        }

        if ((field.DataType == FieldDataType.Number || field.DataType == FieldDataType.Decimal) && value is IConvertible convertible)
        {
            var numeric = Convert.ToDecimal(convertible);
            if (field.MinValue.HasValue && numeric < field.MinValue.Value)
            {
                throw new InvalidOperationException($"Field '{field.Name}' must be greater than or equal to {field.MinValue}");
            }

            if (field.MaxValue.HasValue && numeric > field.MaxValue.Value)
            {
                throw new InvalidOperationException($"Field '{field.Name}' must be less than or equal to {field.MaxValue}");
            }
        }
    }

    private async Task EnforceConstraintsAsync(STable table, IDictionary<string, object?> values, Guid? recordId, CancellationToken cancellationToken)
    {
        foreach (var uniqueField in table.Fields.Where(f => f.IsUnique))
        {
            if (!values.TryGetValue(uniqueField.Name, out var uniqueValue) || uniqueValue is null)
            {
                continue;
            }

            var scoped = _db.RecordIndexes.Where(i => i.TableId == table.Id && i.FieldName == uniqueField.Name);

            scoped = uniqueField.DataType switch
            {
                FieldDataType.Number or FieldDataType.Decimal or FieldDataType.Int when uniqueValue is IConvertible convertible
                    => scoped.Where(i => i.NumberValue == Convert.ToDecimal(convertible, CultureInfo.InvariantCulture)),
                FieldDataType.Boolean
                    => scoped.Where(i => i.BoolValue == Convert.ToBoolean(uniqueValue, CultureInfo.InvariantCulture)),
                FieldDataType.Date or FieldDataType.DateTime
                    => scoped.Where(i => i.DateValue == ParseDateValue(uniqueValue)),
                _ => scoped.Where(i => i.StringValue == uniqueValue.ToString())
            };

            if (recordId.HasValue)
            {
                scoped = scoped.Where(r => r.RecordId != recordId.Value);
            }

            if (await scoped.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"Field '{uniqueField.Name}' must be unique in table {table.Name}");
            }
        }
    }

    private async Task<Dictionary<string, object?>> NormalizePayloadAsync(STable table, IDictionary<string, object?> values, CancellationToken cancellationToken)
    {
        var sourceValues = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in sourceValues.ToArray())
        {
            EnsureFieldExists(table, kv.Key);
        }

        foreach (var field in table.Fields)
        {
            if (!sourceValues.TryGetValue(field.Name, out var value))
            {
                if (field.DefaultValue is not null)
                {
                    value = field.DefaultValue;
                }
                else if (field.IsRequired)
                {
                    throw new InvalidOperationException($"Field '{field.Name}' is required for table {table.Name}");
                }
            }

            if (!sourceValues.TryGetValue(field.Name, out _) && value is null)
            {
                continue;
            }

            var normalizedValue = NormalizeFieldValue(field, value);
            ValidateFieldValue(field, normalizedValue);

            if (!string.IsNullOrWhiteSpace(field.LookupTarget) && normalizedValue is not null)
            {
                var lookupId = ParseGuid(normalizedValue) ?? throw new InvalidOperationException($"Field '{field.Name}' expects a GUID lookup value");
                await EnsureLookupTargetExists(field, lookupId, cancellationToken).ConfigureAwait(false);
                normalizedValue = lookupId;
            }

            normalized[field.Name] = normalizedValue;
        }

        return normalized;
    }

    private static object? NormalizeFieldValue(SFieldDefinition field, object? value)
    {
        if (value is null)
        {
            return null;
        }

        return field.DataType switch
        {
            FieldDataType.Text or FieldDataType.Note or FieldDataType.Tags or FieldDataType.Json or FieldDataType.File
                => NormalizeStringValue(field, value),
            FieldDataType.Lookup
                => ParseGuid(value) ?? throw new InvalidOperationException($"Field '{field.Name}' expects a GUID lookup value"),
            FieldDataType.Number
                => NormalizeIntegerValue(field, value),
            FieldDataType.Decimal
                => NormalizeDecimalValue(field, value),
            FieldDataType.Boolean
                => NormalizeBooleanValue(field, value),
            FieldDataType.Date or FieldDataType.DateTime
                => NormalizeDateValue(field, value),
            _ => throw new ArgumentOutOfRangeException(nameof(field.DataType), $"Unsupported data type {field.DataType}")
        };
    }

    private async Task UpsertRecordIndexesAsync(STable table, F_Record record, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken)
    {
        var existing = _db.RecordIndexes.Where(i => i.RecordId == record.Id);
        _db.RecordIndexes.RemoveRange(existing);

        var indexes = new List<F_RecordIndex>();

        foreach (var field in table.Fields)
        {
            if (!values.TryGetValue(field.Name, out var value) || value is null)
            {
                continue;
            }

            var index = new F_RecordIndex
            {
                TableId = table.Id,
                RecordId = record.Id,
                FieldName = field.Name
            };

            switch (field.DataType)
            {
                case FieldDataType.Number or FieldDataType.Int:
                    index.NumberValue = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    break;
                case FieldDataType.Decimal:
                    index.NumberValue = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    break;
                case FieldDataType.Boolean:
                    index.BoolValue = Convert.ToBoolean(value, CultureInfo.InvariantCulture);
                    break;
                case FieldDataType.Date or FieldDataType.DateTime:
                    index.DateValue = ParseDateValue(value);
                    break;
                default:
                    index.StringValue = value.ToString();
                    break;
            }

            if (index.StringValue is null && index.NumberValue is null && index.DateValue is null && index.BoolValue is null)
            {
                continue;
            }

            indexes.Add(index);
        }

        if (indexes.Count > 0)
        {
            await _db.RecordIndexes.AddRangeAsync(indexes, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string NormalizeStringValue(SFieldDefinition field, object value)
    {
        if (value is JsonElement element && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        if (value is string str)
        {
            return str;
        }

        throw new InvalidOperationException($"Field '{field.Name}' expects text content");
    }

    private static long NormalizeIntegerValue(SFieldDefinition field, object value)
    {
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var intValue))
        {
            return intValue;
        }

        if (value is IConvertible convertible)
        {
            try
            {
                return Convert.ToInt64(convertible, CultureInfo.InvariantCulture);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException($"Field '{field.Name}' expects an integer number", ex);
            }
        }

        throw new InvalidOperationException($"Field '{field.Name}' expects an integer number");
    }

    private static decimal NormalizeDecimalValue(SFieldDefinition field, object value)
    {
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var dec))
        {
            return dec;
        }

        if (value is IConvertible convertible)
        {
            try
            {
                return Convert.ToDecimal(convertible, CultureInfo.InvariantCulture);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException($"Field '{field.Name}' expects a decimal number", ex);
            }
        }

        throw new InvalidOperationException($"Field '{field.Name}' expects a decimal number");
    }

    private static bool NormalizeBooleanValue(SFieldDefinition field, object value)
    {
        if (value is JsonElement element && element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return element.GetBoolean();
        }

        if (value is bool b)
        {
            return b;
        }

        if (value is string str && bool.TryParse(str, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Field '{field.Name}' expects a boolean value");
    }

    private static string NormalizeDateValue(SFieldDefinition field, object value)
    {
        if (value is JsonElement element)
        {
            value = ExtractValue(element);
        }

        if (value is DateTimeOffset dto)
        {
            return dto.ToString("O", CultureInfo.InvariantCulture);
        }

        if (value is DateTime dt)
        {
            return new DateTimeOffset(dt.ToUniversalTime()).ToString("O", CultureInfo.InvariantCulture);
        }

        if (value is string s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        throw new InvalidOperationException($"Field '{field.Name}' expects an ISO-8601 date/time string");
    }

    private static IDictionary<string, string?>? ResolveViewFilter(string? filter, STable table)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var view = table.Views.FirstOrDefault(v => string.Equals(v.Name, filter, StringComparison.OrdinalIgnoreCase));
        if (view is null || string.IsNullOrWhiteSpace(view.QueryDefinition))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(view.QueryDefinition);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IDictionary<string, string?>? MergeEqualsFilters(IDictionary<string, string?>? original, IDictionary<string, string?>? viewFilter)
    {
        if (viewFilter is null || viewFilter.Count == 0)
        {
            return original;
        }

        if (original is null)
        {
            return new Dictionary<string, string?>(viewFilter, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var kv in viewFilter)
        {
            if (!original.ContainsKey(kv.Key))
            {
                original[kv.Key] = kv.Value;
            }
        }

        return original;
    }

    private static int EnsureBasicViews(STable table)
    {
        var added = 0;

        if (!table.Views.Any())
        {
            table.Views.Add(new SViewDefinition
            {
                Name = "all",
                DisplayName = string.IsNullOrWhiteSpace(table.DisplayName) ? table.Name : table.DisplayName,
                Description = "Vue par défaut générée automatiquement",
                QueryDefinition = "{}",
                Visualization = "table",
                IsDefault = true
            });
            added++;
        }

        foreach (var view in table.Views.Where(v => string.IsNullOrWhiteSpace(v.DisplayName)))
        {
            view.DisplayName = view.Name;
        }

        if (string.IsNullOrWhiteSpace(table.DefaultView) && table.Views.Any())
        {
            table.DefaultView = table.Views.FirstOrDefault(v => v.IsDefault)?.Name ?? table.Views.First().Name;
        }

        return added;
    }

    private static IDictionary<string, object?> ParseJsonPayload(string dataJson)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return values;
        }

        using var payload = JsonDocument.Parse(dataJson);
        if (payload.RootElement.ValueKind != JsonValueKind.Object)
        {
            return values;
        }

        foreach (var property in payload.RootElement.EnumerateObject())
        {
            values[property.Name] = ExtractValue(property.Value);
        }

        return values;
    }

    private static string SerializeValues(IDictionary<string, object?> values)
        => JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = false });

    private static Guid? ParseGuid(object value)
        => value switch
        {
            Guid g => g,
            string s when Guid.TryParse(s, out var parsed) => parsed,
            JsonElement element when element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var parsed) => parsed,
            _ => null
        };

    private async Task<KnowledgeNode> UpsertKnowledgeNodeAsync(STable table, F_Record record, CancellationToken cancellationToken)
    {
        var title = BuildRecordTitle(table, record);
        var existing = await _db.KnowledgeNodes.FirstOrDefaultAsync(
            n => n.TableId == table.Id && n.RecordId == record.Id,
            cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            if (!string.Equals(existing.Title, title, StringComparison.Ordinal))
            {
                existing.Title = title;
                _db.KnowledgeNodes.Update(existing);
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            return existing;
        }

        var node = new KnowledgeNode
        {
            TableId = table.Id,
            RecordId = record.Id,
            Title = title,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _db.KnowledgeNodes.AddAsync(node, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return node;
    }

    private string BuildRecordTitle(STable table, F_Record record)
    {
        var values = ParseJsonPayload(record.DataJson);
        var templateTitle = ApplyRowLabelTemplate(table.RowLabelTemplate, values);
        if (!string.IsNullOrWhiteSpace(templateTitle))
        {
            return templateTitle!;
        }

        var firstText = GetFirstTextValue(table, values);
        if (!string.IsNullOrWhiteSpace(firstText))
        {
            return firstText!;
        }

        var prefix = string.IsNullOrWhiteSpace(table.DisplayName) ? table.Name : table.DisplayName;
        return $"{prefix} #{record.Id.ToString()[..8]}";
    }

    private async Task EnsureLookupTargetExists(SFieldDefinition field, Guid lookupId, CancellationToken cancellationToken)
    {
        var targetTable = await FindLookupTableAsync(field, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Lookup target '{field.LookupTarget}' not found for field {field.Name}");

        var exists = await FilterByTable(_db.Records.AsQueryable(), targetTable).AnyAsync(r => r.Id == lookupId, cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            throw new InvalidOperationException($"Lookup value {lookupId} not found in table {targetTable.Name}");
        }
    }

    private async Task<STable?> FindLookupTableAsync(SFieldDefinition field, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(field.LookupTarget))
        {
            return null;
        }

        if (Guid.TryParse(field.LookupTarget, out var tableId))
        {
            return await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false);
        }

        return await _db.Tables
            .Include(t => t.Fields)
            .Include(t => t.Views)
            .FirstOrDefaultAsync(t => t.Name == field.LookupTarget, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ResolvedRecord> BuildResolvedRecordAsync(F_Record record, CancellationToken cancellationToken)
    {
        var table = await GetTableAsync(record.TableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {record.TableId} not found");

        return await BuildResolvedRecordAsync(record, table, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ResolvedRecord> BuildResolvedRecordAsync(F_Record record, STable table, CancellationToken cancellationToken)
    {
        var values = ParseJsonPayload(record.DataJson);
        var lookups = await ResolveLookupValuesAsync(table, (IReadOnlyDictionary<string, object?>)values, cancellationToken).ConfigureAwait(false);
        return new ResolvedRecord(record, new ReadOnlyDictionary<string, object?>(values), new ReadOnlyDictionary<string, LookupResolution>(lookups));
    }

    private async Task<Dictionary<string, LookupResolution>> ResolveLookupValuesAsync(STable table, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, LookupResolution>(StringComparer.OrdinalIgnoreCase);
        var tableCache = new Dictionary<string, STable?>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in table.Fields.Where(f => !string.IsNullOrWhiteSpace(f.LookupTarget)))
        {
            if (!values.TryGetValue(field.Name, out var raw) || raw is null)
            {
                continue;
            }

            var lookupId = ParseGuid(raw);
            if (lookupId is null)
            {
                continue;
            }

            if (!tableCache.TryGetValue(field.LookupTarget!, out var targetTable))
            {
                targetTable = await FindLookupTableAsync(field, cancellationToken).ConfigureAwait(false);
                tableCache[field.LookupTarget!] = targetTable;
            }

            if (targetTable is null)
            {
                continue;
            }

            var targetRecord = await FilterByTable(_db.Records.AsNoTracking(), targetTable)
                .FirstOrDefaultAsync(r => r.Id == lookupId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (targetRecord is null)
            {
                continue;
            }

            var targetValues = ParseJsonPayload(targetRecord.DataJson);
            var label = ResolveLabel(targetTable, (IReadOnlyDictionary<string, object?>)targetValues, field);
            resolved[field.Name] = new LookupResolution(lookupId.Value, label, targetTable.Id, targetTable.Name);
        }

        return resolved;
    }

    private static string? ResolveLabel(STable table, IReadOnlyDictionary<string, object?> targetValues, SFieldDefinition field)
    {
        if (!string.IsNullOrWhiteSpace(field.LookupField) && targetValues.TryGetValue(field.LookupField, out var lookupValue))
        {
            return lookupValue?.ToString();
        }

        var templateLabel = ApplyRowLabelTemplate(table.RowLabelTemplate, targetValues);
        if (!string.IsNullOrWhiteSpace(templateLabel))
        {
            return templateLabel;
        }

        return GetFirstTextValue(table, targetValues);
    }

    private static string? ApplyRowLabelTemplate(string? template, IReadOnlyDictionary<string, object?> values)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        var result = template;
        foreach (var kv in values)
        {
            var placeholder = "{{" + kv.Key + "}}";
            result = result.Replace(placeholder, kv.Value?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static string? GetFirstTextValue(STable table, IReadOnlyDictionary<string, object?> values)
    {
        var textField = table.Fields.FirstOrDefault(f => f.DataType is FieldDataType.Text or FieldDataType.Note);
        if (textField is not null && values.TryGetValue(textField.Name, out var value))
        {
            return value?.ToString();
        }

        return values.Values.Select(v => v?.ToString()).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }
}

public sealed class NoteService : IAionNoteService, INoteService
{
    private readonly AionDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IAudioTranscriptionProvider _transcriptionProvider;
    private readonly ISearchService _search;
    private readonly ILogger<NoteService> _logger;

    public NoteService(AionDbContext db, IFileStorageService fileStorage, IAudioTranscriptionProvider transcriptionProvider, ISearchService search, ILogger<NoteService> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _transcriptionProvider = transcriptionProvider;
        _search = search;
        _logger = logger;
    }

    public async Task<S_Note> CreateDictatedNoteAsync(string title, Stream audioStream, string fileName, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default)
    {
        var audioFile = await _fileStorage.SaveAsync(fileName, audioStream, "audio/wav", cancellationToken).ConfigureAwait(false);
        audioStream.Position = 0;
        var transcription = await _transcriptionProvider.TranscribeAsync(audioStream, fileName, cancellationToken).ConfigureAwait(false);

        var note = BuildNote(title, transcription.Text, NoteSourceType.Voice, links, audioFile.Id);
        await PersistNoteAsync(note, cancellationToken).ConfigureAwait(false);
        return note;
    }

    public async Task<S_Note> CreateTextNoteAsync(string title, string content, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default)
    {
        var note = BuildNote(title, content, NoteSourceType.Text, links, null);
        await PersistNoteAsync(note, cancellationToken).ConfigureAwait(false);
        return note;
    }

    public async Task<IEnumerable<S_Note>> GetChronologicalAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        var notes = await _db.Notes
            .Include(n => n.Links)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return notes
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToList();
    }

    private S_Note BuildNote(string title, string content, NoteSourceType source, IEnumerable<J_Note_Link>? links, Guid? audioFileId)
    {
        var linkList = links?.ToList() ?? new List<J_Note_Link>();
        var context = linkList.Count == 0
            ? null
            : string.Join(", ", linkList.Select(l => $"{l.TargetType}:{l.TargetId}"));

        return new S_Note
        {
            Title = title,
            Content = content,
            Source = source,
            AudioFileId = audioFileId,
            IsTranscribed = source == NoteSourceType.Voice,
            CreatedAt = DateTimeOffset.UtcNow,
            Links = linkList,
            JournalContext = context
        };
    }

    private async Task PersistNoteAsync(S_Note note, CancellationToken cancellationToken)
    {
        await _db.Notes.AddAsync(note, cancellationToken).ConfigureAwait(false);
        var history = new S_HistoryEvent
        {
            Title = "Note créée",
            Description = note.Title,
            OccurredAt = note.CreatedAt,
            Links = new List<S_Link>()
        };

        history.Links.Add(new S_Link
        {
            SourceType = nameof(S_HistoryEvent),
            SourceId = history.Id,
            TargetType = note.JournalContext ?? nameof(S_Note),
            TargetId = note.Id,
            Relation = "note"
        });

        await _db.HistoryEvents.AddAsync(history, cancellationToken).ConfigureAwait(false);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Note {NoteId} persisted (source={Source})", note.Id, note.Source);
        await _search.IndexNoteAsync(note, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class AgendaService : IAionAgendaService, IAgendaService
{
    private readonly AionDbContext _db;
    private readonly ILogger<AgendaService> _logger;

    public AgendaService(AionDbContext db, ILogger<AgendaService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<S_Event> AddEventAsync(S_Event evt, CancellationToken cancellationToken = default)
    {
        evt.ReminderAt ??= evt.Start.AddHours(-2);
        await _db.Events.AddAsync(evt, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var history = new S_HistoryEvent
        {
            Title = "Évènement planifié",
            Description = evt.Title,
            OccurredAt = DateTimeOffset.UtcNow,
            Links = new List<S_Link>()
        };

        foreach (var link in evt.Links)
        {
            history.Links.Add(new S_Link
            {
                SourceType = nameof(S_HistoryEvent),
                SourceId = history.Id,
                TargetType = link.TargetType,
                TargetId = link.TargetId,
                Relation = "agenda"
            });
        }

        await _db.HistoryEvents.AddAsync(history, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Event {EventId} added with reminder {Reminder}", evt.Id, evt.ReminderAt);
        return evt;
    }

    public async Task<IEnumerable<S_Event>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => (await _db.Events
            .Where(e => e.Start >= from && e.Start <= to)
            .Include(e => e.Links)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .OrderBy(e => e.Start)
            .ToList();

    public async Task<IEnumerable<S_Event>> GetPendingRemindersAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
        => await _db.Events.Where(e => !e.IsCompleted && e.ReminderAt.HasValue && e.ReminderAt <= asOf)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

public sealed class FileStorageService : IFileStorageService
{
    private readonly StorageOptions _options;
    private readonly AionDbContext _db;
    private readonly ISearchService _search;
    private readonly ILogger<FileStorageService> _logger;
    private readonly IStorageService _storage;

    public FileStorageService(IOptions<StorageOptions> options, AionDbContext db, ISearchService search, IStorageService storage, ILogger<FileStorageService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _db = db;
        _search = search;
        _storage = storage;
        _logger = logger;
    }

    public async Task<F_File> SaveAsync(string fileName, Stream content, string mimeType, CancellationToken cancellationToken = default)
    {
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (buffer.Length > _options.MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"File {fileName} exceeds the configured limit of {_options.MaxFileSizeBytes / (1024 * 1024)} MB");
        }

        await EnsureStorageQuotaAsync(buffer.Length, cancellationToken).ConfigureAwait(false);

        buffer.Position = 0;
        var id = Guid.NewGuid();
        var stored = await _storage.SaveAsync(fileName, buffer, cancellationToken).ConfigureAwait(false);

        var file = new F_File
        {
            Id = id,
            FileName = fileName,
            MimeType = mimeType,
            StoragePath = stored.Path,
            Size = stored.Size,
            Sha256 = stored.Sha256,
            UploadedAt = DateTimeOffset.UtcNow
        };

        await _db.Files.AddAsync(file, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("File {FileId} stored at {Path}", id, stored.Path);
        await _search.IndexFileAsync(file, cancellationToken).ConfigureAwait(false);
        return file;
    }

    public async Task<Stream> OpenAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await _db.Files.FirstAsync(f => f.Id == fileId, cancellationToken).ConfigureAwait(false);
        return await _storage.OpenReadAsync(file.StoragePath, _options.RequireIntegrityCheck ? file.Sha256 : null, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await _db.Files.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            _logger.LogWarning("Attempted to delete missing file {FileId}", fileId);
            return;
        }

        var links = await _db.FileLinks
            .Where(l => l.FileId == fileId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        _db.FileLinks.RemoveRange(links);
        _db.Files.Remove(file);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _storage.DeleteAsync(file.StoragePath, cancellationToken).ConfigureAwait(false);

        await _search.RemoveAsync("File", fileId, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("File {FileId} deleted with {LinkCount} link(s) removed", fileId, links.Count);
    }

    public async Task<F_FileLink> LinkAsync(Guid fileId, string targetType, Guid targetId, string? relation = null, CancellationToken cancellationToken = default)
    {
        var link = new F_FileLink
        {
            FileId = fileId,
            TargetType = targetType,
            TargetId = targetId,
            Relation = relation
        };

        await _db.FileLinks.AddAsync(link, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("File {FileId} linked to {TargetType}:{TargetId}", fileId, targetType, targetId);
        return link;
    }

    public async Task<IEnumerable<F_File>> GetForAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default)
    {
        var fileIds = await _db.FileLinks.Where(l => l.TargetType == targetType && l.TargetId == targetId)
            .Select(l => l.FileId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await _db.Files.Where(f => fileIds.Contains(f.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureStorageQuotaAsync(long incomingFileSize, CancellationToken cancellationToken)
    {
        var usedBytes = await _db.Files.SumAsync(f => f.Size, cancellationToken).ConfigureAwait(false);
        if (usedBytes + incomingFileSize > _options.MaxTotalBytes)
        {
            throw new InvalidOperationException("Storage quota exceeded; delete files before uploading new content.");
        }
    }
}

public sealed class CloudBackupService : ICloudBackupService
{
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _backupFolder;
    private readonly BackupOptions _options;
    private readonly ILogger<CloudBackupService> _logger;

    public CloudBackupService(IOptions<BackupOptions> options, ILogger<CloudBackupService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _backupFolder = _options.BackupFolder ?? throw new InvalidOperationException("Backup folder must be configured");
        _logger = logger;
        Directory.CreateDirectory(_backupFolder);
    }

    public async Task BackupAsync(string encryptedDatabasePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(encryptedDatabasePath))
        {
            throw new FileNotFoundException("Database file not found", encryptedDatabasePath);
        }

        var databaseInfo = new FileInfo(encryptedDatabasePath);
        if (databaseInfo.Length > _options.MaxDatabaseSizeBytes)
        {
            throw new InvalidOperationException($"Database exceeds the maximum backup size of {_options.MaxDatabaseSizeBytes / (1024 * 1024)} MB");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var backupFileName = $"aion-{timestamp:yyyyMMddHHmmss}.db";
        var destination = Path.Combine(_backupFolder, backupFileName);
        await using var source = new FileStream(encryptedDatabasePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var dest = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);

        var manifest = new BackupManifest
        {
            FileName = backupFileName,
            CreatedAt = timestamp,
            Size = new FileInfo(destination).Length,
            Sha256 = await ComputeHashAsync(destination, cancellationToken).ConfigureAwait(false),
            SourcePath = Path.GetFullPath(encryptedDatabasePath),
            IsEncrypted = false
        };

        await using var manifestStream = new FileStream(Path.ChangeExtension(destination, ".json"), FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(manifestStream, manifest, ManifestSerializerOptions, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Backup created at {Destination} (hash {Hash})", destination, manifest.Sha256);
    }

    public async Task RestoreAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        var manifest = LoadLatestManifest();
        if (manifest is null)
        {
            throw new FileNotFoundException("No backup manifest found", _backupFolder);
        }

        var backupFile = Path.Combine(_backupFolder, manifest.FileName);
        if (!File.Exists(backupFile))
        {
            throw new FileNotFoundException("Backup file missing", backupFile);
        }

        var tempPath = destinationPath + ".restoring";
        await using (var source = new FileStream(backupFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var dest = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
        }

        var computedHash = await ComputeHashAsync(tempPath, cancellationToken).ConfigureAwait(false);
        if (_options.RequireIntegrityCheck && !string.Equals(computedHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(tempPath);
            throw new InvalidOperationException("Backup integrity check failed: hash mismatch");
        }

        var backupExisting = destinationPath + ".bak";
        if (File.Exists(destinationPath))
        {
            File.Move(destinationPath, backupExisting, overwrite: true);
        }

        File.Move(tempPath, destinationPath, overwrite: true);
        _logger.LogInformation("Backup restored transactionally from {BackupFile}", backupFile);
    }

    private BackupManifest? LoadLatestManifest()
    {
        var manifestFile = Directory.GetFiles(_backupFolder, "*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (manifestFile is null)
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(manifestFile);
            return JsonSerializer.Deserialize<BackupManifest>(json, ManifestSerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read backup manifest {Manifest}", manifestFile);
            return null;
        }
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}

public sealed class AutomationService : IAionAutomationService, IAutomationService
{
    private readonly AionDbContext _db;
    private readonly ILogger<AutomationService> _logger;
    private readonly IAutomationOrchestrator _orchestrator;

    public AutomationService(AionDbContext db, ILogger<AutomationService> logger, IAutomationOrchestrator orchestrator)
    {
        _db = db;
        _logger = logger;
        _orchestrator = orchestrator;
    }

    public async Task<S_AutomationRule> AddRuleAsync(S_AutomationRule rule, CancellationToken cancellationToken = default)
    {
        await _db.AutomationRules.AddAsync(rule, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Automation rule {Rule} registered", rule.Name);
        return rule;
    }

    public async Task<IEnumerable<S_AutomationRule>> GetRulesAsync(CancellationToken cancellationToken = default)
        => await _db.AutomationRules
            .Include(r => r.Conditions)
            .Include(r => r.Actions)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<IEnumerable<AutomationExecution>> TriggerAsync(string eventName, object payload, CancellationToken cancellationToken = default)
        => _orchestrator.TriggerAsync(eventName, payload, cancellationToken);
}

public sealed class DashboardService : IDashboardService
{
    private readonly AionDbContext _db;

    public DashboardService(AionDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<DashboardWidget>> GetWidgetsAsync(CancellationToken cancellationToken = default)
        => await _db.Widgets.OrderBy(w => w.Order).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<DashboardWidget> SaveWidgetAsync(DashboardWidget widget, CancellationToken cancellationToken = default)
    {
        if (await _db.Widgets.AnyAsync(w => w.Id == widget.Id, cancellationToken).ConfigureAwait(false))
        {
            _db.Widgets.Update(widget);
        }
        else
        {
            await _db.Widgets.AddAsync(widget, cancellationToken).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return widget;
    }
}

public sealed class TemplateService : IAionTemplateMarketplaceService, ITemplateService
{
    private readonly AionDbContext _db;
    private readonly string _marketplaceFolder;

    public TemplateService(AionDbContext db, IOptions<MarketplaceOptions> options)
    {
        _db = db;
        ArgumentNullException.ThrowIfNull(options);
        _marketplaceFolder = options.Value.MarketplaceFolder ?? throw new InvalidOperationException("Marketplace folder is required");
        Directory.CreateDirectory(_marketplaceFolder);
    }

    public async Task<TemplatePackage> ExportModuleAsync(Guid moduleId, CancellationToken cancellationToken = default)
    {
        var module = await _db.Modules
            .Include(m => m.EntityTypes).ThenInclude(e => e.Fields)
            .Include(m => m.Reports)
            .Include(m => m.AutomationRules).ThenInclude(r => r.Actions)
            .Include(m => m.AutomationRules).ThenInclude(r => r.Conditions)
            .FirstAsync(m => m.Id == moduleId, cancellationToken)
            .ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(module, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var package = new TemplatePackage
        {
            Name = module.Name,
            Description = module.Description,
            Payload = payload,
            Version = "1.0.0"
        };

        await _db.Templates.AddAsync(package, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await CreateOrUpdateMarketplaceEntry(package, cancellationToken).ConfigureAwait(false);
        return package;
    }

    public async Task<S_Module> ImportModuleAsync(TemplatePackage package, CancellationToken cancellationToken = default)
    {
        var module = JsonSerializer.Deserialize<S_Module>(package.Payload) ?? new S_Module { Name = package.Name };
        await CreateOrUpdateMarketplaceEntry(package, cancellationToken).ConfigureAwait(false);
        EnsureModuleMetadata(module);
        await _db.Modules.AddAsync(module, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return module;
    }

    private async Task CreateOrUpdateMarketplaceEntry(TemplatePackage package, CancellationToken cancellationToken)
    {
        var fileName = Path.Combine(_marketplaceFolder, $"{package.Id}.json");
        await File.WriteAllTextAsync(fileName, package.Payload, cancellationToken).ConfigureAwait(false);

        if (!await _db.Marketplace.AnyAsync(i => i.Id == package.Id, cancellationToken).ConfigureAwait(false))
        {
            await _db.Marketplace.AddAsync(new MarketplaceItem
            {
                Id = package.Id,
                Name = package.Name,
                Category = "Module",
                PackagePath = fileName
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var existing = await _db.Marketplace.FirstAsync(i => i.Id == package.Id, cancellationToken).ConfigureAwait(false);
            existing.PackagePath = fileName;
            existing.Name = package.Name;
            _db.Marketplace.Update(existing);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureModuleMetadata(S_Module module)
    {
        var now = DateTimeOffset.UtcNow;
        if (module.ModifiedAt == default)
        {
            module.ModifiedAt = now;
        }

        if (module.Version <= 0)
        {
            module.Version = 1;
        }
    }

    public async Task<IEnumerable<MarketplaceItem>> GetMarketplaceAsync(CancellationToken cancellationToken = default)
    {
        // Synchronise le disque et la base pour rester cohérent avec les fichiers locaux
        foreach (var file in Directory.EnumerateFiles(_marketplaceFolder, "*.json"))
        {
            var id = Guid.Parse(Path.GetFileNameWithoutExtension(file));
            if (!await _db.Marketplace.AnyAsync(i => i.Id == id, cancellationToken).ConfigureAwait(false))
            {
                await _db.Marketplace.AddAsync(new MarketplaceItem
                {
                    Id = id,
                    Name = Path.GetFileName(file),
                    Category = "Module",
                    PackagePath = file
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await _db.Marketplace.ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class LifeService : IAionLifeLogService, ILifeService
{
    private readonly AionDbContext _db;

    public LifeService(AionDbContext db)
    {
        _db = db;
    }

    public async Task<S_HistoryEvent> AddHistoryAsync(S_HistoryEvent evt, CancellationToken cancellationToken = default)
    {
        await _db.HistoryEvents.AddAsync(evt, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return evt;
    }

    public async Task<IEnumerable<S_HistoryEvent>> GetTimelineAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        var query = _db.HistoryEvents.Include(h => h.Links).AsQueryable();
        if (from.HasValue)
        {
            query = query.Where(h => h.OccurredAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(h => h.OccurredAt <= to.Value);
        }

        var results = await query
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return results
            .OrderByDescending(h => h.OccurredAt)
            .ToList();
    }
}

public sealed class PredictService : IAionPredictionService, IPredictService
{
    private readonly AionDbContext _db;

    public PredictService(AionDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<PredictionInsight>> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var insights = new List<PredictionInsight>
        {
            new()
            {
                Kind = PredictionKind.Reminder,
                Message = "Hydrate your potager seedlings this evening.",
                GeneratedAt = DateTimeOffset.UtcNow
            }
        };

        await _db.Predictions.AddRangeAsync(insights, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return insights;
    }
}

public sealed class PersonaEngine : IAionPersonaEngine, IPersonaEngine
{
    private readonly AionDbContext _db;

    public PersonaEngine(AionDbContext db)
    {
        _db = db;
    }

    public async Task<UserPersona> GetPersonaAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _db.Personas.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var defaultPersona = new UserPersona { Name = "Default", Tone = PersonaTone.Neutral };
        await _db.Personas.AddAsync(defaultPersona, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return defaultPersona;
    }

    public async Task<UserPersona> SavePersonaAsync(UserPersona persona, CancellationToken cancellationToken = default)
    {
        if (await _db.Personas.AnyAsync(p => p.Id == persona.Id, cancellationToken).ConfigureAwait(false))
        {
            _db.Personas.Update(persona);
        }
        else
        {
            await _db.Personas.AddAsync(persona, cancellationToken).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return persona;
    }
}

public sealed class VisionService : IAionVisionService, IVisionService
{
    private readonly AionDbContext _db;

    public VisionService(AionDbContext db)
    {
        _db = db;
    }

    public async Task<S_VisionAnalysis> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var analysis = new S_VisionAnalysis
        {
            FileId = request.FileId,
            AnalysisType = request.AnalysisType,
            ResultJson = JsonSerializer.Serialize(new { summary = "Vision analysis placeholder" })
        };

        await _db.VisionAnalyses.AddAsync(analysis, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return analysis;
    }
}
