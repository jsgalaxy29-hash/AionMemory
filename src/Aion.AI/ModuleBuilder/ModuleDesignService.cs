using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Microsoft.Extensions.Logging;

namespace Aion.AI.ModuleBuilder;

public sealed class ModuleDesignService : IModuleDesignService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IChatModel _provider;
    private readonly IModuleValidator _validator;
    private readonly IModuleApplier _applier;
    private readonly ILogger<ModuleDesignService> _logger;

    public ModuleDesignService(IChatModel provider, IModuleValidator validator, IModuleApplier applier, ILogger<ModuleDesignService> logger)
    {
        _provider = provider;
        _validator = validator;
        _applier = applier;
        _logger = logger;
    }

    public string? LastGeneratedJson { get; private set; }

    public async Task<ModuleDesignResult> DesignModuleAsync(ModuleDesignRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("Prompt cannot be empty.", nameof(request));
        }

        var prompt = BuildPrompt(request);
        var structured = await StructuredJsonResponseHandler.GetValidJsonAsync(
            _provider,
            prompt,
            StructuredJsonSchemas.ModuleSpecDesign,
            _logger,
            cancellationToken).ConfigureAwait(false);

        LastGeneratedJson = structured.Json;

        if (!structured.IsValid || string.IsNullOrWhiteSpace(structured.Json))
        {
            return new ModuleDesignResult(
                null,
                BuildFallbackQuestions(request.Prompt),
                Array.Empty<ModuleDesignSource>(),
                structured.Json ?? string.Empty);
        }

        ModuleDesignResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<ModuleDesignResponse>(structured.Json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse module design response JSON.");
            return new ModuleDesignResult(
                null,
                BuildFallbackQuestions(request.Prompt),
                Array.Empty<ModuleDesignSource>(),
                structured.Json ?? string.Empty);
        }

        if (response is null)
        {
            return new ModuleDesignResult(
                null,
                BuildFallbackQuestions(request.Prompt),
                Array.Empty<ModuleDesignSource>(),
                structured.Json ?? string.Empty);
        }

        var sources = MapSources(response.Sources);

        if (string.Equals(response.Status, "clarify", StringComparison.OrdinalIgnoreCase))
        {
            var questions = MapQuestions(response.Questions);
            return new ModuleDesignResult(null, questions, sources, structured.Json ?? string.Empty);
        }

        if (!string.Equals(response.Status, "complete", StringComparison.OrdinalIgnoreCase))
        {
            return new ModuleDesignResult(
                null,
                BuildFallbackQuestions(request.Prompt),
                sources,
                structured.Json ?? string.Empty);
        }

        var spec = ParseSpec(response.Spec);
        if (spec is null)
        {
            return new ModuleDesignResult(
                null,
                BuildFallbackQuestions(request.Prompt),
                sources,
                structured.Json ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(spec.Version))
        {
            spec.Version = ModuleSpecVersions.V1;
        }

        var validation = await _validator.ValidateAsync(spec, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            var questions = BuildValidationQuestions(validation.Errors);
            return new ModuleDesignResult(null, questions, sources, structured.Json ?? string.Empty);
        }

        return new ModuleDesignResult(spec, Array.Empty<ModuleDesignQuestion>(), sources, structured.Json ?? string.Empty);
    }

    public async Task<ModuleDesignApplyResult> DesignAndApplyAsync(ModuleDesignRequest request, CancellationToken cancellationToken = default)
    {
        var design = await DesignModuleAsync(request, cancellationToken).ConfigureAwait(false);
        if (!design.IsComplete || design.Spec is null)
        {
            return new ModuleDesignApplyResult(design, Array.Empty<STable>());
        }

        var tables = await _applier.ApplyAsync(design.Spec, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new ModuleDesignApplyResult(design, tables);
    }

    private static string BuildPrompt(ModuleDesignRequest request)
    {
        var answersSection = BuildAnswersSection(request.Answers);
        var schemaOrg = request.UseSchemaOrg
            ? "Utilise schema.org pour nommer les entités/champs quand pertinent et ajoute la source associée."
            : "Ignore schema.org et laisse la liste des sources vide.";

        return $"""
Tu es l'orchestrateur AION chargé de concevoir un ModuleSpec v{ModuleSpecVersions.V1}.
Si des informations manquent ou sont ambiguës, renvoie un statut "clarify" avec des questions ciblées.
Sinon, renvoie un statut "complete" avec la spec finale.
{schemaOrg}

Format JSON attendu :
{StructuredJsonSchemas.ModuleSpecDesign.Description}

ModuleSpec v{ModuleSpecVersions.V1} attendu :
{{
  "version": "{ModuleSpecVersions.V1}",
  "slug": "module",
  "displayName": "Nom lisible",
  "description": "Optionnel",
  "tables": [
    {{
      "slug": "table",
      "displayName": "Nom table",
      "fields": [
        {{ "slug": "champ", "label": "Libellé", "dataType": "Text|Number|Decimal|Boolean|Date|DateTime|Enum|Lookup|File|Note|Json|Tags", "isRequired": false }}
      ],
      "views": []
    }}
  ]
}}

Description utilisateur (locale {request.Locale}) :
{request.Prompt}

Réponses déjà fournies :
{answersSection}

Ne réponds que par du JSON valide sans texte additionnel.
""";
    }

    private static string BuildAnswersSection(IReadOnlyList<ModuleDesignAnswer> answers)
    {
        if (answers.Count == 0)
        {
            return "Aucune réponse fournie.";
        }

        var builder = new StringBuilder();
        foreach (var answer in answers)
        {
            builder.AppendLine($"- {answer.QuestionId}: {answer.Answer}");
        }

        return builder.ToString().Trim();
    }

    private static IReadOnlyList<ModuleDesignQuestion> BuildFallbackQuestions(string prompt)
        => new[]
        {
            new ModuleDesignQuestion("module-goal", $"Quel est l'objectif principal du module \"{prompt}\" ?", true, "Ex: suivi d'activité, CRM, inventaire"),
            new ModuleDesignQuestion("entities", "Quelles sont les entités principales à suivre ?", true, "Ex: clients, projets, commandes"),
            new ModuleDesignQuestion("fields", "Quels champs clés sont indispensables pour chaque entité ?", true, "Ex: statut, date, montant"),
            new ModuleDesignQuestion("relations", "Y a-t-il des relations ou statuts spécifiques à modéliser ?", false, "Ex: client → commandes, statut en cours")
        };

    private static IReadOnlyList<ModuleDesignQuestion> BuildValidationQuestions(IEnumerable<string> errors)
        => errors
            .Select((error, index) => new ModuleDesignQuestion($"validation-{index + 1}", $"Peux-tu préciser : {error}", true))
            .ToList();

    private static ModuleSpec? ParseSpec(JsonElement? specElement)
    {
        if (specElement is null || specElement.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ModuleSpec>(specElement.Value.GetRawText(), SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<ModuleDesignQuestion> MapQuestions(IReadOnlyList<ModuleDesignQuestionPayload>? questions)
        => questions?
            .Where(q => !string.IsNullOrWhiteSpace(q.Question))
            .Select((q, index) =>
            {
                var id = string.IsNullOrWhiteSpace(q.Id) ? $"question-{index + 1}" : q.Id;
                return new ModuleDesignQuestion(id, q.Question ?? string.Empty, q.Required ?? true, q.Hint);
            })
            .ToList()
            ?? Array.Empty<ModuleDesignQuestion>();

    private static IReadOnlyList<ModuleDesignSource> MapSources(IReadOnlyList<ModuleDesignSourcePayload>? sources)
        => sources?
            .Where(s => !string.IsNullOrWhiteSpace(s.Title))
            .Select(s => new ModuleDesignSource(s.Title ?? string.Empty, s.Url, s.Type))
            .ToList()
            ?? Array.Empty<ModuleDesignSource>();

    private sealed class ModuleDesignResponse
    {
        public string? Status { get; set; }
        public List<ModuleDesignQuestionPayload> Questions { get; set; } = new();
        public JsonElement? Spec { get; set; }
        public List<ModuleDesignSourcePayload> Sources { get; set; } = new();
    }

    private sealed class ModuleDesignQuestionPayload
    {
        public string? Id { get; set; }
        public string? Question { get; set; }
        public bool? Required { get; set; }
        public string? Hint { get; set; }
    }

    private sealed class ModuleDesignSourcePayload
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Type { get; set; }
    }
}
