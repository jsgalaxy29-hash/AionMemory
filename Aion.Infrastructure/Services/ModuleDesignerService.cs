using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;

namespace Aion.Infrastructure.Services;

public class ModuleDesignerService
{
    private readonly IModuleDesigner _designer;
    private readonly IMetadataService _metadata;
    private readonly IDataEngine _dataEngine;

    public ModuleDesignerService(IModuleDesigner designer, IMetadataService metadata, IDataEngine dataEngine)
    {
        _designer = designer;
        _metadata = metadata;
        _dataEngine = dataEngine;
    }

    public string? LastGeneratedJson { get; private set; }

    public async Task<S_Module> CreateModuleFromPromptAsync(string prompt, CancellationToken token = default)
    {
        var module = await _designer.GenerateModuleFromPromptAsync(prompt, token).ConfigureAwait(false);
        LastGeneratedJson = _designer.LastGeneratedJson;

        await _metadata.CreateModuleAsync(module, token).ConfigureAwait(false);
        await EnsureTablesAsync(module, token).ConfigureAwait(false);

        return module;
    }

    private async Task EnsureTablesAsync(S_Module module, CancellationToken token)
    {
        foreach (var entity in module.EntityTypes)
        {
            var existing = await _dataEngine.GetTableAsync(entity.Id, token).ConfigureAwait(false);
            if (existing is not null)
            {
                continue;
            }

            var table = new STable
            {
                Id = entity.Id,
                Name = entity.Name,
                DisplayName = entity.PluralName,
                Description = entity.Description,
                Fields = entity.Fields.Select(f => new SFieldDefinition
                {
                    Id = f.Id,
                    TableId = entity.Id,
                    Name = f.Name,
                    Label = f.Label,
                    DataType = f.DataType,
                    IsRequired = f.IsRequired,
                    DefaultValue = f.DefaultValue,
                    LookupTarget = f.LookupTarget
                }).ToList(),
                Views = new List<SViewDefinition>()
            };

            await _dataEngine.CreateTableAsync(table, token).ConfigureAwait(false);
        }
    }
}
