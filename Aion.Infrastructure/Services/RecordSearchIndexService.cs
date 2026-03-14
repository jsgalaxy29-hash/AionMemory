using System.Globalization;
using System.Text;
using System.Text.Json;
using Aion.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services;

public sealed class RecordSearchIndexService
{
    private readonly AionDbContext _db;
    private readonly ILogger<RecordSearchIndexService> _logger;
    private readonly Dictionary<Guid, STable?> _tableCache = new();
    private readonly Dictionary<string, STable?> _lookupTableCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, Dictionary<Guid, string?>> _lookupLabelCache = new();

    public RecordSearchIndexService(AionDbContext db, ILogger<RecordSearchIndexService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RebuildAsync(CancellationToken cancellationToken = default)
    {
        await EnsureRecordSearchIndexExistsAsync(cancellationToken).ConfigureAwait(false);
        await _db.Database.ExecuteSqlRawAsync("DELETE FROM RecordSearch;", cancellationToken).ConfigureAwait(false);

        var records = await _db.Records.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var record in records)
        {
            await UpdateRecordAsync(record, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Record search index rebuilt with {Count} record(s).", records.Count);
    }

    public async Task UpdateRecordAsync(F_Record record, CancellationToken cancellationToken = default)
    {
        var table = await GetTableAsync(record.TableId, cancellationToken).ConfigureAwait(false);
        if (table is null)
        {
            return;
        }

        var content = await BuildSearchContentAsync(table, record, cancellationToken).ConfigureAwait(false);
        await UpsertRecordSearchAsync(record, content, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureRecordSearchIndexExistsAsync(CancellationToken cancellationToken)
    {
        if (await TableExistsAsync("RecordSearch", cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(SearchIndexSql.RecordSearch, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = $name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (shouldClose)
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }

        return result is string;
    }

    private async Task<STable?> GetTableAsync(Guid tableId, CancellationToken cancellationToken)
    {
        if (_tableCache.TryGetValue(tableId, out var cached))
        {
            return cached;
        }

        var table = await _db.Tables
            .Include(t => t.Fields)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tableId, cancellationToken)
            .ConfigureAwait(false);

        _tableCache[tableId] = table;
        return table;
    }

    private async Task<string> BuildSearchContentAsync(STable table, F_Record record, CancellationToken cancellationToken)
    {
        var values = ParseJsonPayload(record.DataJson);
        var tokens = new List<string>();

        foreach (var value in values.Values)
        {
            AppendToken(tokens, value);
        }

        foreach (var field in table.Fields.Where(f => f.IsComputed && !string.IsNullOrWhiteSpace(f.ComputedExpression)))
        {
            var computed = EvaluateComputedExpression(field.ComputedExpression!, values);
            if (!string.IsNullOrWhiteSpace(computed))
            {
                tokens.Add(computed);
            }
        }

        var lookupTokens = await ResolveLookupLabelsAsync(table, values, cancellationToken).ConfigureAwait(false);
        tokens.AddRange(lookupTokens);

        var fileTokens = await BuildLinkedFileTokensAsync(record.Id, cancellationToken).ConfigureAwait(false);
        tokens.AddRange(fileTokens);

        var normalized = string.Join(" ", tokens.Where(token => !string.IsNullOrWhiteSpace(token)));
        return string.IsNullOrWhiteSpace(normalized)
            ? record.DataJson
            : $"{normalized} {record.DataJson}".Trim();
    }

    private async Task<IReadOnlyCollection<string>> BuildLinkedFileTokensAsync(Guid recordId, CancellationToken cancellationToken)
    {
        var tokens = new List<string>();
        var linked = await _db.FileLinks
            .AsNoTracking()
            .Where(link => link.TargetId == recordId)
            .Join(_db.Files.AsNoTracking(),
                link => link.FileId,
                file => file.Id,
                (_, file) => file)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var file in linked)
        {
            AppendToken(tokens, file.FileName);
            AppendToken(tokens, file.OcrText);
        }

        return tokens;
    }

    private async Task<IReadOnlyCollection<string>> ResolveLookupLabelsAsync(
        STable table,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken)
    {
        var resolved = new List<string>();

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

            var targetTable = await FindLookupTableAsync(field, cancellationToken).ConfigureAwait(false);
            if (targetTable is null)
            {
                continue;
            }

            var label = await GetLookupLabelAsync(targetTable, field, lookupId.Value, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(label))
            {
                resolved.Add(label);
            }
        }

        return resolved;
    }

    private async Task<string?> GetLookupLabelAsync(STable targetTable, SFieldDefinition field, Guid lookupId, CancellationToken cancellationToken)
    {
        if (_lookupLabelCache.TryGetValue(targetTable.Id, out var labels) && labels.TryGetValue(lookupId, out var cached))
        {
            return cached;
        }

        labels ??= new Dictionary<Guid, string?>();
        _lookupLabelCache[targetTable.Id] = labels;

        var targetRecord = await _db.Records.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TableId == targetTable.Id && r.Id == lookupId, cancellationToken)
            .ConfigureAwait(false);

        if (targetRecord is null)
        {
            labels[lookupId] = null;
            return null;
        }

        var targetValues = ParseJsonPayload(targetRecord.DataJson);
        var label = ResolveLabel(targetTable, targetValues, field);
        labels[lookupId] = label;
        return label;
    }

    private async Task<STable?> FindLookupTableAsync(SFieldDefinition field, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(field.LookupTarget))
        {
            return null;
        }

        if (_lookupTableCache.TryGetValue(field.LookupTarget, out var cached))
        {
            return cached;
        }

        STable? targetTable;
        if (Guid.TryParse(field.LookupTarget, out var tableId))
        {
            targetTable = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            targetTable = await _db.Tables
                .Include(t => t.Fields)
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Name == field.LookupTarget, cancellationToken)
                .ConfigureAwait(false);
        }

        _lookupTableCache[field.LookupTarget] = targetTable;
        return targetTable;
    }

