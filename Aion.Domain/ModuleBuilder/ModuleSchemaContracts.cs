using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;

namespace Aion.Domain.ModuleBuilder;

public interface IModuleSchemaService
{
    Task<STable> CreateModuleAsync(ModuleSpec spec, CancellationToken cancellationToken = default);
}
