using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

    public ModuleApplier(AionDbContext db, IModuleValidator validator, ILogger<ModuleApplier> logger)
    {
        _db = db;
        _validator = validator;
        _logger = logger;
    }

    public async Task<IReadOnlyList<STable>> ApplyAsync(ModuleSpec spec, CancellationToken cancellationToken = default)
    {
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
            UpsertFields(table, tableSpec.Fields);
            UpsertViews(table, tableSpec.Views);
            EnsureDefaults(table, tableSpec);

            applied.Add(table);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Applied module spec {Slug} with {TableCount} table(s).", spec.Slug, applied.Count);
        return applied;
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

    private static void UpsertFields(STable table, IEnumerable<FieldSpec> fields)
    {
        foreach (var spec in fields)
        {
            var field = table.Fields.FirstOrDefault(f => string.Equals(f.Name, spec.Slug, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                field = new SFieldDefinition
                {
                    Id = spec.Id ?? Guid.NewGuid(),
                    Name = spec.Slug
                };
                table.Fields.Add(field);
            }

            field.TableId = table.Id;
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
