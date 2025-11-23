using Aion.Domain;
using Aion.AI;

namespace Aion.Infrastructure.Services;

public class ModuleDesignerService
{
    private readonly IModuleDesigner _designer;
    private readonly IMetadataService _metadata;

    public ModuleDesignerService(IModuleDesigner designer, IMetadataService metadata)
    {
        _designer = designer;
        _metadata = metadata;
    }

    public string? LastGeneratedJson { get; private set; }

    public async Task<S_Module> CreateModuleFromPromptAsync(string prompt, CancellationToken token = default)
    {
        var module = await _designer.GenerateModuleFromPromptAsync(prompt, token).ConfigureAwait(false);
        LastGeneratedJson = _designer.LastGeneratedJson;

        await _metadata.CreateModuleAsync(module, token).ConfigureAwait(false);

        return module;
    }
}
