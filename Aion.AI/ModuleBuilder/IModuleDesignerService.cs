using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;

namespace Aion.AI.ModuleBuilder;

public interface IModuleDesignerService
{
    string? LastGeneratedJson { get; }
    Task<S_Module> CreateModuleFromPromptAsync(string prompt, CancellationToken token = default);
    Task<S_Module> CreateModuleFromJsonAsync(string json, CancellationToken token = default);
}
