using Aion.Domain;
using Aion.Infrastructure.Services;

namespace Aion.Infrastructure.Seed;

public static class PotagerSeedData
{
    public static async Task EnsureSeedAsync(IMetadataService metadataService, IDataEngine dataEngine, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadataService);
        ArgumentNullException.ThrowIfNull(dataEngine);

        var module = new S_Module
        {
            Name = "Potager",
            Description = "Gestion du potager, plantations et récoltes",
        };

        var plantEntity = new S_EntityType
        {
            Name = "Plantation",
            PluralName = "Plantations",
            Fields =
            {
                new S_Field { Name = "Name", Label = "Nom", DataType = FieldDataType.Text, IsRequired = true },
                new S_Field { Name = "Variety", Label = "Variété", DataType = FieldDataType.Text },
                new S_Field { Name = "PlantedOn", Label = "Date de plantation", DataType = FieldDataType.Date },
                new S_Field { Name = "Location", Label = "Emplacement", DataType = FieldDataType.Text },
                new S_Field { Name = "Notes", Label = "Notes", DataType = FieldDataType.Note }
            }
        };

        module.EntityTypes.Add(plantEntity);

        var harvestEntity = new S_EntityType
        {
            Name = "Harvest",
            PluralName = "Récoltes",
            Fields =
            {
                new S_Field { Name = "PlantName", Label = "Plant", DataType = FieldDataType.Lookup, LookupTarget = plantEntity.Name },
                new S_Field { Name = "Quantity", Label = "Quantité", DataType = FieldDataType.Number },
                new S_Field { Name = "Unit", Label = "Unité", DataType = FieldDataType.Text },
                new S_Field { Name = "HarvestDate", Label = "Date", DataType = FieldDataType.Date }
            }
        };

        module.EntityTypes.Add(harvestEntity);

        var harvestRelation = new S_Relation
        {
            FromField = "PlantName",
            ToEntity = plantEntity.Name,
            Kind = RelationKind.ManyToOne,
            EntityTypeId = harvestEntity.Id
        };
        harvestEntity.Relations.Add(harvestRelation);

        var report = new S_ReportDefinition
        {
            Name = "Récoltes par mois",
            ModuleId = module.Id,
            QueryDefinition = "group by HarvestDate month sum Quantity"
        };
        module.Reports.Add(report);

        var automation = new S_AutomationRule
        {
            ModuleId = module.Id,
            Name = "Rappel arrosage",
            Trigger = AutomationTriggerType.Scheduled,
            TriggerFilter = "0 19 * * *",
            Actions = { new AutomationAction { ActionType = AutomationActionType.SendNotification, ParametersJson = "{\\\"text\\\":\\\"Arroser les semis\\\"}" } }
        };
        module.AutomationRules.Add(automation);

        await metadataService.CreateModuleAsync(module, cancellationToken).ConfigureAwait(false);

        await dataEngine.InsertAsync(plantEntity.Id, "{\"Name\":\"Tomates\",\"Variety\":\"Coeur de boeuf\",\"PlantedOn\":\"2024-03-20\"}", cancellationToken).ConfigureAwait(false);
        await dataEngine.InsertAsync(plantEntity.Id, "{\"Name\":\"Courgettes\",\"Variety\":\"Verte\",\"PlantedOn\":\"2024-04-02\"}", cancellationToken).ConfigureAwait(false);
    }
}
