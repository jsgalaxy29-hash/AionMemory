using Aion.Domain;

namespace Aion.AppHost.Services.Rendering;

public interface IDynamicListRenderer
{
    ListRenderModel Render(STable table);
}
