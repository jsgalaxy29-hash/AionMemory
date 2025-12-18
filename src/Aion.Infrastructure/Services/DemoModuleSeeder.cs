using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Domain.SeedData;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services;

public sealed class DemoModuleSeeder
{
    private readonly IMetadataService _metadata;
    private readonly IDataEngine _dataEngine;
    private readonly ILogger<DemoModuleSeeder> _logger;

    public DemoModuleSeeder(IMetadataService metadata, IDataEngine dataEngine, ILogger<DemoModuleSeeder> logger)
    {
        _metadata = metadata;
        _dataEngine = dataEngine;
        _logger = logger;
    }

    public async Task EnsureDemoDataAsync(CancellationToken cancellationToken = default)
    {
        var modules = await _metadata.GetModulesAsync(cancellationToken).ConfigureAwait(false);
        var contactsModule = modules.FirstOrDefault(m => m.Id == ContactsModuleDefinition.ModuleId ||
            m.Name.Equals("Contacts", StringComparison.OrdinalIgnoreCase));

        if (contactsModule is null)
        {
            contactsModule = ContactsModuleDefinition.CreateModule();
            await _metadata.CreateModuleAsync(contactsModule, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Demo module Contacts created");
        }

        var contactEntity = contactsModule.EntityTypes.FirstOrDefault(e => e.Id == ContactsModuleDefinition.EntityId);
        if (contactEntity is null)
        {
            contactEntity = ContactsModuleDefinition.CreateEntityType();
            await _metadata.AddEntityTypeAsync(contactsModule.Id, contactEntity, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Demo Contacts entity attached to module");
        }

        await EnsureContactTableAsync(contactEntity, cancellationToken).ConfigureAwait(false);
        await EnsureSampleRecordAsync(contactEntity.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureContactTableAsync(S_EntityType contactEntity, CancellationToken cancellationToken)
    {
        var existing = await _dataEngine.GetTableAsync(contactEntity.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return;
        }

        var table = ContactsModuleDefinition.CreateTable();
        await _dataEngine.CreateTableAsync(table, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Demo Contacts table created");
    }

    private async Task EnsureSampleRecordAsync(Guid entityId, CancellationToken cancellationToken)
    {
        var existing = await _dataEngine.QueryAsync(entityId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (existing.Any())
        {
            return;
        }

        var payload = new Dictionary<string, object?>
        {
            ["firstName"] = "Aïcha",
            ["lastName"] = "Dupont",
            ["email"] = "aicha.dupont@example.com",
            ["phone"] = "+33 6 12 34 56 78",
            ["notes"] = "Rencontrée lors du salon VivaTech."
        };

        await _dataEngine.InsertAsync(entityId, System.Text.Json.JsonSerializer.Serialize(payload), cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation("Demo Contacts sample record inserted");
    }
}
