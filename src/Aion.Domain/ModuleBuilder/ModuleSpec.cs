using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using Aion.Domain;

namespace Aion.Domain.ModuleBuilder;

public static class ModuleSpecVersions
{
    public const string V1 = "1.0";
}

public static class ModuleFieldDataTypes
{
    public const string Text = "Text";
    public const string Number = "Number";
    public const string Decimal = "Decimal";
    public const string Boolean = "Boolean";
    public const string Date = "Date";
    public const string DateTime = "DateTime";
    public const string Enum = "Enum";
    public const string Lookup = "Lookup";
    public const string File = "File";
    public const string Note = "Note";
    public const string Json = "Json";
    public const string Tags = "Tags";

    public static readonly IReadOnlyCollection<string> All =
        new[] { Text, Number, Decimal, Boolean, Date, DateTime, Enum, Lookup, File, Note, Json, Tags };

    public static bool IsValid(string dataType)
        => !string.IsNullOrWhiteSpace(dataType) && All.Contains(dataType, StringComparer.OrdinalIgnoreCase);

    public static FieldDataType ToDomainType(string dataType)
    {
        var normalized = Normalize(dataType);
        if (normalized.Equals(Number, StringComparison.OrdinalIgnoreCase)) return FieldDataType.Number;
        if (normalized.Equals(Decimal, StringComparison.OrdinalIgnoreCase)) return FieldDataType.Decimal;
        if (normalized.Equals(Boolean, StringComparison.OrdinalIgnoreCase)) return FieldDataType.Boolean;
        if (normalized.Equals(Date, StringComparison.OrdinalIgnoreCase)) return FieldDataType.Date;
        if (normalized.Equals(DateTime, StringComparison.OrdinalIgnoreCase)) return FieldDataType.DateTime;
        if (normalized.Equals(Enum, StringComparison.OrdinalIgnoreCase)) return FieldDataType.Enum;
        if (normalized.Equals(Lookup, StringComparison.OrdinalIgnoreCase)) return FieldDataType.Lookup;
        if (normalized.Equals(File, StringComparison.OrdinalIgnoreCase)) return FieldDataType.File;
        if (normalized.Equals(Note, StringComparison.OrdinalIgnoreCase)) return FieldDataType.Note;
        if (normalized.Equals(Json, StringComparison.OrdinalIgnoreCase)) return FieldDataType.Json;
        if (normalized.Equals(Tags, StringComparison.OrdinalIgnoreCase)) return FieldDataType.Tags;
        return FieldDataType.Text;
    }

    public static bool IsNumeric(string dataType)
    {
        var normalized = Normalize(dataType);
        return normalized.Equals(Number, StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals(Decimal, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTextual(string dataType)
    {
        var normalized = Normalize(dataType);
        return normalized.Equals(Text, StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(Json, StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(Note, StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(Tags, StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(File, StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(Enum, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string dataType)
        => dataType?.Trim() ?? string.Empty;
}

public sealed class ModuleSpec
{
    [Required, StringLength(8)]
    public string Version { get; set; } = ModuleSpecVersions.V1;

    public Guid? ModuleId { get; set; }

    [Required, StringLength(128)]
    public string Slug { get; set; } = string.Empty;

    [StringLength(128)]
    public string? DisplayName { get; set; }

    [StringLength(1024)]
    public string? Description { get; set; }

    public List<TableSpec> Tables { get; set; } = new();
}

public sealed class TableSpec
{
    public Guid? Id { get; set; }

    [Required, StringLength(128)]
    public string Slug { get; set; } = string.Empty;

    [StringLength(128)]
    public string? DisplayName { get; set; }

    [StringLength(1024)]
    public string? Description { get; set; }

    public bool IsSystem { get; set; }

    public bool SupportsSoftDelete { get; set; }

    public bool HasAuditTrail { get; set; }

    [StringLength(128)]
    public string? DefaultView { get; set; }

    [StringLength(256)]
    public string? RowLabelTemplate { get; set; }

    public List<FieldSpec> Fields { get; set; } = new();

    public List<ViewSpec> Views { get; set; } = new();
}

public sealed class FieldSpec
{
    public Guid? Id { get; set; }

    [Required, StringLength(128)]
    public string Slug { get; set; } = string.Empty;

    [Required, StringLength(128)]
    public string Label { get; set; } = string.Empty;

    [StringLength(64)]
    public string DataType { get; set; } = ModuleFieldDataTypes.Text;

    public bool IsRequired { get; set; }

    public bool IsSearchable { get; set; }

    public bool IsListVisible { get; set; }

    public bool IsPrimaryKey { get; set; }

    public bool IsUnique { get; set; }

    public bool IsIndexed { get; set; }

    public bool IsFilterable { get; set; }

    public bool IsSortable { get; set; }

    public bool IsHidden { get; set; }

    public bool IsReadOnly { get; set; }

    public bool IsComputed { get; set; }

    public JsonElement? DefaultValue { get; set; }

    public List<string>? EnumValues { get; set; }

    public LookupSpec? Lookup { get; set; }

    [StringLength(2048)]
    public string? ComputedExpression { get; set; }

    public int? MinLength { get; set; }

    public int? MaxLength { get; set; }

    public decimal? MinValue { get; set; }

    public decimal? MaxValue { get; set; }

    [StringLength(512)]
    public string? ValidationPattern { get; set; }

    [StringLength(256)]
    public string? Placeholder { get; set; }

    [StringLength(128)]
    public string? Unit { get; set; }
}

public sealed class LookupSpec
{
    [Required, StringLength(128)]
    public string TargetTableSlug { get; set; } = string.Empty;

    [StringLength(128)]
    public string? LabelField { get; set; }
}

public sealed class ViewSpec
{
    public Guid? Id { get; set; }

    [Required, StringLength(128)]
    public string Slug { get; set; } = string.Empty;

    [StringLength(128)]
    public string? DisplayName { get; set; }

    [StringLength(1024)]
    public string? Description { get; set; }

    public Dictionary<string, string?>? Filter { get; set; }

    [StringLength(128)]
    public string? Sort { get; set; }

    [Range(1, 500)]
    public int? PageSize { get; set; }

    [StringLength(128)]
    public string? Visualization { get; set; }

    public bool IsDefault { get; set; }
}
