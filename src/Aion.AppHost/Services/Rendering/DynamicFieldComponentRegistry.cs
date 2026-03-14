using Aion.AppHost.Components.DynamicFields;

namespace Aion.AppHost.Services.Rendering;

public sealed class DynamicFieldComponentRegistry : IDynamicFieldComponentRegistry
{
    private static readonly IReadOnlyDictionary<FormFieldComponentKind, Type> FieldComponents =
        new Dictionary<FormFieldComponentKind, Type>
        {
            [FormFieldComponentKind.Text] = typeof(TextFieldRenderer),
            [FormFieldComponentKind.TextArea] = typeof(TextAreaFieldRenderer),
            [FormFieldComponentKind.JsonTextArea] = typeof(TextAreaFieldRenderer),
            [FormFieldComponentKind.Number] = typeof(NumberFieldRenderer),
            [FormFieldComponentKind.Decimal] = typeof(DecimalFieldRenderer),
            [FormFieldComponentKind.Checkbox] = typeof(CheckboxFieldRenderer),
            [FormFieldComponentKind.DatePicker] = typeof(DateFieldRenderer),
            [FormFieldComponentKind.Select] = typeof(SelectFieldRenderer),
            [FormFieldComponentKind.FilePlaceholder] = typeof(FilePlaceholderFieldRenderer)
        };

    public Type Resolve(FormFieldComponentKind componentKind)
        => FieldComponents.TryGetValue(componentKind, out var componentType)
            ? componentType
            : typeof(TextFieldRenderer);
}
