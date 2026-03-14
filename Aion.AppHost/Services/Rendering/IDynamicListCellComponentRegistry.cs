using Microsoft.AspNetCore.Components;

namespace Aion.AppHost.Services.Rendering;

public interface IDynamicListCellComponentRegistry
{
    Type Resolve(ListColumnComponentKind componentKind);
}
