using Aion.AppHost.Components.DynamicListCells;

namespace Aion.AppHost.Services.Rendering;

public sealed class DynamicListCellComponentRegistry : IDynamicListCellComponentRegistry
{
    private static readonly IReadOnlyDictionary<ListColumnComponentKind, Type> CellComponents =
        new Dictionary<ListColumnComponentKind, Type>
        {
            [ListColumnComponentKind.Text] = typeof(TextListCellRenderer),
            [ListColumnComponentKind.Number] = typeof(NumberListCellRenderer),
            [ListColumnComponentKind.Decimal] = typeof(DecimalListCellRenderer),
            [ListColumnComponentKind.Boolean] = typeof(BooleanListCellRenderer),
            [ListColumnComponentKind.Date] = typeof(DateListCellRenderer),
            [ListColumnComponentKind.Enum] = typeof(SelectListCellRenderer),
            [ListColumnComponentKind.Lookup] = typeof(SelectListCellRenderer),
            [ListColumnComponentKind.Json] = typeof(JsonListCellRenderer),
            [ListColumnComponentKind.Note] = typeof(TextListCellRenderer),
            [ListColumnComponentKind.File] = typeof(FileListCellRenderer)
        };

    public Type Resolve(ListColumnComponentKind componentKind)
        => CellComponents.TryGetValue(componentKind, out var componentType)
            ? componentType
            : typeof(TextListCellRenderer);
}
