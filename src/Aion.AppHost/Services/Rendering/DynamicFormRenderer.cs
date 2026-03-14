using Aion.Domain;

namespace Aion.AppHost.Services.Rendering;

public sealed class DynamicFormRenderer : IDynamicFormRenderer
{
    public FormRenderModel Render(STable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var fields = table.Fields
            .Select(field => new FormFieldRenderModel(
                field.Id,
                field.Name,
                field.Label,
                field.DataType,
                DynamicRenderTypeMapper.ToFormComponent(field.DataType),
                field.IsRequired,
                field.IsReadOnly,
                ResolveOptions(field),
                ResolvePlaceholder(field.DataType)))
            .ToList();

        return new FormRenderModel(table.Id, table.Name, fields);
    }

    private static IReadOnlyList<string> ResolveOptions(SFieldDefinition field)
    {
        if (field.DataType != FieldDataType.Enum || string.IsNullOrWhiteSpace(field.EnumValues))
        {
            return Array.Empty<string>();
        }

        return field.EnumValues
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ResolvePlaceholder(FieldDataType dataType)
        => dataType == FieldDataType.File ? "Upload à venir" : null;
}
