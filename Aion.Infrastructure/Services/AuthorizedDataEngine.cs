using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Domain.Logic;
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

    public async Task<F_Record> InsertAsync(Guid tableId, string dataJson, CancellationToken cancellationToken = default)
    {
        var payload = DynamicListLogic.DeserializePayload(dataJson);
        await EnsureWriteAuthorizedAsync(tableId, null, payload, cancellationToken).ConfigureAwait(false);
        return await _inner.InsertAsync(tableId, dataJson, cancellationToken).ConfigureAwait(false);
    }

    public async Task<F_Record> InsertAsync(Guid tableId, IDictionary<string, object?> data, CancellationToken cancellationToken = default)
    {
        await EnsureWriteAuthorizedAsync(tableId, null, data, cancellationToken).ConfigureAwait(false);
        return await _inner.InsertAsync(tableId, data, cancellationToken).ConfigureAwait(false);
    }

    public Task<F_Record?> GetAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.GetAsync(tableId, id, cancellationToken), cancellationToken, id);

    public Task<ResolvedRecord?> GetResolvedAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.GetResolvedAsync(tableId, id, cancellationToken), cancellationToken, id);

    public async Task<F_Record> UpdateAsync(Guid tableId, Guid id, string dataJson, CancellationToken cancellationToken = default)
    {
        var payload = DynamicListLogic.DeserializePayload(dataJson);
        await EnsureWriteAuthorizedAsync(tableId, id, payload, cancellationToken).ConfigureAwait(false);
        return await _inner.UpdateAsync(tableId, id, dataJson, cancellationToken).ConfigureAwait(false);
    }

    public async Task<F_Record> UpdateAsync(Guid tableId, Guid id, IDictionary<string, object?> data, CancellationToken cancellationToken = default)
    {
        await EnsureWriteAuthorizedAsync(tableId, id, data, cancellationToken).ConfigureAwait(false);
        return await _inner.UpdateAsync(tableId, id, data, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Delete, tableId, () => _inner.DeleteAsync(tableId, id, cancellationToken), cancellationToken, id);

    public Task<IEnumerable<ChangeSet>> GetHistoryAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.GetHistoryAsync(tableId, recordId, cancellationToken), cancellationToken, recordId);

    public Task<int> CountAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.CountAsync(tableId, spec, cancellationToken), cancellationToken);

    public Task<IEnumerable<F_Record>> QueryAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.QueryAsync(tableId, spec, cancellationToken), cancellationToken);

    public Task<IEnumerable<ResolvedRecord>> QueryResolvedAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.QueryResolvedAsync(tableId, spec, cancellationToken), cancellationToken);

    public Task<IEnumerable<RecordSearchHit>> SearchAsync(Guid tableId, string query, SearchOptions? options = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.SearchAsync(tableId, query, options, cancellationToken), cancellationToken);

    public Task<IEnumerable<RecordSearchHit>> SearchSmartAsync(Guid tableId, string query, SearchOptions? options = null, CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.SearchSmartAsync(tableId, query, options, cancellationToken), cancellationToken);

    public Task<KnowledgeEdge> LinkRecordsAsync(
        Guid fromTableId,
        Guid fromRecordId,
        Guid toTableId,
        Guid toRecordId,
        KnowledgeRelationType relationType,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            PermissionAction.Write,
            fromTableId,
            () => _inner.LinkRecordsAsync(fromTableId, fromRecordId, toTableId, toRecordId, relationType, cancellationToken),
            cancellationToken,
            fromRecordId);

    public Task<KnowledgeGraphSlice> GetKnowledgeGraphAsync(
        Guid tableId,
        Guid recordId,
        int depth = 1,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(PermissionAction.Read, tableId, () => _inner.GetKnowledgeGraphAsync(tableId, recordId, depth, cancellationToken), cancellationToken, recordId);

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

    private async Task EnsureWriteAuthorizedAsync(Guid tableId, Guid? recordId, IDictionary<string, object?> data, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetCurrentUserId();
        var scope = recordId.HasValue
            ? PermissionScope.ForRecord(tableId, recordId.Value)
            : PermissionScope.ForTable(tableId);

        var result = await _authorizationService.AuthorizeAsync(userId, PermissionAction.Write, scope, cancellationToken).ConfigureAwait(false);
        if (result.IsAllowed)
        {
            return;
        }

        var fieldsToCheck = await ResolveFieldsToAuthorizeAsync(tableId, recordId, data, cancellationToken).ConfigureAwait(false);
        if (fieldsToCheck.Count == 0)
        {
            _logger.LogWarning("Access denied for user {UserId} on table {TableId} for action {Action}: {Reason}", userId, tableId, PermissionAction.Write, result.Reason);
            throw new InvalidOperationException(result.Reason ?? "Access denied.");
        }

        foreach (var fieldName in fieldsToCheck)
        {
            var fieldScope = recordId.HasValue
                ? PermissionScope.ForRecordField(tableId, recordId.Value, fieldName)
                : PermissionScope.ForField(tableId, fieldName);

            var fieldResult = await _authorizationService.AuthorizeAsync(userId, PermissionAction.Write, fieldScope, cancellationToken).ConfigureAwait(false);
            if (!fieldResult.IsAllowed)
            {
                _logger.LogWarning(
                    "Access denied for user {UserId} on table {TableId} field {FieldName} for action {Action}: {Reason}",
                    userId,
                    tableId,
                    fieldName,
                    PermissionAction.Write,
                    fieldResult.Reason);
                throw new InvalidOperationException(fieldResult.Reason ?? "Access denied.");
            }
        }
    }

    private async Task<HashSet<string>> ResolveFieldsToAuthorizeAsync(
        Guid tableId,
        Guid? recordId,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken)
    {
        var fields = new HashSet<string>(data.Keys, StringComparer.OrdinalIgnoreCase);
        if (!recordId.HasValue)
        {
            return fields;
        }

        var existing = await _inner.GetAsync(tableId, recordId.Value, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return fields;
        }

        var existingValues = DynamicListLogic.DeserializePayload(existing.DataJson);
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fieldName, value) in data)
        {
            if (!existingValues.TryGetValue(fieldName, out var existingValue) || !AreValuesEquivalent(value, existingValue))
            {
                changed.Add(fieldName);
            }
        }

        return changed;
    }

    private static bool AreValuesEquivalent(object? left, object? right)
    {
        var normalizedLeft = NormalizeValue(left);
        var normalizedRight = NormalizeValue(right);

        if (normalizedLeft is null && normalizedRight is null)
        {
            return true;
        }

        if (normalizedLeft is null || normalizedRight is null)
        {
            return false;
        }

        if (normalizedLeft is Guid leftGuid && normalizedRight is string rightString && Guid.TryParse(rightString, out var parsedRight))
        {
            return leftGuid == parsedRight;
        }

        if (normalizedRight is Guid rightGuid && normalizedLeft is string leftString && Guid.TryParse(leftString, out var parsedLeft))
        {
            return rightGuid == parsedLeft;
        }

        if (IsNumeric(normalizedLeft) && IsNumeric(normalizedRight))
        {
            return NormalizeNumber(normalizedLeft) == NormalizeNumber(normalizedRight);
        }

        return normalizedLeft.Equals(normalizedRight);
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt64(out var l) => l,
                JsonValueKind.Number when element.TryGetDecimal(out var d) => d,
                JsonValueKind.Number when element.TryGetDouble(out var dbl) => dbl,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };
        }

        return value;
    }

    private static bool IsNumeric(object value)
    {
        var typeCode = Type.GetTypeCode(value.GetType());
        return typeCode is TypeCode.Byte
            or TypeCode.SByte
            or TypeCode.UInt16
            or TypeCode.UInt32
            or TypeCode.UInt64
            or TypeCode.Int16
            or TypeCode.Int32
            or TypeCode.Int64
            or TypeCode.Decimal
            or TypeCode.Double
            or TypeCode.Single;
    }

    private static decimal NormalizeNumber(object value)
        => Convert.ToDecimal(value, CultureInfo.InvariantCulture);
}
