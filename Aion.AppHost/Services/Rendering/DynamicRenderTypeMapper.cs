using Aion.Domain;

namespace Aion.AppHost.Services.Rendering;

internal static class DynamicRenderTypeMapper
{
    public static FormFieldComponentKind ToFormComponent(FieldDataType dataType)
        => dataType switch
        {
            FieldDataType.Text => FormFieldComponentKind.Text,
            FieldDataType.Number => FormFieldComponentKind.Number,
            FieldDataType.Decimal => FormFieldComponentKind.Decimal,
            FieldDataType.Boolean => FormFieldComponentKind.Checkbox,
            FieldDataType.Date or FieldDataType.DateTime => FormFieldComponentKind.DatePicker,
            FieldDataType.Lookup or FieldDataType.Enum => FormFieldComponentKind.Select,
            FieldDataType.File => FormFieldComponentKind.FilePlaceholder,
            FieldDataType.Note => FormFieldComponentKind.TextArea,
            FieldDataType.Json => FormFieldComponentKind.JsonTextArea,
            _ => FormFieldComponentKind.Text
        };

    public static ListColumnComponentKind ToListComponent(FieldDataType dataType)
        => dataType switch
        {
            FieldDataType.Text => ListColumnComponentKind.Text,
            FieldDataType.Number => ListColumnComponentKind.Number,
            FieldDataType.Decimal => ListColumnComponentKind.Decimal,
            FieldDataType.Boolean => ListColumnComponentKind.Boolean,
            FieldDataType.Date or FieldDataType.DateTime => ListColumnComponentKind.Date,
            FieldDataType.Lookup => ListColumnComponentKind.Lookup,
            FieldDataType.File => ListColumnComponentKind.File,
            FieldDataType.Enum => ListColumnComponentKind.Enum,
            FieldDataType.Note => ListColumnComponentKind.Note,
            FieldDataType.Json => ListColumnComponentKind.Json,
            _ => ListColumnComponentKind.Text
        };
}
