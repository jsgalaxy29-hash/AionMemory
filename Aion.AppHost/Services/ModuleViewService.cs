using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;

namespace Aion.AppHost.Services;

public sealed class ModuleViewService : IModuleViewService
{
    private readonly IDataEngine _dataEngine;
    private readonly IAppInitializationService _initializationService;
    private readonly ConcurrentDictionary<Guid, STable> _tableCache = new();

    public ModuleViewService(IDataEngine dataEngine, IAppInitializationService initializationService)
    {
        _dataEngine = dataEngine;
        _initializationService = initializationService;
    }

    public async Task<IReadOnlyList<STable>> GetTablesAsync(CancellationToken cancellationToken = default)
    {
        await _initializationService.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var tables = await _dataEngine.GetTablesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var table in tables)
        {
            _tableCache[table.Id] = table;
        }

        return tables.ToList();
    }

    public async Task<STable?> GetTableAsync(Guid tableId, CancellationToken cancellationToken = default)
    {
        if (_tableCache.TryGetValue(tableId, out var table))
        {
            return table;
        }

        await _initializationService.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var resolved = await _dataEngine.GetTableAsync(tableId, cancellationToken).ConfigureAwait(false);
        if (resolved is not null)
        {
            _tableCache[tableId] = resolved;
        }

        return resolved;
    }

    public async Task<STable?> GetTableByNameAsync(string tableName, CancellationToken cancellationToken = default)
    {
        await _initializationService.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var tables = await _dataEngine.GetTablesAsync(cancellationToken).ConfigureAwait(false);
        var resolved = tables.FirstOrDefault(t => string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase));
        if (resolved is not null)
        {
            _tableCache[resolved.Id] = resolved;
        }

        return resolved;
    }

    public async Task<IReadOnlyList<SViewDefinition>> GetViewsAsync(Guid tableId, CancellationToken cancellationToken = default)
    {
        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false);
        return table?.Views?.ToList() ?? Array.Empty<SViewDefinition>();
    }
}
