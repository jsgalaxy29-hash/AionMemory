using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Microsoft.Extensions.Logging;

namespace Aion.AppHost.Services;

public sealed class TableDefinitionService : ITableDefinitionService
{
    private readonly IDataEngine _dataEngine;
    private readonly ILogger<TableDefinitionService> _logger;
    private readonly IAppInitializationService _initialization;

    public TableDefinitionService(IDataEngine dataEngine, ILogger<TableDefinitionService> logger, IAppInitializationService initialization)
    {
        _dataEngine = dataEngine;
        _logger = logger;
        _initialization = initialization;
    }

    public Task<STable?> GetTableAsync(Guid entityTypeId, CancellationToken cancellationToken = default)
        => GetTableInternalAsync(entityTypeId, cancellationToken);

    public async Task<STable> EnsureTableAsync(S_EntityType entityType, CancellationToken cancellationToken = default)
    {
        await _initialization.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        var existing = await _dataEngine.GetTableAsync(entityType.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var table = new STable
        {
            Id = entityType.Id,
            Name = entityType.Name,
            DisplayName = entityType.PluralName,
            Description = entityType.Description ?? entityType.Name,
            Fields = entityType.Fields
                .Select(f => new SFieldDefinition
                {
                    Id = f.Id,
                    TableId = entityType.Id,
                    Name = f.Name,
                    Label = f.Label,
                    DataType = f.DataType,
                    IsRequired = f.IsRequired,
                    IsSearchable = f.IsSearchable,
                    IsListVisible = f.IsListVisible,
                    DefaultValue = f.DefaultValue,
                    EnumValues = f.EnumValues,
                    RelationTargetEntityTypeId = f.RelationTargetEntityTypeId
                })
                .ToList(),
            Views = new List<SViewDefinition>()
        };

        var created = await _dataEngine.CreateTableAsync(table, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Table {Table} created from entity {EntityId}", table.Name, entityType.Id);
        return created;
    }

    private async Task<STable?> GetTableInternalAsync(Guid entityTypeId, CancellationToken cancellationToken)
    {
        await _initialization.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await _dataEngine.GetTableAsync(entityTypeId, cancellationToken).ConfigureAwait(false);
    }
}
