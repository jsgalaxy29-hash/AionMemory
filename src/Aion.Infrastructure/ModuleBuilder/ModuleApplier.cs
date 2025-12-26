using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.ModuleBuilder;

public sealed class ModuleApplier : IModuleApplier
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly AionDbContext _db;
    private readonly IModuleValidator _validator;
    private readonly ILogger<ModuleApplier> _logger;
    private readonly IOperationScopeFactory _operationScopeFactory;

    public ModuleApplier(AionDbContext db, IModuleValidator validator, ILogger<ModuleApplier> logger, IOperationScopeFactory operationScopeFactory)
    {
        _db = db;
        _validator = validator;
        _logger = logger;
        _operationScopeFactory = operationScopeFactory;
    }

    public async Task<IReadOnlyList<STable>> ApplyAsync(ModuleSpec spec, CancellationToken cancellationToken = default)
    {
        using var operation = BeginOperation(spec.Slug);
        await _validator.ValidateAndThrowAsync(spec, cancellationToken).ConfigureAwait(false);

        var applied = new List<STable>();
        foreach (var tableSpec in spec.Tables)
        {
            var table = await _db.Tables
                .Include(t => t.Fields)
                .Include(t => t.Views)
                .FirstOrDefaultAsync(t => t.Name == tableSpec.Slug, cancellationToken)
                .ConfigureAwait(false);

            if (table is null)
            {
                table = new STable
                {
                    Id = tableSpec.Id ?? Guid.NewGuid(),
                    Name = tableSpec.Slug
                };
                _db.Tables.Add(table);
            }

            UpdateTable(table, tableSpec);
            var fieldSync = UpsertFields(table, tableSpec.Fields);
            await ApplyFieldRenamesAsync(table, fieldSync.Renames, cancellationToken).ConfigureAwait(false);
            await DeactivateRemovedFieldsAsync(table, fieldSync.RemovedFields, cancellationToken).ConfigureAwait(false);
            UpsertViews(table, tableSpec.Views);
            EnsureDefaults(table, tableSpec);

            applied.Add(table);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Applied module spec {Slug} with {TableCount} table(s) in {ElapsedMs}ms",
            spec.Slug,
            applied.Count,
            operation.Elapsed.TotalMilliseconds);
        return applied;
    }

    private OperationScopeContext BeginOperation(string moduleSlug)
    {
        var scope = _operationScopeFactory.Start("Module.Apply");
        var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Operation"] = "Module.Apply",
            ["CorrelationId"] = scope.Context.CorrelationId,
            ["OperationId"] = scope.Context.OperationId,
            ["ModuleSpec"] = moduleSlug
        }) ?? NullScope.Instance;

        return new OperationScopeContext(scope, logScope);
    }

    private sealed class OperationScopeContext : IDisposable
    {
        private readonly IOperationScope _operationScope;
        private readonly IDisposable _logScope;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public OperationScopeContext(IOperationScope operationScope, IDisposable logScope)
        {
            _operationScope = operationScope;
            _logScope = logScope;
        }

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

    private static void UpdateTable(STable table, TableSpec spec)
    {
        table.Name = spec.Slug;
        table.DisplayName = string.IsNullOrWhiteSpace(spec.DisplayName) ? spec.Slug : spec.DisplayName!;
        table.Description = spec.Description;
        table.IsSystem = spec.IsSystem;
        table.SupportsSoftDelete = spec.SupportsSoftDelete;
        table.HasAuditTrail = spec.HasAuditTrail;
        table.DefaultView = spec.DefaultView;
        table.RowLabelTemplate = spec.RowLabelTemplate;
    }

    private sealed record FieldRename(string OldName, string NewName, Guid FieldId);

    private sealed record FieldSyncResult(IReadOnlyList<FieldRename> Renames, IReadOnlyList<SFieldDefinition> RemovedFields);

    private static FieldSyncResult UpsertFields(STable table, IEnumerable<FieldSpec> fields)
    {
        var existingById = table.Fields.ToDictionary(f => f.Id);
        var existingByName = table.Fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var matched = new HashSet<Guid>();
        var renames = new List<FieldRename>();

        foreach (var spec in fields)
        {
            SFieldDefinition? field = null;
            if (spec.Id.HasValue && existingById.TryGetValue(spec.Id.Value, out var fieldById))
            {
                field = fieldById;
            }
            else if (existingByName.TryGetValue(spec.Slug, out var fieldByName))
            {
                field = fieldByName;
            }

            if (field is null)
            {
                field = new SFieldDefinition
                {
                    Id = spec.Id ?? Guid.NewGuid(),
                    Name = spec.Slug
                };
                table.Fields.Add(field);
            }

            if (!string.Equals(field.Name, spec.Slug, StringComparison.OrdinalIgnoreCase))
            {
                renames.Add(new FieldRename(field.Name, spec.Slug, field.Id));
            }

            field.TableId = table.Id;
            field.Name = spec.Slug;
            field.Label = spec.Label;
            field.DataType = ModuleFieldDataTypes.ToDomainType(spec.DataType);
            field.IsRequired = spec.IsRequired;
            field.IsSearchable = spec.IsSearchable;
            field.IsListVisible = spec.IsListVisible;
            field.IsPrimaryKey = spec.IsPrimaryKey;
            field.IsUnique = spec.IsUnique;
            field.IsIndexed = spec.IsIndexed;
            field.IsFilterable = spec.IsFilterable;
            field.IsSortable = spec.IsSortable;
            field.IsHidden = spec.IsHidden;
            field.IsReadOnly = spec.IsReadOnly;
            field.IsComputed = spec.IsComputed;
            field.DefaultValue = SerializeDefaultValue(spec.DefaultValue);
            field.EnumValues = spec.EnumValues is { Count: > 0 } ? string.Join(",", spec.EnumValues) : null;
            field.RelationTargetEntityTypeId = null;
            field.LookupTarget = spec.Lookup?.TargetTableSlug;
            field.LookupField = spec.Lookup?.LabelField;
            field.ComputedExpression = spec.ComputedExpression;
            field.MinLength = spec.MinLength;
            field.MaxLength = spec.MaxLength;
            field.MinValue = spec.MinValue;
            field.MaxValue = spec.MaxValue;
            field.ValidationPattern = spec.ValidationPattern;
            field.Placeholder = spec.Placeholder;
            field.Unit = spec.Unit;

            matched.Add(field.Id);
        }

        var removedFields = table.Fields.Where(f => !matched.Contains(f.Id)).ToList();
        return new FieldSyncResult(renames, removedFields);
    }

    private async Task ApplyFieldRenamesAsync(STable table, IReadOnlyList<FieldRename> renames, CancellationToken cancellationToken)
    {
        if (renames.Count == 0)
        {
            return;
        }

        var renameMap = renames
            .Where(r => !string.Equals(r.OldName, r.NewName, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(r => r.OldName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(r => r.OldName, r => r.NewName, StringComparer.OrdinalIgnoreCase);

        if (renameMap.Count == 0)
        {
            return;
        }

        var records = await _db.Records
            .Where(r => r.TableId == table.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var record in records)
        {
            if (TryRenameRecordFields(record.DataJson, renameMap, out var updatedJson))
            {
                record.DataJson = updatedJson;
                record.ModifiedAt = DateTimeOffset.UtcNow;
            }
        }

        var indexes = await _db.RecordIndexes
            .Where(r => r.TableId == table.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var index in indexes)
        {
            if (renameMap.TryGetValue(index.FieldName, out var newName))
            {
                index.FieldName = newName;
            }
        }
    }

    private async Task DeactivateRemovedFieldsAsync(STable table, IReadOnlyList<SFieldDefinition> removedFields, CancellationToken cancellationToken)
    {
        if (removedFields.Count == 0)
        {
            return;
        }

        foreach (var field in removedFields)
        {
            field.IsHidden = true;
            field.IsReadOnly = true;
            field.IsSearchable = false;
            field.IsListVisible = false;
            field.IsPrimaryKey = false;
            field.IsUnique = false;
            field.IsIndexed = false;
            field.IsFilterable = false;
            field.IsSortable = false;
            field.IsRequired = false;
        }

        var removedNames = removedFields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var indexes = await _db.RecordIndexes
            .Where(r => r.TableId == table.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var toRemove = indexes.Where(index => removedNames.Contains(index.FieldName)).ToList();
        if (toRemove.Count > 0)
        {
            _db.RecordIndexes.RemoveRange(toRemove);
        }
    }

    private static void UpsertViews(STable table, IEnumerable<ViewSpec> views)
    {
        foreach (var spec in views)
        {
            var view = table.Views.FirstOrDefault(v => string.Equals(v.Name, spec.Slug, StringComparison.OrdinalIgnoreCase));
            if (view is null)
            {
                view = new SViewDefinition
                {
                    Id = spec.Id ?? Guid.NewGuid(),
                    Name = spec.Slug
                };
                table.Views.Add(view);
            }

            view.TableId = table.Id;
            view.DisplayName = string.IsNullOrWhiteSpace(spec.DisplayName) ? spec.Slug : spec.DisplayName!;
            view.Description = spec.Description;
            view.QueryDefinition = BuildQueryDefinition(spec.Filter);
            view.FilterExpression = null;
            view.SortExpression = spec.Sort;
            view.PageSize = spec.PageSize;
            view.Visualization = string.IsNullOrWhiteSpace(spec.Visualization) ? "table" : spec.Visualization;
            view.IsDefault = spec.IsDefault;
        }
    }

    private static void EnsureDefaults(STable table, TableSpec tableSpec)
    {
        if (!table.Views.Any())
        {
            table.Views.Add(new SViewDefinition
            {
                Name = "all",
                DisplayName = table.DisplayName,
                QueryDefinition = "{}",
                Visualization = "table",
                IsDefault = true,
                TableId = table.Id
            });
        }

        if (!table.Views.Any(v => v.IsDefault))
        {
            var defaultView = !string.IsNullOrWhiteSpace(tableSpec.DefaultView)
                ? table.Views.FirstOrDefault(v => string.Equals(v.Name, tableSpec.DefaultView, StringComparison.OrdinalIgnoreCase))
                : null;

            defaultView ??= table.Views.First();
            defaultView.IsDefault = true;
        }

        if (string.IsNullOrWhiteSpace(table.DefaultView))
        {
            table.DefaultView = table.Views.FirstOrDefault(v => v.IsDefault)?.Name ?? table.Views.First().Name;
        }

        if (!string.IsNullOrWhiteSpace(tableSpec.DefaultView))
        {
            var target = table.Views.FirstOrDefault(v => string.Equals(v.Name, tableSpec.DefaultView, StringComparison.OrdinalIgnoreCase));
            if (target is not null)
            {
                foreach (var view in table.Views)
                {
                    view.IsDefault = view.Id == target.Id;
                }

                table.DefaultView = target.Name;
            }
        }

        var defaultCount = table.Views.Count(v => v.IsDefault);
        if (defaultCount > 1)
        {
            var primary = table.Views.First(v => v.IsDefault);
            foreach (var view in table.Views.Where(v => v.IsDefault && v.Id != primary.Id))
            {
                view.IsDefault = false;
            }
        }
    }

    private static string BuildQueryDefinition(Dictionary<string, string?>? filter)
        => filter is null || filter.Count == 0 ? "{}" : JsonSerializer.Serialize(filter, SerializerOptions);

    private static bool TryRenameRecordFields(string dataJson, IReadOnlyDictionary<string, string> renameMap, out string updatedJson)
    {
        updatedJson = dataJson;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(dataJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (node is not JsonObject obj)
        {
            return false;
        }

        var changed = false;
        foreach (var (oldName, newName) in renameMap)
        {
            if (!obj.TryGetPropertyValue(oldName, out var value))
            {
                continue;
            }

            if (!obj.ContainsKey(newName))
            {
                obj[newName] = value;
            }

            obj.Remove(oldName);
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        updatedJson = obj.ToJsonString(SerializerOptions);
        return true;
    }

    private static string? SerializeDefaultValue(JsonElement? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.Value.GetString(),
            _ => value.Value.GetRawText()
        };
    }
}
