using Aion.Domain;

namespace Aion.AppHost.Services.Rendering;

public sealed class DynamicListRenderer : IDynamicListRenderer
{
    public ListRenderModel Render(STable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var columns = table.Fields
            .Where(field => field.IsListVisible)
            .Select(field => new ListColumnRenderModel(
                field.Id,
                field.Name,
                field.Label,
                field.DataType,
                DynamicRenderTypeMapper.ToListComponent(field.DataType),
                field.IsSortable))
            .ToList();

        return new ListRenderModel(table.Id, table.Name, columns);
    }
}
