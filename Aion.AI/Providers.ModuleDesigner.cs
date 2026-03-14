using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using Aion.Domain;
using Aion.AI.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.AI;

public sealed class ModuleDesigner : IModuleDesigner
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    private readonly IChatModel _provider;
    private readonly ILogger<ModuleDesigner> _logger;
    public ModuleDesigner(IChatModel provider, ILogger<ModuleDesigner> logger)
    {
        _provider = provider;
        _logger = logger;
    }
    public string? LastGeneratedJson { get; private set; }
    public async Task<ModuleDesignResult> GenerateModuleAsync(ModuleDesignRequest request, CancellationToken cancellationToken = default)
    {
        var generationPrompt = $@"Tu es l'orchestrateur AION. Génère STRICTEMENT du JSON compact sans texte additionnel avec ce schéma :
{StructuredJsonSchemas.ModuleDesign.Description}
Description utilisateur: {request.Prompt}
Ne réponds que par du JSON valide.";

        var structured = await StructuredJsonResponseHandler.GetValidJsonAsync(
            _provider,
            generationPrompt,
            StructuredJsonSchemas.ModuleDesign,
            _logger,
            cancellationToken).ConfigureAwait(false);

        LastGeneratedJson = structured.Json;
        try
        {
            if (structured.IsValid && !string.IsNullOrWhiteSpace(LastGeneratedJson))
            {
                var design = JsonSerializer.Deserialize<ModuleDesignSchema>(LastGeneratedJson, SerializerOptions);
                if (design is not null)
                {
                    return new ModuleDesignResult(BuildModule(design, request), LastGeneratedJson ?? string.Empty);
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse module design JSON, returning fallback module");
        }
        var fallback = BuildFallbackModule(request.Prompt, request.ModuleNameHint);
        return new ModuleDesignResult(fallback, LastGeneratedJson ?? string.Empty);
    }
    private S_Module BuildModule(ModuleDesignSchema design, ModuleDesignRequest request)
    {
        var moduleName = NormalizeName(design.Module?.Name)
            ?? NormalizeName(request.ModuleNameHint)
            ?? NormalizeName(request.Prompt)
            ?? "Module IA";
        var module = new S_Module
        {
            Name = moduleName,
            Description = design.Module?.PluralName ?? $"Généré depuis: {request.Prompt}",
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
        if (request.IncludeRelations)
        {
            AppendRelations(module, design.Relations);
        }
        return module;
    }
    private void AppendRelations(S_Module module, IEnumerable<DesignRelation>? relations)
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
                EntityTypeId = entityType?.Id ?? Guid.Empty,
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
    private static S_Module BuildFallbackModule(string prompt, string? moduleNameHint)
    {
        var name = NormalizeName(moduleNameHint) ?? NormalizeName(prompt) ?? "Module IA";
        var module = new S_Module
        {
            Name = name,
            Description = "Généré automatiquement"
        };
        var entity = new S_EntityType
        {
            ModuleId = module.Id,
            Name = "Item",
            PluralName = "Items",
            Fields = BuildDefaultFields(null)
        };
        foreach (var field in entity.Fields)
        {
            field.EntityTypeId = entity.Id;
        }
        module.EntityTypes.Add(entity);
        return module;
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
