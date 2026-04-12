using System;
using System.Linq;
using System.Text.Json;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;

namespace Aion.Infrastructure.ModuleBuilder;

internal static class FieldSpecMapper
{
    public static SFieldDefinition Map(FieldSpec fieldSpec, int order)
    {
        ArgumentNullException.ThrowIfNull(fieldSpec);

        return new SFieldDefinition
        {
            Id = fieldSpec.Id ?? Guid.NewGuid(),
            Name = fieldSpec.Slug,
            Label = fieldSpec.Label,
            DataType = ModuleFieldDataTypes.ToDomainType(fieldSpec.DataType),
            IsRequired = fieldSpec.IsRequired,
            IsSearchable = fieldSpec.IsSearchable,
            IsListVisible = fieldSpec.IsListVisible,
            IsPrimaryKey = fieldSpec.IsPrimaryKey,
            IsUnique = fieldSpec.IsUnique,
            IsIndexed = fieldSpec.IsIndexed,
            IsFilterable = fieldSpec.IsFilterable,
            IsSortable = fieldSpec.IsSortable,
            IsHidden = fieldSpec.IsHidden,
            IsReadOnly = fieldSpec.IsReadOnly,
            IsComputed = fieldSpec.IsComputed,
            DefaultValue = ToDefaultValue(fieldSpec.DefaultValue),
            EnumValues = fieldSpec.EnumValues is { Count: > 0 } ? string.Join("|", fieldSpec.EnumValues) : null,
            LookupTarget = fieldSpec.Lookup?.TargetTableSlug,
            LookupField = fieldSpec.Lookup?.LabelField,
            ComputedExpression = fieldSpec.ComputedExpression,
            MinLength = fieldSpec.MinLength,
            MaxLength = fieldSpec.MaxLength,
            MinValue = fieldSpec.MinValue,
            MaxValue = fieldSpec.MaxValue,
            ValidationPattern = fieldSpec.ValidationPattern,
            Placeholder = fieldSpec.Placeholder,
            Unit = fieldSpec.Unit,
            Order = order
        };
    }

    private static string? ToDefaultValue(JsonElement? defaultValue)
    {
        if (!defaultValue.HasValue)
        {
            return null;
        }

        return defaultValue.Value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => defaultValue.Value.GetString(),
            _ => defaultValue.Value.GetRawText()
        };
    }
}
