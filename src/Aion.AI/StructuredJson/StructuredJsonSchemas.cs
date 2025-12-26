using System.Text.Json;

namespace Aion.AI;

internal static class StructuredJsonSchemas
{
    private static readonly HashSet<string> AllowedFieldTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Text", "Number", "Decimal", "Boolean", "Date", "DateTime", "Lookup", "File", "Note", "Json", "Tags", "Calculated"
    };

    public static readonly StructuredJsonSchema Intent = new(
        "intent",
        """{"intent":"","parameters":{},"confidence":0.0}""",
        ValidateIntent);

    public static readonly StructuredJsonSchema ModuleDesign = new(
        "module-design",
        """
{
  "module": { "name": "", "pluralName": "", "icon": "" },
  "entities": [
    {
      "name": "",
      "pluralName": "",
      "icon": "",
      "fields": [
        { "name": "", "label": "", "type": "Text|Number|Decimal|Boolean|Date|DateTime|Lookup|File|Note|Json|Tags|Calculated" }
      ]
    }
  ],
  "relations": [
    { "fromEntity": "", "toEntity": "", "fromField": "", "kind": "OneToMany|ManyToMany", "isBidirectional": false }
  ]
}
""",
        ValidateModuleDesign);

    public static readonly StructuredJsonSchema LegacyModuleDesign = new(
        "legacy-module-design",
        """
{
  "Name": "Nom du module",
  "Description": "Description détaillée",
  "EntityTypes": [
    {
      "Name": "NomEntite",
      "PluralName": "NomsEntites",
      "Fields": [
        { "Name": "NomChamp", "Label": "Label Champ", "DataType": "Text|Number|Decimal|Date|DateTime|Boolean|Lookup|Tags|File|Note" }
      ]
    }
  ]
}
""",
        ValidateLegacyModuleDesign);

    public static readonly StructuredJsonSchema TranscriptionMetadata = new(
        "transcription-metadata",
        """{"language":"","summary":"","keywords":[""],"durationSeconds":0.0}""",
        ValidateTranscriptionMetadata);

    private static bool ValidateIntent(JsonElement root, out string? error)
    {
        error = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "Racine invalide";
            return false;
        }

        if (!HasOnlyProperties(root, "intent", "parameters", "confidence"))
        {
            error = "Propriétés inattendues";
            return false;
        }

        if (!TryGetNonEmptyString(root, "intent", out _))
        {
            error = "intent manquant ou vide";
            return false;
        }

        if (!root.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
        {
            error = "parameters invalide";
            return false;
        }

        if (!root.TryGetProperty("confidence", out var confidence) || confidence.ValueKind != JsonValueKind.Number)
        {
            error = "confidence invalide";
            return false;
        }

        var value = confidence.GetDouble();
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d || value > 1d)
        {
            error = "confidence hors limites";
            return false;
        }

        return true;
    }

    private static bool ValidateModuleDesign(JsonElement root, out string? error)
    {
        error = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "Racine invalide";
            return false;
        }

        if (!HasOnlyProperties(root, "module", "entities", "relations"))
        {
            error = "Propriétés inattendues";
            return false;
        }

        if (!root.TryGetProperty("module", out var moduleElement) || moduleElement.ValueKind != JsonValueKind.Object)
        {
            error = "module invalide";
            return false;
        }

        if (!HasOnlyProperties(moduleElement, "name", "pluralName", "icon"))
        {
            error = "module propriétés inattendues";
            return false;
        }

        if (!TryGetNonEmptyString(moduleElement, "name", out _))
        {
            error = "module.name manquant";
            return false;
        }

        if (!root.TryGetProperty("entities", out var entitiesElement) || entitiesElement.ValueKind != JsonValueKind.Array)
        {
            error = "entities invalide";
            return false;
        }

        foreach (var entity in entitiesElement.EnumerateArray())
        {
            if (entity.ValueKind != JsonValueKind.Object)
            {
                error = "entité invalide";
                return false;
            }

            if (!HasOnlyProperties(entity, "name", "pluralName", "icon", "fields"))
            {
                error = "entité propriétés inattendues";
                return false;
            }

            if (!TryGetNonEmptyString(entity, "name", out _) || !TryGetNonEmptyString(entity, "pluralName", out _))
            {
                error = "entité name/pluralName manquant";
                return false;
            }

            if (!entity.TryGetProperty("fields", out var fieldsElement) || fieldsElement.ValueKind != JsonValueKind.Array)
            {
                error = "fields invalide";
                return false;
            }

            foreach (var field in fieldsElement.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.Object)
                {
                    error = "field invalide";
                    return false;
                }

                if (!HasOnlyProperties(field, "name", "label", "type"))
                {
                    error = "field propriétés inattendues";
                    return false;
                }

                if (!TryGetNonEmptyString(field, "name", out _) || !TryGetNonEmptyString(field, "label", out _))
                {
                    error = "field name/label manquant";
                    return false;
                }

                if (!TryGetNonEmptyString(field, "type", out var type) || !AllowedFieldTypes.Contains(type))
                {
                    error = "field type invalide";
                    return false;
                }
            }
        }

        if (!root.TryGetProperty("relations", out var relationsElement) || relationsElement.ValueKind != JsonValueKind.Array)
        {
            error = "relations invalide";
            return false;
        }

        foreach (var relation in relationsElement.EnumerateArray())
        {
            if (relation.ValueKind != JsonValueKind.Object)
            {
                error = "relation invalide";
                return false;
            }

            if (!HasOnlyProperties(relation, "fromEntity", "toEntity", "fromField", "kind", "isBidirectional"))
            {
                error = "relation propriétés inattendues";
                return false;
            }

            if (!TryGetNonEmptyString(relation, "fromEntity", out _)
                || !TryGetNonEmptyString(relation, "toEntity", out _)
                || !TryGetNonEmptyString(relation, "fromField", out _))
            {
                error = "relation champs manquants";
                return false;
            }

            if (!TryGetNonEmptyString(relation, "kind", out var kind)
                || !(string.Equals(kind, "OneToMany", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kind, "ManyToMany", StringComparison.OrdinalIgnoreCase)))
            {
                error = "relation kind invalide";
                return false;
            }

            if (!relation.TryGetProperty("isBidirectional", out var bidirectional)
                || (bidirectional.ValueKind != JsonValueKind.True && bidirectional.ValueKind != JsonValueKind.False))
            {
                error = "relation isBidirectional invalide";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateTranscriptionMetadata(JsonElement root, out string? error)
    {
        error = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "Racine invalide";
            return false;
        }

        if (!HasOnlyProperties(root, "language", "summary", "keywords", "durationSeconds"))
        {
            error = "Propriétés inattendues";
            return false;
        }

        if (!TryGetNonEmptyString(root, "language", out _))
        {
            error = "language manquant";
            return false;
        }

        if (!TryGetNonEmptyString(root, "summary", out _))
        {
            error = "summary manquant";
            return false;
        }

        if (!root.TryGetProperty("keywords", out var keywords) || keywords.ValueKind != JsonValueKind.Array)
        {
            error = "keywords invalide";
            return false;
        }

        foreach (var keyword in keywords.EnumerateArray())
        {
            if (keyword.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(keyword.GetString()))
            {
                error = "keywords invalide";
                return false;
            }
        }

        if (!root.TryGetProperty("durationSeconds", out var duration)
            || (duration.ValueKind != JsonValueKind.Number && duration.ValueKind != JsonValueKind.Null))
        {
            error = "durationSeconds invalide";
            return false;
        }

        return true;
    }

    private static bool ValidateLegacyModuleDesign(JsonElement root, out string? error)
    {
        error = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            error = "Racine invalide";
            return false;
        }

        if (!HasOnlyProperties(root, "Name", "Description", "EntityTypes"))
        {
            error = "Propriétés inattendues";
            return false;
        }

        if (!TryGetNonEmptyString(root, "Name", out _))
        {
            error = "Name manquant";
            return false;
        }

        if (!root.TryGetProperty("EntityTypes", out var entities) || entities.ValueKind != JsonValueKind.Array)
        {
            error = "EntityTypes invalide";
            return false;
        }

        foreach (var entity in entities.EnumerateArray())
        {
            if (entity.ValueKind != JsonValueKind.Object)
            {
                error = "EntityType invalide";
                return false;
            }

            if (!HasOnlyProperties(entity, "Name", "PluralName", "Fields"))
            {
                error = "EntityType propriétés inattendues";
                return false;
            }

            if (!TryGetNonEmptyString(entity, "Name", out _) || !TryGetNonEmptyString(entity, "PluralName", out _))
            {
                error = "EntityType Name/PluralName manquant";
                return false;
            }

            if (!entity.TryGetProperty("Fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
            {
                error = "Fields invalide";
                return false;
            }

            foreach (var field in fields.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.Object)
                {
                    error = "Field invalide";
                    return false;
                }

                if (!HasOnlyProperties(field, "Name", "Label", "DataType"))
                {
                    error = "Field propriétés inattendues";
                    return false;
                }

                if (!TryGetNonEmptyString(field, "Name", out _) || !TryGetNonEmptyString(field, "Label", out _))
                {
                    error = "Field Name/Label manquant";
                    return false;
                }

                if (!TryGetNonEmptyString(field, "DataType", out _))
                {
                    error = "Field DataType manquant";
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryGetNonEmptyString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var content = property.GetString();
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        value = content.Trim();
        return true;
    }

    private static bool HasOnlyProperties(JsonElement element, params string[] allowedProperties)
    {
        var allowed = new HashSet<string>(allowedProperties, StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                return false;
            }
        }

        return true;
    }
}
