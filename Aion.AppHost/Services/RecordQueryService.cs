using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Microsoft.Extensions.Logging;

namespace Aion.AppHost.Services;

public sealed class RecordQueryService : IRecordQueryService
{
    private readonly IDataEngine _dataEngine;
    private readonly IAppInitializationService _initializationService;
    private readonly IConnectivityService _connectivityService;
    private readonly IOfflineActionQueue _offlineQueue;
    private readonly IOfflineActionReplayService _offlineReplay;
    private readonly ILogger<RecordQueryService> _logger;

    public RecordQueryService(
        IDataEngine dataEngine,
        IAppInitializationService initializationService,
        IConnectivityService connectivityService,
        IOfflineActionQueue offlineQueue,
        IOfflineActionReplayService offlineReplay,
        ILogger<RecordQueryService> logger)
    {
        _dataEngine = dataEngine;
        _initializationService = initializationService;
        _connectivityService = connectivityService;
        _offlineQueue = offlineQueue;
        _offlineReplay = offlineReplay;
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
            var record = await _dataEngine.UpdateAsync(tableId, recordId.Value, data, cancellationToken).ConfigureAwait(false);
            if (!IsOnline())
            {
                await EnqueueOfflineActionAsync(tableId, record.Id, OfflineActionType.Update, record.DataJson, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _offlineReplay.ReplayPendingAsync(cancellationToken).ConfigureAwait(false);
            }

            return record;
        }

        var created = await _dataEngine.InsertAsync(tableId, data, cancellationToken).ConfigureAwait(false);
        if (!IsOnline())
        {
            await EnqueueOfflineActionAsync(tableId, created.Id, OfflineActionType.Create, created.DataJson, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _offlineReplay.ReplayPendingAsync(cancellationToken).ConfigureAwait(false);
        }

        return created;
    }

    public async Task DeleteAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default)
    {
        await _initializationService.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = string.Empty;
        if (!IsOnline())
        {
            var existing = await _dataEngine.GetAsync(tableId, recordId, cancellationToken).ConfigureAwait(false);
            snapshot = existing?.DataJson ?? "{}";
        }

        await _dataEngine.DeleteAsync(tableId, recordId, cancellationToken).ConfigureAwait(false);
        if (!IsOnline())
        {
            await EnqueueOfflineActionAsync(tableId, recordId, OfflineActionType.Delete, snapshot, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _offlineReplay.ReplayPendingAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Record {RecordId} deleted from table {TableId}", recordId, tableId);
    }

    private bool IsOnline() => _connectivityService.IsOnline;

    private async Task EnqueueOfflineActionAsync(
        Guid tableId,
        Guid recordId,
        OfflineActionType actionType,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        var payload = string.IsNullOrWhiteSpace(payloadJson)
            ? JsonSerializer.Serialize(new Dictionary<string, object?>())
            : payloadJson;

        var action = new OfflineRecordAction(
            Guid.NewGuid(),
            tableId,
            recordId,
            actionType,
            payload,
            DateTimeOffset.UtcNow,
            OfflineActionStatus.Pending,
            null,
            null);

        await _offlineQueue.EnqueueAsync(action, cancellationToken).ConfigureAwait(false);
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
