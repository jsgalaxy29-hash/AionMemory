using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services;

public sealed class AuthorizedDataEngine : IDataEngine
{
    private readonly IAionDataEngine _inner;
    private readonly IAuthorizationService _authorizationService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AuthorizedDataEngine> _logger;

    public AuthorizedDataEngine(
        IAionDataEngine inner,
        IAuthorizationService authorizationService,
        ICurrentUserService currentUserService,
        ILogger<AuthorizedDataEngine> logger)
    {
        _inner = inner;
        _authorizationService = authorizationService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public Task<STable> CreateTableAsync(STable table, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.ManageSchema, table.Id, () => _inner.CreateTableAsync(table, cancellationToken), cancellationToken);

    public Task<STable?> GetTableAsync(Guid tableId, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.GetTableAsync(tableId, cancellationToken), cancellationToken);

    public Task<IEnumerable<STable>> GetTablesAsync(CancellationToken cancellationToken = default)
        => _inner.GetTablesAsync(cancellationToken);

    public Task<IEnumerable<SViewDefinition>> GenerateSimpleViewsAsync(Guid tableId, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.ManageSchema, tableId, () => _inner.GenerateSimpleViewsAsync(tableId, cancellationToken), cancellationToken);

    public Task<F_Record> InsertAsync(Guid tableId, string dataJson, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Write, tableId, () => _inner.InsertAsync(tableId, dataJson, cancellationToken), cancellationToken);

    public Task<F_Record> InsertAsync(Guid tableId, IDictionary<string, object?> data, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Write, tableId, () => _inner.InsertAsync(tableId, data, cancellationToken), cancellationToken);

    public Task<F_Record?> GetAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.GetAsync(tableId, id, cancellationToken), cancellationToken, id);

    public Task<ResolvedRecord?> GetResolvedAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.GetResolvedAsync(tableId, id, cancellationToken), cancellationToken, id);

    public Task<F_Record> UpdateAsync(Guid tableId, Guid id, string dataJson, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Write, tableId, () => _inner.UpdateAsync(tableId, id, dataJson, cancellationToken), cancellationToken, id);

    public Task<F_Record> UpdateAsync(Guid tableId, Guid id, IDictionary<string, object?> data, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Write, tableId, () => _inner.UpdateAsync(tableId, id, data, cancellationToken), cancellationToken, id);

    public Task DeleteAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Delete, tableId, () => _inner.DeleteAsync(tableId, id, cancellationToken), cancellationToken, id);

    public Task<int> CountAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.CountAsync(tableId, spec, cancellationToken), cancellationToken);

    public Task<IEnumerable<F_Record>> QueryAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.QueryAsync(tableId, spec, cancellationToken), cancellationToken);

    public Task<IEnumerable<ResolvedRecord>> QueryResolvedAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.QueryResolvedAsync(tableId, spec, cancellationToken), cancellationToken);

    public Task<IEnumerable<RecordSearchHit>> SearchAsync(Guid tableId, string query, SearchOptions? options = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.SearchAsync(tableId, query, options, cancellationToken), cancellationToken);

    private async Task<T> ExecuteAsync<T>(PermissionAction action, Guid tableId, Func<Task<T>> callback, CancellationToken cancellationToken, Guid? recordId = null)
    {
        await EnsureAuthorizedAsync(action, tableId, recordId, cancellationToken).ConfigureAwait(false);
        return await callback().ConfigureAwait(false);
    }

    private async Task ExecuteAsync(PermissionAction action, Guid tableId, Func<Task> callback, CancellationToken cancellationToken, Guid? recordId = null)
    {
        await EnsureAuthorizedAsync(action, tableId, recordId, cancellationToken).ConfigureAwait(false);
        await callback().ConfigureAwait(false);
    }

    private async Task EnsureAuthorizedAsync(PermissionAction action, Guid tableId, Guid? recordId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetCurrentUserId();
        var scope = recordId.HasValue
            ? PermissionScope.ForRecord(tableId, recordId.Value)
            : PermissionScope.ForTable(tableId);

        var result = await _authorizationService.AuthorizeAsync(userId, action, scope, cancellationToken).ConfigureAwait(false);
        if (!result.IsAllowed)
        {
            _logger.LogWarning("Access denied for user {UserId} on table {TableId} for action {Action}: {Reason}", userId, tableId, action, result.Reason);
            throw new InvalidOperationException(result.Reason ?? "Access denied.");
        }
    }
}
