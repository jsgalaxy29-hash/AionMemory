using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.ModuleBuilder;

public sealed class ModuleValidator : IModuleValidator
{
    private sealed record ExistingField(Guid Id, string Name);
    private sealed record ExistingTable(Guid Id, string Name, IReadOnlyList<ExistingField> Fields);

    private readonly AionDbContext _db;
    private readonly ILogger<ModuleValidator> _logger;

    public ModuleValidator(AionDbContext db, ILogger<ModuleValidator> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ModuleValidationResult> ValidateAsync(ModuleSpec spec, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        ValidateAnnotations(spec, "Module", errors);
        if (!string.Equals(spec.Version, ModuleSpecVersions.V1, System.StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Unsupported ModuleSpec version '{spec.Version}'. Expected {ModuleSpecVersions.V1}.");
        }

        if (string.IsNullOrWhiteSpace(spec.Slug))
        {
            errors.Add("Module slug is required.");
        }

        if (spec.Tables.Count == 0)
        {
            errors.Add("At least one table must be defined.");
        }

        var existingTables = await _db.Tables.AsNoTracking()
            .Include(t => t.Fields)
            .Select(t => new ExistingTable(
                t.Id,
                t.Name,
                t.Fields.Select(f => new ExistingField(f.Id, f.Name)).ToList()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var tableSlugs = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var table in spec.Tables)
        {
            ValidateAnnotations(table, $"Table '{table.Slug}'", errors);
            if (table.Fields.Count == 0)
            {
                errors.Add($"Table '{table.Slug}' must declare at least one field.");
            }

            if (!tableSlugs.Add(table.Slug))
            {
                errors.Add($"Table slug '{table.Slug}' is duplicated.");
            }

            if (existingTables.Any(t => string.Equals(t.Name, table.Slug, System.StringComparison.OrdinalIgnoreCase)
                                        && (!table.Id.HasValue || t.Id != table.Id.Value)))
            {
                errors.Add($"Table slug '{table.Slug}' already exists in the database.");
            }

            ValidateTable(table, spec, existingTables, errors);
            WarnOnFieldChanges(table, existingTables);
        }

        return errors.Count == 0 ? ModuleValidationResult.Success() : ModuleValidationResult.Failure(errors);
    }

    public async Task ValidateAndThrowAsync(ModuleSpec spec, CancellationToken cancellationToken = default)
    {
        var result = await ValidateAsync(spec, cancellationToken).ConfigureAwait(false);
        if (!result.IsValid)
        {
            throw new ModuleValidationException(result.Errors);
        }
    }

    private static void ValidateTable(TableSpec table, ModuleSpec spec, IReadOnlyCollection<ExistingTable> existingTables, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(table.Slug))
        {
            errors.Add("Table slug is required.");
        }

        var fieldSlugs = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var field in table.Fields)
        {
            if (!fieldSlugs.Add(field.Slug))
            {
                errors.Add($"Field slug '{field.Slug}' is duplicated in table '{table.Slug}'.");
            }

            ValidateField(field, table, spec, existingTables, errors);
        }

        var viewSlugs = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var view in table.Views)
        {
            if (!viewSlugs.Add(view.Slug))
            {
                errors.Add($"View slug '{view.Slug}' is duplicated in table '{table.Slug}'.");
            }

            ValidateView(view, table, fieldSlugs, errors);
        }

        var defaultCount = table.Views.Count(v => v.IsDefault);
        if (defaultCount > 1)
        {
            errors.Add($"Table '{table.Slug}' cannot declare multiple default views.");
        }

        if (!string.IsNullOrWhiteSpace(table.DefaultView) &&
            !table.Views.Any(v => string.Equals(v.Slug, table.DefaultView, System.StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"Default view '{table.DefaultView}' does not match any view for table '{table.Slug}'.");
        }
    }

    private void WarnOnFieldChanges(TableSpec table, IReadOnlyCollection<ExistingTable> existingTables)
    {
        var existingTable = FindExistingTable(table, existingTables);
        if (existingTable is null)
        {
            return;
        }

        var specFieldsById = table.Fields.Where(f => f.Id.HasValue)
            .ToDictionary(f => f.Id!.Value, f => f);
        var specFieldsBySlug = table.Fields
            .ToDictionary(f => f.Slug, f => f, System.StringComparer.OrdinalIgnoreCase);

        foreach (var existingField in existingTable.Fields)
        {
            var existsById = specFieldsById.ContainsKey(existingField.Id);
            var existsBySlug = specFieldsBySlug.ContainsKey(existingField.Name);
            if (!existsById && !existsBySlug)
            {
                _logger.LogWarning(
                    "ModuleSpec for table {TableSlug} removes field {FieldName} ({FieldId}). Data will be preserved but the field will be marked inactive.",
                    table.Slug,
                    existingField.Name,
                    existingField.Id);
            }
        }

        foreach (var (fieldId, specField) in specFieldsById)
        {
            var existingField = existingTable.Fields.FirstOrDefault(f => f.Id == fieldId);
            if (existingField is null)
            {
                continue;
            }

            if (!string.Equals(existingField.Name, specField.Slug, System.StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "ModuleSpec for table {TableSlug} renames field {OldName} ({FieldId}) to {NewName}. Existing data will be migrated.",
                    table.Slug,
                    existingField.Name,
                    existingField.Id,
                    specField.Slug);
            }
        }
    }

    private static ExistingTable? FindExistingTable(TableSpec table, IReadOnlyCollection<ExistingTable> existingTables)
    {
        if (table.Id.HasValue)
        {
            return existingTables.FirstOrDefault(t => t.Id == table.Id.Value);
        }

        return existingTables.FirstOrDefault(t => string.Equals(t.Name, table.Slug, System.StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateField(FieldSpec field, TableSpec table, ModuleSpec spec, IReadOnlyCollection<ExistingTable> existingTables, ICollection<string> errors)
    {
        ValidateAnnotations(field, $"Field '{field.Slug}' in table '{table.Slug}'", errors);
        if (string.IsNullOrWhiteSpace(field.Slug))
        {
            errors.Add($"Field slug is required in table '{table.Slug}'.");
        }

        if (!ModuleFieldDataTypes.IsValid(field.DataType))
        {
            errors.Add($"Invalid dataType '{field.DataType}' for field '{field.Slug}' in table '{table.Slug}'.");
            return;
        }

        if (!ModuleFieldDataTypes.IsTextual(field.DataType) &&
            (field.MinLength.HasValue || field.MaxLength.HasValue || !string.IsNullOrWhiteSpace(field.ValidationPattern)))
        {
            errors.Add($"Field '{field.Slug}' in table '{table.Slug}' uses text constraints on a non-text data type.");
        }

        if (!ModuleFieldDataTypes.IsNumeric(field.DataType) &&
            (field.MinValue.HasValue || field.MaxValue.HasValue))
        {
            errors.Add($"Field '{field.Slug}' in table '{table.Slug}' uses numeric constraints on a non-numeric data type.");
        }

        if (field.IsPrimaryKey && !field.IsUnique)
        {
            errors.Add($"Primary key field '{field.Slug}' in table '{table.Slug}' must be unique.");
        }

        if (field.MinLength.HasValue && field.MaxLength.HasValue && field.MinLength > field.MaxLength)
        {
            errors.Add($"Field '{field.Slug}' in table '{table.Slug}' has invalid length range (min > max).");
        }

        if (field.MinValue.HasValue && field.MaxValue.HasValue && field.MinValue > field.MaxValue)
        {
            errors.Add($"Field '{field.Slug}' in table '{table.Slug}' has invalid numeric range (min > max).");
        }

        if (!string.IsNullOrWhiteSpace(field.ValidationPattern))
        {
            try
            {
                _ = new Regex(field.ValidationPattern);
            }
            catch
            {
                errors.Add($"Field '{field.Slug}' in table '{table.Slug}' has an invalid validation pattern.");
            }
        }

        ValidateFieldValueConstraints(field, table, errors);
        ValidateLookup(field, table, spec, existingTables, errors);
    }

    private static void ValidateFieldValueConstraints(FieldSpec field, TableSpec table, ICollection<string> errors)
    {
        var normalizedType = ModuleFieldDataTypes.ToDomainType(field.DataType);
        if (field.EnumValues is { Count: > 0 } enumValues)
        {
            var duplicates = enumValues.GroupBy(v => v, System.StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicates is not null)
            {
                errors.Add($"Enum values for field '{field.Slug}' in table '{table.Slug}' contain duplicates ('{duplicates.Key}').");
            }
        }

        if (normalizedType == FieldDataType.Enum && (field.EnumValues is null || field.EnumValues.Count == 0))
        {
            errors.Add($"Field '{field.Slug}' in table '{table.Slug}' is Enum but no enumValues provided.");
        }

        if (normalizedType != FieldDataType.Enum && field.EnumValues is { Count: > 0 })
        {
            errors.Add($"Field '{field.Slug}' in table '{table.Slug}' has enumValues but is not an Enum.");
        }

        if (!IsDefaultCompatible(field, normalizedType, table.Slug, errors))
        {
            return;
        }

        if (field.DefaultValue.HasValue)
        {
            ValidateDefaultAgainstBounds(field, normalizedType, table.Slug, errors);
        }
    }

    private static bool IsDefaultCompatible(FieldSpec field, FieldDataType dataType, string tableSlug, ICollection<string> errors)
    {
        if (!field.DefaultValue.HasValue)
        {
            return true;
        }

        var value = field.DefaultValue.Value;
        switch (dataType)
        {
            case FieldDataType.Text or FieldDataType.Json or FieldDataType.Note or FieldDataType.Tags or FieldDataType.File:
                if (value.ValueKind is not JsonValueKind.String and not JsonValueKind.Null)
                {
                    errors.Add($"Default value for field '{field.Slug}' in table '{tableSlug}' must be a string.");
                    return false;
                }
                break;
            case FieldDataType.Number or FieldDataType.Decimal:
                if (value.ValueKind != JsonValueKind.Number)
                {
                    errors.Add($"Default value for field '{field.Slug}' in table '{tableSlug}' must be numeric.");
                    return false;
                }
                break;
            case FieldDataType.Boolean:
                if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                {
                    errors.Add($"Default value for field '{field.Slug}' in table '{tableSlug}' must be boolean.");
                    return false;
                }
                break;
            case FieldDataType.Date or FieldDataType.DateTime:
                if (value.ValueKind != JsonValueKind.String || !System.DateTimeOffset.TryParse(value.GetString(), out _))
                {
                    errors.Add($"Default value for field '{field.Slug}' in table '{tableSlug}' must be an ISO date string.");
                    return false;
                }
                break;
            case FieldDataType.Enum:
                if (value.ValueKind != JsonValueKind.String)
                {
                    errors.Add($"Default value for enum field '{field.Slug}' in table '{tableSlug}' must be a string.");
                    return false;
                }
                break;
            case FieldDataType.Lookup:
                if (value.ValueKind is not JsonValueKind.String and not JsonValueKind.Null)
                {
                    errors.Add($"Default value for lookup field '{field.Slug}' in table '{tableSlug}' must be a string or null.");
                    return false;
                }
                break;
            default:
                errors.Add($"Unsupported data type {dataType} for field '{field.Slug}' in table '{tableSlug}'.");
                return false;
        }

        return true;
    }

    private static void ValidateDefaultAgainstBounds(FieldSpec field, FieldDataType dataType, string tableSlug, ICollection<string> errors)
    {
        var value = field.DefaultValue!.Value;
        if (dataType is FieldDataType.Text or FieldDataType.Json or FieldDataType.Note or FieldDataType.Tags or FieldDataType.File)
        {
            var str = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
            if (field.MinLength.HasValue && str.Length < field.MinLength.Value)
            {
                errors.Add($"Default value for field '{field.Slug}' in table '{tableSlug}' is shorter than minLength.");
            }

            if (field.MaxLength.HasValue && str.Length > field.MaxLength.Value)
            {
                errors.Add($"Default value for field '{field.Slug}' in table '{tableSlug}' exceeds maxLength.");
            }

            if (!string.IsNullOrWhiteSpace(field.ValidationPattern) && !Regex.IsMatch(str, field.ValidationPattern))
            {
                errors.Add($"Default value for field '{field.Slug}' in table '{tableSlug}' does not match validationPattern.");
            }

            if (dataType == FieldDataType.Enum && field.EnumValues is { Count: > 0 } && !field.EnumValues.Contains(str, System.StringComparer.OrdinalIgnoreCase))
            {
                errors.Add($"Default value for field '{field.Slug}' in table '{tableSlug}' is not part of enumValues.");
            }
        }

        if (ModuleFieldDataTypes.IsNumeric(field.DataType))
        {
            if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetDecimal(out var number))
                {
                    if (field.MinValue.HasValue && number < field.MinValue.Value)
                    {
                        errors.Add($"Default value for field '{field.Slug}' in table '{tableSlug}' is below minValue.");
                    }

                    if (field.MaxValue.HasValue && number > field.MaxValue.Value)
                    {
                        errors.Add($"Default value for field '{field.Slug}' in table '{tableSlug}' exceeds maxValue.");
                    }
                }
            }
        }
    }

    private static void ValidateLookup(FieldSpec field, TableSpec table, ModuleSpec spec, IReadOnlyCollection<ExistingTable> existingTables, ICollection<string> errors)
    {
        var normalizedType = ModuleFieldDataTypes.ToDomainType(field.DataType);
        if (normalizedType != FieldDataType.Lookup)
        {
            if (field.Lookup is not null)
            {
                errors.Add($"Field '{field.Slug}' in table '{table.Slug}' declares a lookup but is not of type Lookup.");
            }
            return;
        }

        if (field.Lookup is null)
        {
            errors.Add($"Lookup field '{field.Slug}' in table '{table.Slug}' must specify lookup settings.");
            return;
        }

        var target = field.Lookup.TargetTableSlug;
        var existsInSpec = spec.Tables.Any(t => string.Equals(t.Slug, target, System.StringComparison.OrdinalIgnoreCase));
        var existsInDb = existingTables.Any(t => string.Equals(t.Name, target, System.StringComparison.OrdinalIgnoreCase));

        if (spec.ModuleId is null && !existsInSpec)
        {
            errors.Add($"Lookup field '{field.Slug}' in table '{table.Slug}' targets '{target}' which is not defined in the same spec.");
        }

        if (spec.ModuleId is not null && !existsInSpec && !existsInDb)
        {
            errors.Add($"Lookup field '{field.Slug}' in table '{table.Slug}' targets '{target}' which does not exist.");
        }
    }

    private static void ValidateView(ViewSpec view, TableSpec table, IReadOnlySet<string> fieldSlugs, ICollection<string> errors)
    {
        ValidateAnnotations(view, $"View '{view.Slug}' in table '{table.Slug}'", errors);
        if (string.IsNullOrWhiteSpace(view.Slug))
        {
            errors.Add($"View slug is required for table '{table.Slug}'.");
        }

        if (view.Filter is not null)
        {
            foreach (var filter in view.Filter.Keys)
            {
                if (!fieldSlugs.Contains(filter))
                {
                    errors.Add($"View '{view.Slug}' in table '{table.Slug}' references unknown field '{filter}' in filter.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(view.Sort))
        {
            var sortField = view.Sort.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(sortField) && !fieldSlugs.Contains(sortField))
            {
                errors.Add($"View '{view.Slug}' in table '{table.Slug}' references unknown field '{sortField}' in sort.");
            }
        }

    }

    private static void ValidateAnnotations(object instance, string context, ICollection<string> errors)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true))
        {
            return;
        }

        foreach (var result in results)
        {
            var message = result.ErrorMessage ?? "Validation failed.";
            errors.Add(string.IsNullOrWhiteSpace(context) ? message : $"{context}: {message}");
        }
    }
}
