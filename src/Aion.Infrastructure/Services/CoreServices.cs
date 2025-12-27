using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aion.AI;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Aion.Infrastructure.Observability;
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
    private const int SemanticPageSize = 250;
    private static readonly JsonSerializerOptions EmbeddingSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly AionDbContext _db;
    private readonly ILogger<AionDataEngine> _logger;
    private readonly ISearchService _search;
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly IOperationScopeFactory _operationScopeFactory;
    private readonly IAutomationRuleEngine _automationRuleEngine;
    private readonly ICurrentUserService _currentUserService;
    private readonly Dictionary<string, STable?> _lookupTableCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, HashSet<Guid>> _lookupExistingIdsCache = new();

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
        private bool _failed;

        public OperationMetricsScope(IOperationScope operationScope, IDisposable logScope, string operationName)
        {
            _operationScope = operationScope;
            _logScope = logScope;
            OperationName = operationName;
        }

        public string OperationName { get; }
        public OperationContext Context => _operationScope.Context;
        public TimeSpan Elapsed => _stopwatch.Elapsed;

        public void MarkFailed(Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                return;
            }

            _failed = true;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            InfrastructureMetrics.RecordDataEngineDuration(OperationName, _stopwatch.Elapsed);
            if (_failed)
            {
                InfrastructureMetrics.RecordDataEngineError(OperationName);
            }
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

        try
        {
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
        catch (Exception ex)
        {
            operation.MarkFailed(ex);
            throw;
        }
    }

    public async Task<IEnumerable<SViewDefinition>> GenerateSimpleViewsAsync(Guid tableId, CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation("DataEngine.GenerateViews", tableId);

        try
        {
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
        catch (Exception ex)
        {
            operation.MarkFailed(ex);
            throw;
        }
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

        try
        {
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
            await AddRecordHistoryAsync(table, record.Id, "Enregistrement créé", validated.Values, now, cancellationToken).ConfigureAwait(false);
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
        catch (Exception ex)
        {
            operation.MarkFailed(ex);
            throw;
        }
    }

    public Task<F_Record> UpdateAsync(Guid tableId, Guid id, string dataJson, CancellationToken cancellationToken = default)
        => UpdateAsync(tableId, id, ParseJsonPayload(dataJson), cancellationToken);

    public async Task<F_Record> UpdateAsync(Guid tableId, Guid id, IDictionary<string, object?> data, CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation("DataEngine.Update", tableId, id);

        try
        {
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
            await AddRecordHistoryAsync(table, record.Id, "Enregistrement mis à jour", validated.Values, now, cancellationToken).ConfigureAwait(false);
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
        catch (Exception ex)
        {
            operation.MarkFailed(ex);
            throw;
        }
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

        var changeType = ChangeType.Delete;
        if (table.SupportsSoftDelete)
        {
            record.DeletedAt = now;
            record.ModifiedAt = now;
            record.Version = nextVersion;
            _db.Records.Update(record);
            changeType = ChangeType.SoftDelete;
        }
        else
        {
            _db.Records.Remove(record);
        }

        await AddAuditEntryAsync(tableId, id, changeType, snapshotJson, snapshotJson, nextVersion, now, cancellationToken).ConfigureAwait(false);
        var deletedValues = ParseJsonPayload(snapshotJson);
        await AddRecordHistoryAsync(table, id, "Enregistrement supprimé", deletedValues, now, cancellationToken).ConfigureAwait(false);
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

        try
        {
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
        catch (Exception ex)
        {
            operation.MarkFailed(ex);
            throw;
        }
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

        try
        {
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
        catch (Exception ex)
        {
            operation.MarkFailed(ex);
            throw;
        }
    }

    public async Task<IEnumerable<RecordSearchHit>> SearchSmartAsync(Guid tableId, string query, SearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<RecordSearchHit>();
        }

        using var operation = BeginOperation("DataEngine.SearchSmart", tableId);

        try
        {
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
        catch (Exception ex)
        {
            operation.MarkFailed(ex);
            throw;
        }
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

        var scores = new Dictionary<Guid, double>();
        var pageIndex = 0;
        while (true)
        {
            var entries = await _db.Embeddings.AsNoTracking()
                .Where(e => e.TableId == tableId)
                .OrderBy(e => e.RecordId)
                .Skip(pageIndex * SemanticPageSize)
                .Take(SemanticPageSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (entries.Count == 0)
            {
                break;
            }

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

            pageIndex++;
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
        var lookupValidations = new List<LookupValidationEntry>();

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
                lookupValidations.Add(new LookupValidationEntry(field, field.LookupTarget!, lookupId));
                normalizedValue = lookupId;
            }

            normalized[field.Name] = normalizedValue;
        }

        if (lookupValidations.Count > 0)
        {
            await ValidateLookupTargetsAsync(lookupValidations, cancellationToken).ConfigureAwait(false);
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

    private sealed record LookupValidationEntry(SFieldDefinition Field, string LookupTarget, Guid LookupId);

    private async Task ValidateLookupTargetsAsync(IReadOnlyCollection<LookupValidationEntry> lookups, CancellationToken cancellationToken)
    {
        var tablesByLookup = new Dictionary<string, STable>(StringComparer.OrdinalIgnoreCase);
        var existingIdsByLookup = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);
        var queryCount = 0;
        var cacheHitCount = 0;

        foreach (var group in lookups.GroupBy(entry => entry.LookupTarget, StringComparer.OrdinalIgnoreCase))
        {
            if (!_lookupTableCache.TryGetValue(group.Key, out var targetTable))
            {
                var field = group.First().Field;
                targetTable = await FindLookupTableAsync(field, cancellationToken).ConfigureAwait(false);
                _lookupTableCache[group.Key] = targetTable;
            }

            if (targetTable is null)
            {
                var field = group.First().Field;
                throw new InvalidOperationException($"Lookup target '{field.LookupTarget}' not found for field {field.Name}");
            }

            var ids = group.Select(entry => entry.LookupId).Distinct().ToArray();
            if (!_lookupExistingIdsCache.TryGetValue(targetTable.Id, out var existingIds))
            {
                existingIds = new HashSet<Guid>();
                _lookupExistingIdsCache[targetTable.Id] = existingIds;
            }

            foreach (var id in ids)
            {
                if (existingIds.Contains(id))
                {
                    cacheHitCount += 1;
                }
            }

            var missingIds = ids.Where(id => !existingIds.Contains(id)).ToArray();
            if (missingIds.Length > 0)
            {
                queryCount += 1;
                var foundIds = await FilterByTable(_db.Records.AsQueryable(), targetTable)
                    .Where(record => missingIds.Contains(record.Id))
                    .Select(record => record.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var id in foundIds)
                {
                    existingIds.Add(id);
                }
            }

            tablesByLookup[group.Key] = targetTable;
            existingIdsByLookup[group.Key] = existingIds;
        }

        foreach (var entry in lookups)
        {
            if (!existingIdsByLookup.TryGetValue(entry.LookupTarget, out var existingIds))
            {
                continue;
            }

            if (!existingIds.Contains(entry.LookupId))
            {
                var targetTable = tablesByLookup[entry.LookupTarget];
                throw new InvalidOperationException($"Lookup value {entry.LookupId} not found in table {targetTable.Name}");
            }
        }

        if (queryCount > 0 || cacheHitCount > 0)
        {
            _logger.LogDebug(
                "Lookup validation executed {QueryCount} query(ies) across {TableCount} table(s) with {CacheHitCount} cached lookup(s).",
                queryCount,
                tablesByLookup.Count,
                cacheHitCount);
        }

        InfrastructureMetrics.RecordLookupValidationQueries(queryCount);
        InfrastructureMetrics.RecordLookupValidationCacheHits(cacheHitCount);
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

    private static string? ResolveRecordLabel(STable table, IReadOnlyDictionary<string, object?> values)
    {
        var templateLabel = ApplyRowLabelTemplate(table.RowLabelTemplate, values);
        if (!string.IsNullOrWhiteSpace(templateLabel))
        {
            return templateLabel;
        }

        return GetFirstTextValue(table, values);
    }

    private async Task<Guid?> ResolveModuleIdAsync(Guid tableId, CancellationToken cancellationToken)
        => await _db.EntityTypes.AsNoTracking()
            .Where(entity => entity.Id == tableId)
            .Select(entity => (Guid?)entity.ModuleId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task AddRecordHistoryAsync(
        STable table,
        Guid recordId,
        string title,
        IReadOnlyDictionary<string, object?> values,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var history = new S_HistoryEvent
        {
            Title = title,
            Description = ResolveRecordLabel(table, values),
            OccurredAt = occurredAt,
            ModuleId = await ResolveModuleIdAsync(table.Id, cancellationToken).ConfigureAwait(false),
            Links = new List<S_Link>()
        };

        history.Links.Add(new S_Link
        {
            SourceType = nameof(S_HistoryEvent),
            SourceId = history.Id,
            TargetType = table.Name,
            TargetId = recordId,
            Relation = "record",
            Type = "history",
            CreatedBy = _currentUserService.GetCurrentUserId(),
            Reason = "record history"
        });

        await _db.HistoryEvents.AddAsync(history, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class NoteService : IAionNoteService, INoteService
{
    private readonly AionDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IAudioTranscriptionProvider _transcriptionProvider;
    private readonly INoteTaggingService _taggingService;
    private readonly ISearchService _search;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<NoteService> _logger;

    public NoteService(
        AionDbContext db,
        IFileStorageService fileStorage,
        IAudioTranscriptionProvider transcriptionProvider,
        INoteTaggingService taggingService,
        ISearchService search,
        ICurrentUserService currentUserService,
        ILogger<NoteService> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _transcriptionProvider = transcriptionProvider;
        _taggingService = taggingService;
        _search = search;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<S_Note> CreateDictatedNoteAsync(string title, Stream audioStream, string fileName, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default)
    {
        var audioFile = await _fileStorage.SaveAsync(fileName, audioStream, "audio/wav", cancellationToken).ConfigureAwait(false);
        audioStream.Position = 0;
        var transcription = await _transcriptionProvider.TranscribeAsync(audioStream, fileName, cancellationToken).ConfigureAwait(false);

        var tags = await _taggingService.SuggestTagsAsync(title, transcription.Text, cancellationToken).ConfigureAwait(false);
        var note = BuildNote(title, transcription.Text, NoteSourceType.Voice, links, audioFile.Id, tags);
        await PersistNoteAsync(note, cancellationToken).ConfigureAwait(false);
        return note;
    }

    public async Task<S_Note> CreateTextNoteAsync(string title, string content, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default)
    {
        var tags = await _taggingService.SuggestTagsAsync(title, content, cancellationToken).ConfigureAwait(false);
        var note = BuildNote(title, content, NoteSourceType.Text, links, null, tags);
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

    private S_Note BuildNote(
        string title,
        string content,
        NoteSourceType source,
        IEnumerable<J_Note_Link>? links,
        Guid? audioFileId,
        IEnumerable<string>? tags)
    {
        var linkList = links?.ToList() ?? new List<J_Note_Link>();
        var normalizedTags = NormalizeTags(tags);
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
            Tags = normalizedTags,
            Links = linkList,
            JournalContext = context
        };
    }

    private static List<string> NormalizeTags(IEnumerable<string>? tags)
        => tags is null
            ? new List<string>()
            : tags
                .Select(tag => tag.Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag)
                .ToList();

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

        history.ModuleId = await ResolveModuleIdAsync(note.Links.Select(l => l.TargetType), cancellationToken).ConfigureAwait(false);

        history.Links.Add(new S_Link
        {
            SourceType = nameof(S_HistoryEvent),
            SourceId = history.Id,
            TargetType = note.JournalContext ?? nameof(S_Note),
            TargetId = note.Id,
            Relation = "note",
            Type = "history",
            CreatedBy = _currentUserService.GetCurrentUserId(),
            Reason = "note created"
        });

        await _db.HistoryEvents.AddAsync(history, cancellationToken).ConfigureAwait(false);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Note {NoteId} persisted (source={Source})", note.Id, note.Source);
        await _search.IndexNoteAsync(note, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid?> ResolveModuleIdAsync(IEnumerable<string> targetTypes, CancellationToken cancellationToken)
    {
        var normalized = targetTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            return null;
        }

        var tableIds = await _db.Tables.AsNoTracking()
            .Where(table => normalized.Contains(table.Name))
            .Select(table => table.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tableIds.Count == 0)
        {
            return null;
        }

        return await _db.EntityTypes.AsNoTracking()
            .Where(entity => tableIds.Contains(entity.Id))
            .Select(entity => (Guid?)entity.ModuleId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

public sealed class AionAgendaService : IAionAgendaService, IAgendaService
{
    private readonly AionDbContext _db;
    private readonly INotificationService _notifications;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AionAgendaService> _logger;

    public AionAgendaService(
        AionDbContext db,
        INotificationService notifications,
        ICurrentUserService currentUserService,
        ILogger<AionAgendaService> logger)
    {
        _db = db;
        _notifications = notifications;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<S_Event> AddEventAsync(S_Event evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ValidateRecurrence(evt);
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

        history.ModuleId = await ResolveModuleIdAsync(evt.Links.Select(l => l.TargetType), cancellationToken).ConfigureAwait(false);

        foreach (var link in evt.Links)
        {
            history.Links.Add(new S_Link
            {
                SourceType = nameof(S_HistoryEvent),
                SourceId = history.Id,
                TargetType = link.TargetType,
                TargetId = link.TargetId,
                Relation = "agenda",
                Type = "history",
                CreatedBy = _currentUserService.GetCurrentUserId(),
                Reason = "agenda event"
            });
        }

        await _db.HistoryEvents.AddAsync(history, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Event {EventId} added with reminder {Reminder}", evt.Id, evt.ReminderAt);
        await ScheduleReminderAsync(evt, cancellationToken).ConfigureAwait(false);
        return evt;
    }

    public async Task<S_Event> UpdateEventAsync(S_Event evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ValidateRecurrence(evt);

        var tracked = await _db.Events.Include(e => e.Links)
            .FirstOrDefaultAsync(e => e.Id == evt.Id, cancellationToken)
            .ConfigureAwait(false);
        if (tracked is null)
        {
            throw new InvalidOperationException($"Event {evt.Id} not found.");
        }

        tracked.Title = evt.Title;
        tracked.Description = evt.Description;
        tracked.Start = evt.Start;
        tracked.End = evt.End;
        tracked.ReminderAt = evt.ReminderAt ?? evt.Start.AddHours(-2);
        tracked.IsCompleted = evt.IsCompleted;
        tracked.RecurrenceFrequency = evt.RecurrenceFrequency;
        tracked.RecurrenceInterval = evt.RecurrenceInterval;
        tracked.RecurrenceCount = evt.RecurrenceCount;
        tracked.RecurrenceUntil = evt.RecurrenceUntil;

        tracked.Links.Clear();
        foreach (var link in evt.Links)
        {
            tracked.Links.Add(new J_Event_Link
            {
                TargetType = link.TargetType,
                TargetId = link.TargetId
            });
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _notifications.CancelAsync(tracked.Id, cancellationToken).ConfigureAwait(false);
        await ScheduleReminderAsync(tracked, cancellationToken).ConfigureAwait(false);
        return tracked;
    }

    public async Task DeleteEventAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var tracked = await _db.Events.Include(e => e.Links)
            .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken)
            .ConfigureAwait(false);
        if (tracked is null)
        {
            return;
        }

        _db.EventLinks.RemoveRange(tracked.Links);
        _db.Events.Remove(tracked);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _notifications.CancelAsync(eventId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<S_Event>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => (await _db.Events
            .Where(e => e.Start <= to && (e.Start >= from || (e.RecurrenceFrequency.HasValue && (e.RecurrenceUntil == null || e.RecurrenceUntil >= from))))
            .Include(e => e.Links)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .OrderBy(e => e.Start)
            .ToList();

    public async Task<IEnumerable<S_Event>> GetOccurrencesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var events = await GetEventsAsync(from, to, cancellationToken).ConfigureAwait(false);
        return ExpandOccurrences(events, from, to)
            .OrderBy(e => e.Start)
            .ToList();
    }

    public async Task<IEnumerable<S_Event>> GetPendingRemindersAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
        => await _db.Events.Where(e => !e.IsCompleted && e.ReminderAt.HasValue && e.ReminderAt <= asOf)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    private static IEnumerable<S_Event> ExpandOccurrences(IEnumerable<S_Event> events, DateTimeOffset from, DateTimeOffset to)
    {
        foreach (var evt in events)
        {
            if (!evt.RecurrenceFrequency.HasValue)
            {
                if (evt.Start >= from && evt.Start <= to)
                {
                    yield return evt;
                }

                continue;
            }

            var interval = evt.RecurrenceInterval.GetValueOrDefault(1);
            if (interval <= 0)
            {
                interval = 1;
            }

            var duration = evt.End.HasValue ? evt.End.Value - evt.Start : (TimeSpan?)null;
            var maxCount = evt.RecurrenceCount ?? int.MaxValue;
            var aligned = AlignToRange(evt.Start, from, evt.RecurrenceFrequency.Value, interval);
            var occurrenceStart = aligned.Start;
            var index = aligned.Skipped;

            while (occurrenceStart <= to && index < maxCount)
            {
                if (!evt.RecurrenceUntil.HasValue || occurrenceStart <= evt.RecurrenceUntil.Value)
                {
                    if (occurrenceStart >= from)
                    {
                        yield return new S_Event
                        {
                            Id = evt.Id,
                            Title = evt.Title,
                            Description = evt.Description,
                            Start = occurrenceStart,
                            End = duration.HasValue ? occurrenceStart.Add(duration.Value) : null,
                            ReminderAt = evt.ReminderAt,
                            RecurrenceFrequency = evt.RecurrenceFrequency,
                            RecurrenceInterval = evt.RecurrenceInterval,
                            RecurrenceCount = evt.RecurrenceCount,
                            RecurrenceUntil = evt.RecurrenceUntil,
                            IsCompleted = evt.IsCompleted,
                            Links = evt.Links.ToList()
                        };
                    }
                }
                else
                {
                    break;
                }

                occurrenceStart = NextOccurrence(occurrenceStart, evt.RecurrenceFrequency.Value, interval);
                index++;
            }
        }
    }

    private static (DateTimeOffset Start, int Skipped) AlignToRange(
        DateTimeOffset start,
        DateTimeOffset from,
        EventRecurrenceFrequency frequency,
        int interval)
    {
        if (start >= from)
        {
            return (start, 0);
        }

        interval = Math.Max(interval, 1);
        return frequency switch
        {
            EventRecurrenceFrequency.Daily => AlignByDays(start, from, interval),
            EventRecurrenceFrequency.Weekly => AlignByDays(start, from, interval * 7),
            EventRecurrenceFrequency.Monthly => AlignByMonths(start, from, interval),
            _ => (start, 0)
        };
    }

    private static (DateTimeOffset Start, int Skipped) AlignByDays(DateTimeOffset start, DateTimeOffset from, int stepDays)
    {
        var totalDays = (from - start).TotalDays;
        var increments = (int)Math.Floor(totalDays / stepDays);
        var aligned = start.AddDays(increments * stepDays);
        var skipped = increments;
        while (aligned < from)
        {
            aligned = aligned.AddDays(stepDays);
            skipped++;
        }

        return (aligned, Math.Max(skipped, 0));
    }

    private static (DateTimeOffset Start, int Skipped) AlignByMonths(DateTimeOffset start, DateTimeOffset from, int stepMonths)
    {
        var monthsDiff = (from.Year - start.Year) * 12 + (from.Month - start.Month);
        var increments = monthsDiff <= 0 ? 0 : monthsDiff / stepMonths;
        var aligned = start.AddMonths(increments * stepMonths);
        var skipped = increments;
        while (aligned < from)
        {
            aligned = aligned.AddMonths(stepMonths);
            skipped++;
        }

        return (aligned, Math.Max(skipped, 0));
    }

    private static DateTimeOffset NextOccurrence(DateTimeOffset current, EventRecurrenceFrequency frequency, int interval)
        => frequency switch
        {
            EventRecurrenceFrequency.Daily => current.AddDays(interval),
            EventRecurrenceFrequency.Weekly => current.AddDays(7 * interval),
            EventRecurrenceFrequency.Monthly => current.AddMonths(interval),
            _ => current
        };

    private static void ValidateRecurrence(S_Event evt)
    {
        if (!evt.RecurrenceFrequency.HasValue)
        {
            return;
        }

        if (evt.RecurrenceInterval.HasValue && evt.RecurrenceInterval <= 0)
        {
            throw new InvalidOperationException("Recurrence interval must be greater than zero.");
        }

        if (evt.RecurrenceCount.HasValue && evt.RecurrenceCount <= 0)
        {
            throw new InvalidOperationException("Recurrence count must be greater than zero.");
        }
    }

    private async Task ScheduleReminderAsync(S_Event evt, CancellationToken cancellationToken)
    {
        if (!evt.ReminderAt.HasValue)
        {
            return;
        }

        await _notifications.ScheduleAsync(
            new NotificationRequest(evt.Id, evt.Title, evt.Description ?? evt.Title, evt.ReminderAt.Value),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid?> ResolveModuleIdAsync(IEnumerable<string> targetTypes, CancellationToken cancellationToken)
    {
        var normalized = targetTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            return null;
        }

        var tableIds = await _db.Tables.AsNoTracking()
            .Where(table => normalized.Contains(table.Name))
            .Select(table => table.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tableIds.Count == 0)
        {
            return null;
        }

        return await _db.EntityTypes.AsNoTracking()
            .Where(entity => tableIds.Contains(entity.Id))
            .Select(entity => (Guid?)entity.ModuleId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
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

    public async Task<S_AutomationRule> SetRuleEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken = default)
    {
        var rule = await _db.AutomationRules.FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Automation rule {ruleId} not found");

        if (rule.IsEnabled == isEnabled)
        {
            return rule;
        }

        rule.IsEnabled = isEnabled;
        _db.AutomationRules.Update(rule);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Automation rule {Rule} updated: enabled={Enabled}", rule.Name, rule.IsEnabled);
        return rule;
    }

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
    private static readonly JsonSerializerOptions PackageSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AionDbContext _db;
    private readonly string _marketplaceFolder;
    private readonly ISecurityAuditService _securityAudit;
    private readonly ICurrentUserService _currentUserService;
    private readonly IModuleApplier _moduleApplier;
    private readonly ILifeService _timeline;

    public TemplateService(
        AionDbContext db,
        IOptions<MarketplaceOptions> options,
        ISecurityAuditService securityAudit,
        ICurrentUserService currentUserService,
        IModuleApplier moduleApplier,
        ILifeService timeline)
    {
        _db = db;
        ArgumentNullException.ThrowIfNull(options);
        _marketplaceFolder = options.Value.MarketplaceFolder ?? throw new InvalidOperationException("Marketplace folder is required");
        _securityAudit = securityAudit;
        _currentUserService = currentUserService;
        _moduleApplier = moduleApplier;
        _timeline = timeline;
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

        var entityIds = module.EntityTypes.Select(e => e.Id).ToList();
        var tables = await _db.Tables
            .Include(t => t.Fields)
            .Include(t => t.Views)
            .Where(t => entityIds.Contains(t.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var moduleSpec = BuildModuleSpec(module, tables);
        var payload = JsonSerializer.Serialize(moduleSpec, PackageSerializerOptions);
        var assetsManifest = BuildAssetsManifest();
        var assetsManifestJson = JsonSerializer.Serialize(assetsManifest, PackageSerializerOptions);
        var author = ResolveAuthor();
        var version = module.Version.ToString(CultureInfo.InvariantCulture);
        var signature = ComputeSignature(author, version, payload, assetsManifestJson);

        var package = new TemplatePackage
        {
            Name = module.Name,
            Description = module.Description,
            Payload = payload,
            Version = version,
            Author = author,
            Signature = signature,
            AssetsManifest = assetsManifestJson
        };

        await _db.Templates.AddAsync(package, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await CreateOrUpdateMarketplaceEntry(package, cancellationToken).ConfigureAwait(false);
        await _securityAudit.LogAsync(new SecurityAuditEvent(
            SecurityAuditCategory.ModuleExport,
            "module.exported",
            "module",
            module.Id,
            new Dictionary<string, object?>
            {
                ["moduleName"] = module.Name,
                ["templateId"] = package.Id,
                ["version"] = package.Version
            }), cancellationToken).ConfigureAwait(false);
        await _timeline.AddHistoryAsync(new S_HistoryEvent
        {
            Title = "Module exporté",
            Description = module.Name,
            OccurredAt = DateTimeOffset.UtcNow,
            ModuleId = module.Id
        }, cancellationToken).ConfigureAwait(false);
        return package;
    }

    public async Task<S_Module> ImportModuleAsync(TemplatePackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        NormalizePackage(package);
        ValidateOrUpdateSignature(package);

        var payload = ParseTemplatePayload(package.Payload, package.Name);
        var module = payload.Module ?? BuildModuleFromSpec(payload.ModuleSpec, package.Name);
        EnsureModuleMetadata(module);

        var existing = await _db.Modules
            .Include(m => m.EntityTypes).ThenInclude(e => e.Fields)
            .FirstOrDefaultAsync(m => m.Id == module.Id || m.Name == module.Name, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await _db.Modules.AddAsync(module, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            module = existing;
            UpsertModuleFromSpec(module, payload.ModuleSpec);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _moduleApplier.ApplyAsync(payload.ModuleSpec, cancellationToken: cancellationToken).ConfigureAwait(false);
        await CreateOrUpdateMarketplaceEntry(package, cancellationToken).ConfigureAwait(false);
        EnsureModuleMetadata(module);
        await _securityAudit.LogAsync(new SecurityAuditEvent(
            SecurityAuditCategory.ModuleImport,
            "module.imported",
            "module",
            module.Id,
            new Dictionary<string, object?>
            {
                ["moduleName"] = module.Name,
                ["templateId"] = package.Id,
                ["version"] = package.Version
            }), cancellationToken).ConfigureAwait(false);
        await _timeline.AddHistoryAsync(new S_HistoryEvent
        {
            Title = "Module importé",
            Description = module.Name,
            OccurredAt = DateTimeOffset.UtcNow,
            ModuleId = module.Id
        }, cancellationToken).ConfigureAwait(false);
        return module;
    }

    private async Task CreateOrUpdateMarketplaceEntry(TemplatePackage package, CancellationToken cancellationToken)
    {
        var fileName = Path.Combine(_marketplaceFolder, $"{package.Id}.json");
        var packageJson = JsonSerializer.Serialize(package, PackageSerializerOptions);
        await File.WriteAllTextAsync(fileName, packageJson, cancellationToken).ConfigureAwait(false);

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
            var package = await TryReadPackageAsync(file, cancellationToken).ConfigureAwait(false);
            var fileName = Path.GetFileNameWithoutExtension(file);
            var id = package?.Id ?? ResolveMarketplaceId(fileName);
            if (!await _db.Marketplace.AnyAsync(i => i.Id == id, cancellationToken).ConfigureAwait(false))
            {
                await _db.Marketplace.AddAsync(new MarketplaceItem
                {
                    Id = id,
                    Name = package?.Name ?? Path.GetFileName(file),
                    Category = "Module",
                    PackagePath = file
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await _db.Marketplace.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ModuleSpec BuildModuleSpec(S_Module module, IReadOnlyCollection<STable> tables)
    {
        var tableById = tables.ToDictionary(t => t.Id);
        var entityById = module.EntityTypes.ToDictionary(e => e.Id);
        var moduleSpec = new ModuleSpec
        {
            ModuleId = module.Id,
            Slug = ResolveModuleSlug(module.Name),
            DisplayName = module.Name,
            Description = module.Description,
            Tables = new List<TableSpec>()
        };

        foreach (var entity in module.EntityTypes)
        {
            tableById.TryGetValue(entity.Id, out var table);
            moduleSpec.Tables.Add(MapTable(entity, table, entityById));
        }

        return moduleSpec;
    }

    private static TableSpec MapTable(S_EntityType entity, STable? table, IReadOnlyDictionary<Guid, S_EntityType> entityById)
    {
        if (table is not null)
        {
            return new TableSpec
            {
                Id = table.Id,
                Slug = table.Name,
                DisplayName = table.DisplayName,
                Description = table.Description,
                IsSystem = table.IsSystem,
                SupportsSoftDelete = table.SupportsSoftDelete,
                HasAuditTrail = table.HasAuditTrail,
                DefaultView = table.DefaultView,
                RowLabelTemplate = table.RowLabelTemplate,
                Fields = table.Fields.Select(MapField).ToList(),
                Views = table.Views.Select(MapView).ToList()
            };
        }

        return new TableSpec
        {
            Id = entity.Id,
            Slug = entity.Name,
            DisplayName = entity.PluralName,
            Description = entity.Description,
            Fields = entity.Fields.Select(field => MapField(field, entityById)).ToList()
        };
    }

    private static FieldSpec MapField(SFieldDefinition field)
    {
        return new FieldSpec
        {
            Id = field.Id,
            Slug = field.Name,
            Label = field.Label,
            DataType = MapDataType(field.DataType),
            IsRequired = field.IsRequired,
            IsSearchable = field.IsSearchable,
            IsListVisible = field.IsListVisible,
            IsPrimaryKey = field.IsPrimaryKey,
            IsUnique = field.IsUnique,
            IsIndexed = field.IsIndexed,
            IsFilterable = field.IsFilterable,
            IsSortable = field.IsSortable,
            IsHidden = field.IsHidden,
            IsReadOnly = field.IsReadOnly,
            IsComputed = field.IsComputed,
            DefaultValue = DeserializeDefault(field.DefaultValue),
            EnumValues = string.IsNullOrWhiteSpace(field.EnumValues) ? null : field.EnumValues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Lookup = string.IsNullOrWhiteSpace(field.LookupTarget)
                ? null
                : new LookupSpec
                {
                    TargetTableSlug = field.LookupTarget!,
                    LabelField = field.LookupField
                },
            ComputedExpression = field.ComputedExpression,
            MinLength = field.MinLength,
            MaxLength = field.MaxLength,
            MinValue = field.MinValue,
            MaxValue = field.MaxValue,
            ValidationPattern = field.ValidationPattern,
            Placeholder = field.Placeholder,
            Unit = field.Unit
        };
    }

    private static FieldSpec MapField(S_Field field, IReadOnlyDictionary<Guid, S_EntityType> entityById)
    {
        LookupSpec? lookup = null;
        if (field.DataType == FieldDataType.Lookup && field.RelationTargetEntityTypeId.HasValue &&
            entityById.TryGetValue(field.RelationTargetEntityTypeId.Value, out var targetEntity))
        {
            lookup = new LookupSpec { TargetTableSlug = targetEntity.Name };
        }

        return new FieldSpec
        {
            Id = field.Id,
            Slug = field.Name,
            Label = field.Label,
            DataType = MapDataType(field.DataType),
            IsRequired = field.IsRequired,
            IsSearchable = field.IsSearchable,
            IsListVisible = field.IsListVisible,
            DefaultValue = DeserializeDefault(field.DefaultValue),
            EnumValues = string.IsNullOrWhiteSpace(field.EnumValues)
                ? null
                : field.EnumValues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Lookup = lookup
        };
    }

    private static ViewSpec MapView(SViewDefinition view)
    {
        return new ViewSpec
        {
            Id = view.Id,
            Slug = view.Name,
            DisplayName = view.DisplayName,
            Description = view.Description,
            Filter = DeserializeFilter(view.QueryDefinition),
            Sort = view.SortExpression,
            PageSize = view.PageSize,
            Visualization = view.Visualization,
            IsDefault = view.IsDefault
        };
    }

    private static TemplateAssetsManifest BuildAssetsManifest()
        => new();

    private string ResolveAuthor()
    {
        var userId = _currentUserService.GetCurrentUserId();
        return userId == Guid.Empty ? "unknown" : userId.ToString("D");
    }

    private static string ComputeSignature(string author, string version, string payload, string assetsManifest)
    {
        var input = $"{author}|{version}|{payload}|{assetsManifest}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void NormalizePackage(TemplatePackage package)
    {
        package.Version = string.IsNullOrWhiteSpace(package.Version) ? "1.0.0" : package.Version;
        package.Author = string.IsNullOrWhiteSpace(package.Author) ? "unknown" : package.Author;
        if (string.IsNullOrWhiteSpace(package.AssetsManifest))
        {
            package.AssetsManifest = JsonSerializer.Serialize(new TemplateAssetsManifest(), PackageSerializerOptions);
        }
    }

    private static void ValidateOrUpdateSignature(TemplatePackage package)
    {
        var signature = ComputeSignature(package.Author ?? string.Empty, package.Version, package.Payload, package.AssetsManifest ?? string.Empty);
        if (string.IsNullOrWhiteSpace(package.Signature))
        {
            package.Signature = signature;
            return;
        }

        if (!string.Equals(package.Signature, signature, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Template signature mismatch.");
        }
    }

    private static TemplatePayload ParseTemplatePayload(string payload, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("Template payload is empty.");
        }

        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("tables", out _))
        {
            var spec = JsonSerializer.Deserialize<ModuleSpec>(payload, PackageSerializerOptions) ?? new ModuleSpec();
            EnsureModuleSpecDefaults(spec, fallbackName);
            EnsureTableIds(spec);
            return new TemplatePayload(spec, null);
        }

        if (document.RootElement.TryGetProperty("entityTypes", out _))
        {
            var module = JsonSerializer.Deserialize<S_Module>(payload, PackageSerializerOptions) ?? new S_Module { Name = fallbackName };
            var spec = BuildModuleSpec(module, Array.Empty<STable>());
            EnsureModuleSpecDefaults(spec, fallbackName);
            EnsureTableIds(spec);
            return new TemplatePayload(spec, module);
        }

        var fallback = JsonSerializer.Deserialize<ModuleSpec>(payload, PackageSerializerOptions) ?? new ModuleSpec { Slug = fallbackName };
        EnsureModuleSpecDefaults(fallback, fallbackName);
        EnsureTableIds(fallback);
        return new TemplatePayload(fallback, null);
    }

    private static void EnsureModuleSpecDefaults(ModuleSpec spec, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(spec.Slug))
        {
            spec.Slug = ResolveModuleSlug(fallbackName);
        }

        if (string.IsNullOrWhiteSpace(spec.DisplayName))
        {
            spec.DisplayName = spec.Slug;
        }
    }

    private static void EnsureTableIds(ModuleSpec spec)
    {
        foreach (var table in spec.Tables)
        {
            table.Id ??= Guid.NewGuid();
        }
    }

    private static S_Module BuildModuleFromSpec(ModuleSpec spec, string fallbackName)
    {
        EnsureModuleSpecDefaults(spec, fallbackName);

        var module = new S_Module
        {
            Id = spec.ModuleId ?? Guid.NewGuid(),
            Name = spec.DisplayName ?? spec.Slug,
            Description = spec.Description
        };

        foreach (var table in spec.Tables)
        {
            var fields = table.Fields.Select(MapField).ToList();
            var entity = new S_EntityType
            {
                Id = table.Id ?? Guid.NewGuid(),
                ModuleId = module.Id,
                Name = table.Slug,
                PluralName = table.DisplayName ?? table.Slug,
                Description = table.Description ?? string.Empty,
                Fields = fields
            };

            foreach (var field in fields)
            {
                field.EntityTypeId = entity.Id;
            }

            table.Id = entity.Id;
            module.EntityTypes.Add(entity);
        }

        return module;
    }

    private static void UpsertModuleFromSpec(S_Module module, ModuleSpec spec)
    {
        foreach (var table in spec.Tables)
        {
            var entity = module.EntityTypes.FirstOrDefault(e =>
                (table.Id.HasValue && e.Id == table.Id.Value) ||
                string.Equals(e.Name, table.Slug, StringComparison.OrdinalIgnoreCase));

            if (entity is null)
            {
                entity = new S_EntityType
                {
                    Id = table.Id ?? Guid.NewGuid(),
                    ModuleId = module.Id,
                    Name = table.Slug,
                    PluralName = table.DisplayName ?? table.Slug,
                    Description = table.Description ?? string.Empty
                };
                module.EntityTypes.Add(entity);
            }
            else
            {
                entity.Name = table.Slug;
                entity.PluralName = table.DisplayName ?? table.Slug;
                entity.Description = table.Description ?? entity.Description;
            }

            foreach (var fieldSpec in table.Fields)
            {
                var field = entity.Fields.FirstOrDefault(f =>
                    (fieldSpec.Id.HasValue && f.Id == fieldSpec.Id.Value) ||
                    string.Equals(f.Name, fieldSpec.Slug, StringComparison.OrdinalIgnoreCase));

                if (field is null)
                {
                    field = MapField(fieldSpec);
                    field.EntityTypeId = entity.Id;
                    entity.Fields.Add(field);
                }
                else
                {
                    field.Name = fieldSpec.Slug;
                    field.Label = fieldSpec.Label;
                    field.DataType = ModuleFieldDataTypes.ToDomainType(fieldSpec.DataType);
                    field.IsRequired = fieldSpec.IsRequired;
                    field.IsSearchable = fieldSpec.IsSearchable;
                    field.IsListVisible = fieldSpec.IsListVisible;
                    field.DefaultValue = fieldSpec.DefaultValue?.GetRawText();
                    field.EnumValues = fieldSpec.EnumValues is null ? null : string.Join(",", fieldSpec.EnumValues);
                }
            }
        }
    }

    private static S_Field MapField(FieldSpec spec)
    {
        return new S_Field
        {
            Id = spec.Id ?? Guid.NewGuid(),
            Name = spec.Slug,
            Label = spec.Label,
            DataType = ModuleFieldDataTypes.ToDomainType(spec.DataType),
            IsRequired = spec.IsRequired,
            IsSearchable = spec.IsSearchable,
            IsListVisible = spec.IsListVisible,
            DefaultValue = spec.DefaultValue?.GetRawText(),
            EnumValues = spec.EnumValues is null ? null : string.Join(",", spec.EnumValues)
        };
    }

    private static string ResolveModuleSlug(string name)
        => string.IsNullOrWhiteSpace(name) ? "module" : name.Trim();

    private async Task<TemplatePackage?> TryReadPackageAsync(string filePath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        try
        {
            var package = JsonSerializer.Deserialize<TemplatePackage>(content, PackageSerializerOptions);
            return package is not null && !string.IsNullOrWhiteSpace(package.Payload) ? package : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Guid ResolveMarketplaceId(string fileName)
    {
        if (Guid.TryParse(fileName, out var parsed))
        {
            return parsed;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fileName));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string MapDataType(FieldDataType dataType)
    {
        return dataType switch
        {
            FieldDataType.Number or FieldDataType.Int => ModuleFieldDataTypes.Number,
            FieldDataType.Decimal => ModuleFieldDataTypes.Decimal,
            FieldDataType.Boolean => ModuleFieldDataTypes.Boolean,
            FieldDataType.Date => ModuleFieldDataTypes.Date,
            FieldDataType.DateTime => ModuleFieldDataTypes.DateTime,
            FieldDataType.Enum => ModuleFieldDataTypes.Enum,
            FieldDataType.Lookup => ModuleFieldDataTypes.Lookup,
            FieldDataType.File => ModuleFieldDataTypes.File,
            FieldDataType.Note => ModuleFieldDataTypes.Note,
            FieldDataType.Json => ModuleFieldDataTypes.Json,
            FieldDataType.Tags => ModuleFieldDataTypes.Tags,
            _ => ModuleFieldDataTypes.Text
        };
    }

    private static JsonElement? DeserializeDefault(string? defaultValue)
    {
        if (string.IsNullOrWhiteSpace(defaultValue))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(defaultValue);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse(JsonSerializer.Serialize(defaultValue)).RootElement.Clone();
        }
    }

    private static Dictionary<string, string?>? DeserializeFilter(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(definition);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record TemplatePayload(ModuleSpec ModuleSpec, S_Module? Module);
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

    public async Task<TimelinePage> GetTimelinePageAsync(TimelineQuery query, CancellationToken cancellationToken = default)
    {
        var take = query.NormalizedTake;
        var skip = query.NormalizedSkip;

        var eventsQuery = _db.HistoryEvents.Include(h => h.Links).AsQueryable();
        if (query.ModuleId.HasValue)
        {
            eventsQuery = eventsQuery.Where(h => h.ModuleId == query.ModuleId.Value);
        }

        if (query.From.HasValue)
        {
            eventsQuery = eventsQuery.Where(h => h.OccurredAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            eventsQuery = eventsQuery.Where(h => h.OccurredAt <= query.To.Value);
        }

        var results = await eventsQuery
            .OrderByDescending(h => h.OccurredAt)
            .ThenByDescending(h => h.Id)
            .Skip(skip)
            .Take(take + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = results.Count > take;
        if (hasMore)
        {
            results.RemoveAt(results.Count - 1);
        }

        var nextSkip = skip + results.Count;
        return new TimelinePage(results, hasMore, nextSkip);
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
