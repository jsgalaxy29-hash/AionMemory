using Microsoft.AspNetCore.Components;

namespace Aion.AppHost.Services.Rendering;

public interface IDynamicFieldComponentRegistry
{
    Type Resolve(FormFieldComponentKind componentKind);
}
