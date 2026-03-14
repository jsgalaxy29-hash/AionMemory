using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aion.AI;
using Aion.Domain.ModuleBuilder;
using Microsoft.Extensions.Logging;

namespace Aion.AI.ModuleBuilder;

public sealed class ModuleSpecDesigner : IModuleSpecDesigner
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IChatModel _provider;
    private readonly ILogger<ModuleSpecDesigner> _logger;

    public ModuleSpecDesigner(IChatModel provider, ILogger<ModuleSpecDesigner> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public string? LastGeneratedJson { get; private set; }

    public async Task<ModuleSpecDesignResult> DesignAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var generationPrompt = $@"Tu es l'orchestrateur AION. Génère UNIQUEMENT un JSON compact conforme au ModuleSpec v{ModuleSpecVersions.V1}.
Format attendu :
{{
  ""version"": ""{ModuleSpecVersions.V1}"",
  ""slug"": ""nom_module"",
  ""description"": ""Optionnel"",
  ""tables"": [
    {{
      ""slug"": ""table_slug"",
      ""displayName"": ""Nom lisible"",
      ""description"": ""Optionnel"",
      ""fields"": [
        {{ ""slug"": ""champ"", ""label"": ""Libellé"", ""dataType"": ""Text|Number|Decimal|Boolean|Date|DateTime|Enum|Lookup|File|Note|Json|Tags"", ""isRequired"": false }}
      ],
      ""views"": [
        {{ ""slug"": ""list"", ""displayName"": ""Liste"", ""filter"": {{""status"": ""open""}}, ""sort"": ""createdAt desc"", ""isDefault"": true }}
      ]
    }}
  ]
}}
Description utilisateur: {prompt}
Ne réponds que par du JSON valide sans commentaire.";

        var response = await _provider.GenerateAsync(generationPrompt, cancellationToken).ConfigureAwait(false);
        LastGeneratedJson = JsonHelper.ExtractJson(response.Content);

        var spec = ParseSpecOrNull(LastGeneratedJson);
        spec ??= BuildFallback(prompt);

        if (string.IsNullOrWhiteSpace(spec.Version))
        {
            spec.Version = ModuleSpecVersions.V1;
        }

        return new ModuleSpecDesignResult(spec, LastGeneratedJson ?? string.Empty);
    }

    private static ModuleSpec? ParseSpecOrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ModuleSpec>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ModuleSpec BuildFallback(string prompt)
    {
        var slug = Slugify(prompt);
        return new ModuleSpec
        {
            Slug = string.IsNullOrWhiteSpace(slug) ? "module-ia" : slug,
            Description = $"Généré depuis: {prompt}",
            Tables =
            {
                new TableSpec
                {
                    Slug = "items",
                    DisplayName = "Items",
                    Fields = new List<FieldSpec>
                    {
                        new() { Slug = "title", Label = "Titre", DataType = ModuleFieldDataTypes.Text, IsRequired = true, IsSearchable = true, IsListVisible = true },
                        new() { Slug = "status", Label = "Statut", DataType = ModuleFieldDataTypes.Enum, EnumValues = new List<string> { "todo", "doing", "done" }, IsFilterable = true }
                    },
                    Views = new List<ViewSpec>
                    {
                        new() { Slug = "all", DisplayName = "Tous", Filter = new Dictionary<string, string?>(), Sort = "title asc", IsDefault = true }
                    }
                }
            }
        };
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Trim().ToLowerInvariant();
        var chars = new List<char>(cleaned.Length);
        foreach (var c in cleaned)
        {
            chars.Add(char.IsLetterOrDigit(c) ? c : '-');
        }

        return string.Join("", chars).Trim('-');
    }
}
