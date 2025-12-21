using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aion.AI;
using Aion.Domain;
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
        entityType.ModuleId = moduleId;
        await _db.EntityTypes.AddAsync(entityType, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Entity type {Name} added to module {Module}", entityType.Name, moduleId);
        return entityType;
    }

    public async Task<S_Module> CreateModuleAsync(S_Module module, CancellationToken cancellationToken = default)
    {
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
}

public sealed class AionDataEngine : IAionDataEngine, IDataEngine
{
    private readonly AionDbContext _db;
    private readonly ILogger<AionDataEngine> _logger;
    private readonly ISearchService _search;

    public AionDataEngine(AionDbContext db, ILogger<AionDataEngine> logger, ISearchService search)
    {
        _db = db;
        _logger = logger;
        _search = search;
    }

    public async Task<STable> CreateTableAsync(STable table, CancellationToken cancellationToken = default)
    {
        var added = EnsureBasicViews(table);
        NormalizeTableDefinition(table);
        ValidateTableDefinition(table);

        await _db.Tables.AddAsync(table, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Table {Table} created with {FieldCount} fields", table.Name, table.Fields.Count);
        if (added > 0)
        {
            _logger.LogInformation("{ViewCount} default view(s) created for table {Table}", added, table.Name);
        }
        return table;
    }

    public async Task<IEnumerable<SViewDefinition>> GenerateSimpleViewsAsync(Guid tableId, CancellationToken cancellationToken = default)
    {
        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        var added = EnsureBasicViews(table);
        if (added > 0)
        {
            NormalizeTableDefinition(table);
            _db.Tables.Update(table);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Generated {Count} simple view(s) for table {Table}", added, table.Name);
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
        var (table, validated) = await ValidateRecordAsync(tableId, data, null, cancellationToken).ConfigureAwait(false);

        var record = new F_Record
        {
            TableId = tableId,
            DataJson = validated.DataJson,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _db.Records.AddAsync(record, cancellationToken).ConfigureAwait(false);
        await UpsertRecordIndexesAsync(table, record, validated.Values, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Record {RecordId} inserted for table {TableId}", record.Id, tableId);
        await _search.IndexRecordAsync(record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public Task<F_Record> UpdateAsync(Guid tableId, Guid id, string dataJson, CancellationToken cancellationToken = default)
        => UpdateAsync(tableId, id, ParseJsonPayload(dataJson), cancellationToken);

    public async Task<F_Record> UpdateAsync(Guid tableId, Guid id, IDictionary<string, object?> data, CancellationToken cancellationToken = default)
    {
        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        var record = await FilterByTable(_db.Records, table).FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Record {id} not found for table {tableId}");

        var validated = await ValidateRecordAsync(table, data, id, cancellationToken).ConfigureAwait(false);

        record.DataJson = validated.DataJson;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        _db.Records.Update(record);
        await UpsertRecordIndexesAsync(table, record, validated.Values, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Record {RecordId} updated for table {TableId}", id, tableId);
        await _search.IndexRecordAsync(record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task DeleteAsync(Guid tableId, Guid id, CancellationToken cancellationToken = default)
    {
        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        var record = await FilterByTable(_db.Records, table).FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        if (table.SupportsSoftDelete)
        {
            record.DeletedAt = DateTimeOffset.UtcNow;
            _db.Records.Update(record);
        }
        else
        {
            _db.Records.Remove(record);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Record {RecordId} deleted for table {TableId}", id, tableId);
        await _search.RemoveAsync("Record", id, cancellationToken).ConfigureAwait(false);
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

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<ResolvedRecord>> QueryResolvedAsync(Guid tableId, QuerySpec? spec = null, CancellationToken cancellationToken = default)
    {
        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        var results = await QueryAsync(tableId, spec, cancellationToken).ConfigureAwait(false);
        var resolved = new List<ResolvedRecord>();

        foreach (var record in results)
        {
            var resolvedRecord = await BuildResolvedRecordAsync(record, table, cancellationToken).ConfigureAwait(false);
            resolved.Add(resolvedRecord);
        }

        return resolved;
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
    private readonly string _storageRoot;
    private readonly StorageOptions _options;
    private readonly AionDbContext _db;
    private readonly ISearchService _search;
    private readonly ILogger<FileStorageService> _logger;
    private readonly byte[] _encryptionKey;

    public FileStorageService(IOptions<StorageOptions> options, AionDbContext db, ISearchService search, ILogger<FileStorageService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _storageRoot = _options.RootPath ?? throw new InvalidOperationException("Storage root path must be provided");
        _db = db;
        _search = search;
        _logger = logger;
        _encryptionKey = DeriveKey(_options.EncryptionKey ?? throw new InvalidOperationException("Storage encryption key missing"));
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<F_File> SaveAsync(string fileName, Stream content, string mimeType, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(_storageRoot, id.ToString());
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (buffer.Length > _options.MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"File {fileName} exceeds the configured limit of {_options.MaxFileSizeBytes / (1024 * 1024)} MB");
        }

        await EnsureStorageQuotaAsync(buffer.Length, cancellationToken).ConfigureAwait(false);

        buffer.Position = 0;
        var hash = ComputeHash(buffer);
        buffer.Position = 0;

        var encryptedPayload = Encrypt(buffer.ToArray());
        await File.WriteAllBytesAsync(path, encryptedPayload, cancellationToken).ConfigureAwait(false);

        var file = new F_File
        {
            Id = id,
            FileName = fileName,
            MimeType = mimeType,
            StoragePath = path,
            Size = buffer.Length,
            Sha256 = hash,
            UploadedAt = DateTimeOffset.UtcNow
        };

        await _db.Files.AddAsync(file, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("File {FileId} stored at {Path}", id, path);
        await _search.IndexFileAsync(file, cancellationToken).ConfigureAwait(false);
        return file;
    }

    public async Task<Stream> OpenAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await _db.Files.FirstAsync(f => f.Id == fileId, cancellationToken).ConfigureAwait(false);
        var encryptedBytes = await File.ReadAllBytesAsync(file.StoragePath, cancellationToken).ConfigureAwait(false);
        var decrypted = Decrypt(encryptedBytes);

        if (_options.RequireIntegrityCheck && !string.IsNullOrWhiteSpace(file.Sha256))
        {
            var computedHash = ComputeHash(new MemoryStream(decrypted));
            if (!string.Equals(computedHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("File integrity validation failed");
            }
        }

        return new MemoryStream(decrypted, writable: false);
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

        if (File.Exists(file.StoragePath))
        {
            File.Delete(file.StoragePath);
        }

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

    private static byte[] DeriveKey(string material)
    {
        try
        {
            return Convert.FromBase64String(material);
        }
        catch (FormatException)
        {
            return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material));
        }
    }

    private byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_encryptionKey);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);
        return payload;
    }

    private byte[] Decrypt(ReadOnlySpan<byte> payload)
    {
        var nonce = payload[..12];
        var tag = payload[12..28];
        var cipher = payload[28..];
        var plaintext = new byte[cipher.Length];

        using var aes = new AesGcm(_encryptionKey);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return plaintext;
    }

    private static string ComputeHash(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        return Convert.ToHexString(hash);
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
