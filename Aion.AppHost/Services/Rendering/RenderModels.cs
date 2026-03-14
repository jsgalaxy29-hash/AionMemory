using Aion.Domain;

namespace Aion.AppHost.Services.Rendering;

public sealed record FormRenderModel(
    Guid TableId,
    string TableName,
    IReadOnlyList<FormFieldRenderModel> Fields);

public sealed record FormFieldRenderModel(
    Guid FieldId,
    string FieldName,
    string Label,
    FieldDataType DataType,
    FormFieldComponentKind ComponentKind,
    bool IsRequired,
    bool IsReadOnly,
    IReadOnlyList<string> Options,
    string? Placeholder);

public sealed record ListRenderModel(
    Guid TableId,
    string TableName,
    IReadOnlyList<ListColumnRenderModel> Columns);

public sealed record ListColumnRenderModel(
    Guid FieldId,
    string FieldName,
    string Label,
    FieldDataType DataType,
    ListColumnComponentKind ComponentKind,
    bool IsSortable);

public enum FormFieldComponentKind
{
    Text,
    Number,
    Decimal,
    Checkbox,
    DatePicker,
    Select,
    FilePlaceholder,
    TextArea,
    JsonTextArea
}

public enum ListColumnComponentKind
{
    Text,
    Number,
    Decimal,
    Boolean,
    Date,
    Lookup,
    File,
    Enum,
    Note,
    Json
}
