using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Microsoft.Extensions.Logging;

namespace Aion.AppHost.Services;

public sealed class RecordQueryService : IRecordQueryService
{
    private readonly IDataEngine _dataEngine;
    private readonly IAppInitializationService _initializationService;
    private readonly ILogger<RecordQueryService> _logger;

    public RecordQueryService(IDataEngine dataEngine, IAppInitializationService initializationService, ILogger<RecordQueryService> logger)
    {
        _dataEngine = dataEngine;
        _initializationService = initializationService;
        _logger = logger;
    }

    public async Task<RecordPage<F_Record>> QueryAsync(Guid tableId, QuerySpec query, CancellationToken cancellationToken = default)
    {
        await _initializationService.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var normalized = NormalizeQuery(query);
        var records = (await _dataEngine.QueryAsync(tableId, normalized, cancellationToken).ConfigureAwait(false)).ToList();
        var total = await _dataEngine.CountAsync(tableId, normalized, cancellationToken).ConfigureAwait(false);
        return new RecordPage<F_Record>(records, total);
    }

    public async Task<int> CountAsync(Guid tableId, QuerySpec query, CancellationToken cancellationToken = default)
    {
        await _initializationService.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var normalized = NormalizeQuery(query, ignorePaging: true);
        return await _dataEngine.CountAsync(tableId, normalized, cancellationToken).ConfigureAwait(false);
    }

    public async Task<F_Record?> GetAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default)
    {
        await _initializationService.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await _dataEngine.GetAsync(tableId, recordId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ResolvedRecord?> GetResolvedAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default)
    {
        await _initializationService.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await _dataEngine.GetResolvedAsync(tableId, recordId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<F_Record> SaveAsync(Guid tableId, Guid? recordId, IDictionary<string, object?> data, CancellationToken cancellationToken = default)
    {
        await _initializationService.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (recordId.HasValue)
        {
            return await _dataEngine.UpdateAsync(tableId, recordId.Value, data, cancellationToken).ConfigureAwait(false);
        }

        return await _dataEngine.InsertAsync(tableId, data, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default)
    {
        await _initializationService.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _dataEngine.DeleteAsync(tableId, recordId, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Record {RecordId} deleted from table {TableId}", recordId, tableId);
    }

    private static QuerySpec NormalizeQuery(QuerySpec? query, bool ignorePaging = false)
    {
        query ??= new QuerySpec();

        return new QuerySpec
        {
            Filters = query.Filters.ToList(),
            FullText = query.FullText,
            OrderBy = query.OrderBy,
            Descending = query.Descending,
            Skip = ignorePaging ? null : query.Skip,
            Take = ignorePaging ? null : query.Take,
            Projection = query.Projection,
            View = query.View
        };
    }
}