    private static void AppendToken(ICollection<string> tokens, object? value)
    {
        if (value is null)
        {
            return;
        }

        var text = value switch
        {
            string s => s,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(text))
        {
            tokens.Add(text);
        }
    }

    private static string? EvaluateComputedExpression(string expression, IReadOnlyDictionary<string, object?> values)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var trimmed = expression.Trim();
        if (trimmed.Contains("{{", StringComparison.Ordinal) && trimmed.Contains("}}", StringComparison.Ordinal))
        {
            return ApplyRowLabelTemplate(trimmed, values);
        }

        if (!trimmed.StartsWith("concat(", StringComparison.OrdinalIgnoreCase) || !trimmed.EndsWith(")", StringComparison.Ordinal))
        {
            return null;
        }

        var inner = trimmed["concat(".Length..^1];
        var tokens = ParseConcatArguments(inner);
        var builder = new StringBuilder();
        foreach (var token in tokens)
        {
            var resolved = ResolveConcatToken(token, values);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                builder.Append(resolved);
            }
        }

        var result = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static IEnumerable<string> ParseConcatArguments(string input)
    {
        var results = new List<string>();
        var current = new StringBuilder();
        char? quote = null;

        foreach (var ch in input)
        {
            if (quote.HasValue)
            {
                if (ch == quote.Value)
                {
                    quote = null;
                }

                current.Append(ch);
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
                current.Append(ch);
                continue;
            }

            if (ch == ',')
            {
                results.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            results.Add(current.ToString());
        }

        return results;
    }

    private static string? ResolveConcatToken(string token, IReadOnlyDictionary<string, object?> values)
    {
        var trimmed = token.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if ((trimmed.StartsWith('\'') && trimmed.EndsWith('\'')) || (trimmed.StartsWith('"') && trimmed.EndsWith('"')))
        {
            return trimmed.Length > 1 ? trimmed[1..^1] : string.Empty;
        }

        if (values.TryGetValue(trimmed, out var value))
        {
            return value?.ToString();
        }

        return null;
    }

    private static Guid? ParseGuid(object value)
        => value switch
        {
            Guid g => g,
            string s when Guid.TryParse(s, out var parsed) => parsed,
            JsonElement element when element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var parsed) => parsed,
            _ => null
        };

    private static Dictionary<string, object?> ParseJsonPayload(string dataJson)
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

    private async Task UpsertRecordSearchAsync(F_Record record, string content, CancellationToken cancellationToken)
    {
        await EnsureRecordSearchIndexExistsAsync(cancellationToken).ConfigureAwait(false);
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM RecordSearch WHERE RecordId = {record.Id};",
            cancellationToken).ConfigureAwait(false);
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO RecordSearch(RecordId, EntityTypeId, Content) VALUES ({record.Id}, {record.TableId}, {content ?? string.Empty});",
            cancellationToken).ConfigureAwait(false);
    }
}
