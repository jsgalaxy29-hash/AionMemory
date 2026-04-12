using Aion.Domain;

namespace Aion.AppHost.Services.Rendering;

public interface IDynamicFormRenderer
{
    FormRenderModel Render(STable table);
}
