using Aion.Domain;

namespace Aion.AppHost.Services;

public interface ITableDefinitionService
{
    Task<STable?> GetTableAsync(Guid entityTypeId, CancellationToken cancellationToken = default);
    Task<STable> EnsureTableAsync(S_EntityType entityType, CancellationToken cancellationToken = default);
}
