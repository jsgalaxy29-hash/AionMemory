using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;

namespace Aion.AI.ModuleBuilder;

public class ModuleDesignerService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

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
        var design = await _designer.GenerateModuleAsync(new ModuleDesignRequest { Prompt = prompt }, token).ConfigureAwait(false);
        var module = design.Module;
        LastGeneratedJson = design.RawDesignJson;

        await _metadata.CreateModuleAsync(module, token).ConfigureAwait(false);
        await EnsureTablesAsync(module, token).ConfigureAwait(false);

        return module;
    }

    public async Task<S_Module> CreateModuleFromJsonAsync(string json, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Le JSON du module est vide.");
        }

        var module = ParseModuleFromJson(json);
        LastGeneratedJson = json;

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
                    IsSearchable = f.IsSearchable,
                    IsListVisible = f.IsListVisible,
                    DefaultValue = f.DefaultValue,
                    EnumValues = f.EnumValues,
                    RelationTargetEntityTypeId = f.RelationTargetEntityTypeId
                }).ToList(),
                Views = new List<SViewDefinition>()
            };

            await _dataEngine.CreateTableAsync(table, token).ConfigureAwait(false);
        }
    }

    private static S_Module ParseModuleFromJson(string json)
    {
        ModuleDesignSchema? design;
        try
        {
            design = JsonSerializer.Deserialize<ModuleDesignSchema>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"JSON invalide : {ex.Message}", ex);
        }

        if (design is null)
        {
            throw new InvalidOperationException("Le JSON ne contient pas de module valide.");
        }

        var moduleName = NormalizeName(design.Module?.Name) ?? "Module manuel";
        var module = new S_Module
        {
            Name = moduleName,
            Description = design.Module?.Description ?? design.Module?.PluralName ?? "Module importé",
            EntityTypes = new List<S_EntityType>()
        };

        foreach (var entity in design.Entities ?? Enumerable.Empty<DesignEntity>())
        {
            var entityName = NormalizeName(entity.Name) ?? "Entité";
            var pluralName = NormalizeName(entity.PluralName) ?? EnsurePlural(entityName);
            var entityType = new S_EntityType
            {
                ModuleId = module.Id,
                Name = entityName,
                PluralName = pluralName,
                Icon = entity.Icon,
                Fields = new List<S_Field>(),
                Relations = new List<S_Relation>()
            };

            var fields = entity.Fields?.Count > 0 ? BuildFields(module, entityType, entity.Fields) : BuildDefaultFields(entityType);
            foreach (var field in fields)
            {
                entityType.Fields.Add(field);
            }

            module.EntityTypes.Add(entityType);
        }

        if (!module.EntityTypes.Any())
        {
            var fallbackEntity = new S_EntityType
            {
                ModuleId = module.Id,
                Name = "Item",
                PluralName = "Items",
                Fields = BuildDefaultFields(null)
            };

            foreach (var field in fallbackEntity.Fields)
            {
                field.EntityTypeId = fallbackEntity.Id;
            }

            module.EntityTypes.Add(fallbackEntity);
        }

        AppendRelations(module, design.Relations);

        return module;
    }

    private static IEnumerable<S_Field> BuildFields(S_Module module, S_EntityType entityType, IEnumerable<DesignField> fields)
    {
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name))
            {
                continue;
            }

            yield return new S_Field
            {
                EntityTypeId = entityType.Id,
                Name = NormalizeName(field.Name) ?? field.Name!,
                Label = field.Label ?? field.Name ?? string.Empty,
                DataType = MapFieldType(field.Type),
                IsRequired = field.Required ?? false,
                DefaultValue = field.DefaultValue,
                EnumValues = field.OptionsJson,
                RelationTargetEntityTypeId = module.EntityTypes
                    .FirstOrDefault(e => IsSameName(e.Name, field.LookupTarget) || IsSameName(e.PluralName, field.LookupTarget))?.Id
            };
        }
    }

    private static List<S_Field> BuildDefaultFields(S_EntityType? entityType)
        =>
        [
            new()
            {
                EntityTypeId = entityType?.Id ?? Guid.Empty,
                Name = "Titre",
                Label = "Titre",
                DataType = FieldDataType.Text,
                IsRequired = true,
                IsSearchable = true,
                IsListVisible = true
            },
            new()
            {
                EntityTypeId = entityType?.Id ?? Guid.Empty,
                Name = "Créé le",
                Label = "Créé le",
                DataType = FieldDataType.Date
            }
        ];

    private static void AppendRelations(S_Module module, IEnumerable<DesignRelation>? relations)
    {
        if (relations is null)
        {
            return;
        }

        foreach (var relation in relations)
        {
            var source = module.EntityTypes.FirstOrDefault(e => IsSameName(e.Name, relation.FromEntity) || IsSameName(e.PluralName, relation.FromEntity));
            if (source is null)
            {
                continue;
            }

            var target = module.EntityTypes.FirstOrDefault(e => IsSameName(e.Name, relation.ToEntity) || IsSameName(e.PluralName, relation.ToEntity));
            if (target is null)
            {
                continue;
            }

            var kind = Enum.TryParse<RelationKind>(relation.Kind, true, out var parsedKind)
                ? parsedKind
                : RelationKind.OneToMany;

            source.Relations.Add(new S_Relation
            {
                FromEntityTypeId = source.Id,
                ToEntityTypeId = target.Id,
                Kind = kind,
                RoleName = NormalizeName(relation.FromField) ?? "Relation"
            });
        }
    }

    private static FieldDataType MapFieldType(string? type) => type?.ToLowerInvariant() switch
    {
        "number" or "int" or "integer" => FieldDataType.Number,
        "decimal" or "float" or "double" => FieldDataType.Decimal,
        "bool" or "boolean" => FieldDataType.Boolean,
        "date" or "datetime" or "timestamp" => FieldDataType.Date,
        "lookup" or "relation" => FieldDataType.Lookup,
        "file" or "image" or "photo" => FieldDataType.File,
        "enum" => FieldDataType.Enum,
        _ => FieldDataType.Text
    };

    private static string? NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    private static string EnsurePlural(string singular)
        => singular.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? singular : $"{singular}s";

    private static bool IsSameName(string left, string? right)
        => right is not null && left.Equals(NormalizeName(right), StringComparison.OrdinalIgnoreCase);

    private sealed class ModuleDesignSchema
    {
        public ModuleDefinition? Module { get; set; }
        public List<DesignEntity> Entities { get; set; } = new();
        public List<DesignRelation> Relations { get; set; } = new();
    }

    private sealed class ModuleDefinition
    {
        public string? Name { get; set; }
        public string? PluralName { get; set; }
        public string? Description { get; set; }
        public string? Icon { get; set; }
    }

    private sealed class DesignEntity
    {
        public string? Name { get; set; }
        public string? PluralName { get; set; }
        public string? Icon { get; set; }
        public List<DesignField> Fields { get; set; } = new();
    }

    private sealed class DesignField
    {
        public string? Name { get; set; }
        public string? Label { get; set; }
        public string? Type { get; set; }
        public bool? Required { get; set; }
        public string? DefaultValue { get; set; }
        public string? LookupTarget { get; set; }
        public string? OptionsJson { get; set; }
    }

    private sealed class DesignRelation
    {
        public string? FromEntity { get; set; }
        public string? ToEntity { get; set; }
        public string? FromField { get; set; }
        public string? Kind { get; set; }
        public bool? IsBidirectional { get; set; }
    }
}
