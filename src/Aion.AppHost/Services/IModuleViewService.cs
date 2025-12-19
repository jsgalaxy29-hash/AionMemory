using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;

namespace Aion.AppHost.Services;

/// <summary>
/// Provides read-only access to module/table/view definitions for UI components.
/// </summary>
public interface IModuleViewService
{
    Task<IReadOnlyList<STable>> GetTablesAsync(CancellationToken cancellationToken = default);
    Task<STable?> GetTableAsync(Guid tableId, CancellationToken cancellationToken = default);
    Task<STable?> GetTableByNameAsync(string tableName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SViewDefinition>> GetViewsAsync(Guid tableId, CancellationToken cancellationToken = default);
}
