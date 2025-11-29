using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services;

public sealed class DemoModuleSeeder
{
    private static readonly Guid ContactsModuleId = Guid.Parse("f4e8c7e2-bad5-4c96-9c0c-7f4bcb8f3411");
    private static readonly Guid ContactEntityId = Guid.Parse("9df0d7c8-65c9-4c1a-9ef3-b6d998b3b672");
    private static readonly Guid FirstNameFieldId = Guid.Parse("c3af0ad5-2cfa-4aa4-9c31-3be06f1e8a3d");
    private static readonly Guid LastNameFieldId = Guid.Parse("1bb5e7f1-26df-4c20-8f5b-1b5238a82097");
    private static readonly Guid EmailFieldId = Guid.Parse("6e59a5b9-097a-449d-9d76-9f5f91f6f2b4");
    private static readonly Guid PhoneFieldId = Guid.Parse("6cfd1d0f-52c6-4a39-97d7-4b7d69b5c1ea");
    private static readonly Guid NotesFieldId = Guid.Parse("7b1a8f57-6cf1-4efd-8b1d-6d0132d072c5");

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
        var contactsModule = modules.FirstOrDefault(m => m.Id == ContactsModuleId || m.Name.Equals("Contacts", StringComparison.OrdinalIgnoreCase));

        if (contactsModule is null)
        {
            contactsModule = BuildContactsModule();
            await _metadata.CreateModuleAsync(contactsModule, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Demo module Contacts created");
        }

        var contactEntity = contactsModule.EntityTypes.FirstOrDefault(e => e.Id == ContactEntityId)
            ?? contactsModule.EntityTypes.First();

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

        var table = new STable
        {
            Id = contactEntity.Id,
            Name = contactEntity.Name,
            DisplayName = contactEntity.PluralName,
            Description = contactEntity.Description,
            Fields = contactEntity.Fields.Select(f => new SFieldDefinition
            {
                Id = f.Id,
                TableId = contactEntity.Id,
                Name = f.Name,
                Label = f.Label,
                DataType = f.DataType,
                IsRequired = f.IsRequired,
                DefaultValue = f.DefaultValue,
                LookupTarget = f.LookupTarget
            }).ToList(),
            Views = new List<SViewDefinition>
            {
                new()
                {
                    Id = Guid.Parse("7bc2bc75-08f4-4fe1-9857-3c6fb0502ae0"),
                    TableId = contactEntity.Id,
                    Name = "Email uniquement",
                    QueryDefinition = "{ \"email\": \"\" }",
                    Visualization = "table"
                }
            }
        };

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

    private static S_Module BuildContactsModule()
    {
        var entity = new S_EntityType
        {
            Id = ContactEntityId,
            ModuleId = ContactsModuleId,
            Name = "Contact",
            PluralName = "Contacts",
            Description = "Carnet d'adresses minimal pour la démo",
            Fields =
            {
                new S_Field
                {
                    Id = FirstNameFieldId,
                    EntityTypeId = ContactEntityId,
                    Name = "firstName",
                    Label = "Prénom",
                    DataType = FieldDataType.Text,
                    IsRequired = true
                },
                new S_Field
                {
                    Id = LastNameFieldId,
                    EntityTypeId = ContactEntityId,
                    Name = "lastName",
                    Label = "Nom",
                    DataType = FieldDataType.Text,
                    IsRequired = true
                },
                new S_Field
                {
                    Id = EmailFieldId,
                    EntityTypeId = ContactEntityId,
                    Name = "email",
                    Label = "Email",
                    DataType = FieldDataType.Text,
                    IsRequired = false
                },
                new S_Field
                {
                    Id = PhoneFieldId,
                    EntityTypeId = ContactEntityId,
                    Name = "phone",
                    Label = "Téléphone",
                    DataType = FieldDataType.Text,
                    IsRequired = false
                },
                new S_Field
                {
                    Id = NotesFieldId,
                    EntityTypeId = ContactEntityId,
                    Name = "notes",
                    Label = "Notes",
                    DataType = FieldDataType.Note,
                    IsRequired = false
                }
            }
        };

        return new S_Module
        {
            Id = ContactsModuleId,
            Name = "Contacts",
            Description = "Carnet d'adresses connecté au DataEngine",
            EntityTypes = { entity }
        };
    }
}
