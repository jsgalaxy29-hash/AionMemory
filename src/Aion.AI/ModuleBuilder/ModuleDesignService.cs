using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using DomainModuleBuilder = Aion.Domain.ModuleBuilder;
using Microsoft.Extensions.Logging;
using Aion.Domain.ModuleBuilder;

namespace Aion.AI.ModuleBuilder;

public sealed class ModuleDesignService : DomainModuleBuilder.IModuleDesignService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IChatModel _provider;
    private readonly IModuleValidator _validator;
    private readonly IModuleSchemaService _moduleSchemaService;
    private readonly ILogger<ModuleDesignService> _logger;
    private static readonly Action<ILogger, Exception?> LogModuleDesignJsonParseFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1, nameof(LogModuleDesignJsonParseFailed)),
            "Failed to parse module design response JSON.");

    private const string ModuleSpecJsonExample =
        """
{
  "version": "{VERSION}",
  "slug": "module",
  "displayName": "Nom lisible",
  "description": "Optionnel",
  "tables": [
    {
      "slug": "table",
      "displayName": "Nom table",
      "fields": [
        { "slug": "champ", "label": "Libellé", "dataType": "Text|Number|Decimal|Boolean|Date|DateTime|Enum|Lookup|File|Note|Json|Tags", "isRequired": false }
      ],
      "views": []
    }
  ]
}
""";

    public ModuleDesignService(IChatModel provider, IModuleValidator validator, IModuleSchemaService moduleSchemaService, ILogger<ModuleDesignService> logger)
    {
        _provider = provider;
        _validator = validator;
        _moduleSchemaService = moduleSchemaService;
        _logger = logger;
    }

    public string? LastGeneratedJson { get; private set; }

    public async Task<DomainModuleBuilder.ModuleDesignResult> DesignModuleAsync(DomainModuleBuilder.ModuleDesignRequest request, CancellationToken cancellationToken = default)
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
            return new DomainModuleBuilder.ModuleDesignResult(
                null,
                BuildFallbackQuestions(request.Prompt),
                Array.Empty<DomainModuleBuilder.ModuleDesignSource>(),
                structured.Json ?? string.Empty);
        }

        ModuleDesignResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<ModuleDesignResponse>(structured.Json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            LogModuleDesignJsonParseFailed(_logger, ex);
            return new DomainModuleBuilder.ModuleDesignResult(
                null,
                BuildFallbackQuestions(request.Prompt),
                Array.Empty<DomainModuleBuilder.ModuleDesignSource>(),
                structured.Json ?? string.Empty);
        }

        if (response is null)
        {
            return new DomainModuleBuilder.ModuleDesignResult(
                null,
                BuildFallbackQuestions(request.Prompt),
                Array.Empty<DomainModuleBuilder.ModuleDesignSource>(),
                structured.Json ?? string.Empty);
        }

        var sources = MapSources(response.Sources);

        if (string.Equals(response.Status, "clarify", StringComparison.OrdinalIgnoreCase))
        {
            var questions = MapQuestions(response.Questions);
            return new DomainModuleBuilder.ModuleDesignResult(null, questions, sources, structured.Json ?? string.Empty);
        }

        if (!string.Equals(response.Status, "complete", StringComparison.OrdinalIgnoreCase))
        {
            return new DomainModuleBuilder.ModuleDesignResult(
                null,
                BuildFallbackQuestions(request.Prompt),
                sources,
                structured.Json ?? string.Empty);
        }

        var spec = ParseSpec(response.Spec);
        if (spec is null)
        {
            return new DomainModuleBuilder.ModuleDesignResult(
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
            return new DomainModuleBuilder.ModuleDesignResult(null, questions, sources, structured.Json ?? string.Empty);
        }

        return new DomainModuleBuilder.ModuleDesignResult(spec, Array.Empty<DomainModuleBuilder.ModuleDesignQuestion>(), sources, structured.Json ?? string.Empty);
    }

    public async Task<DomainModuleBuilder.ModuleDesignApplyResult> DesignAndApplyAsync(DomainModuleBuilder.ModuleDesignRequest request, CancellationToken cancellationToken = default)
    {
        var design = await DesignModuleAsync(request, cancellationToken).ConfigureAwait(false);
        if (!design.IsComplete || design.Spec is null)
        {
            return new DomainModuleBuilder.ModuleDesignApplyResult(design, Array.Empty<STable>());
        }

        var createdTable = await _moduleSchemaService.CreateModuleAsync(design.Spec, cancellationToken).ConfigureAwait(false);
        return new DomainModuleBuilder.ModuleDesignApplyResult(design, new[] { createdTable });
    }

    private static string BuildPrompt(DomainModuleBuilder.ModuleDesignRequest request)
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
{ModuleSpecJsonExample.Replace("{VERSION}", ModuleSpecVersions.V1, StringComparison.Ordinal)}

Description utilisateur (locale {request.Locale}) :
{request.Prompt}

Réponses déjà fournies :
{answersSection}

Ne réponds que par du JSON valide sans texte additionnel.
""";
    }

    private static string BuildAnswersSection(IReadOnlyList<DomainModuleBuilder.ModuleDesignAnswer> answers)
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

    private static IReadOnlyList<DomainModuleBuilder.ModuleDesignQuestion> BuildFallbackQuestions(string prompt)
        => new[]
        {
            new DomainModuleBuilder.ModuleDesignQuestion("module-goal", $"Quel est l'objectif principal du module \"{prompt}\" ?", true, "Ex: suivi d'activité, CRM, inventaire"),
            new DomainModuleBuilder.ModuleDesignQuestion("entities", "Quelles sont les entités principales à suivre ?", true, "Ex: clients, projets, commandes"),
            new DomainModuleBuilder.ModuleDesignQuestion("fields", "Quels champs clés sont indispensables pour chaque entité ?", true, "Ex: statut, date, montant"),
            new DomainModuleBuilder.ModuleDesignQuestion("relations", "Y a-t-il des relations ou statuts spécifiques à modéliser ?", false, "Ex: client → commandes, statut en cours")
        };

    private static IReadOnlyList<DomainModuleBuilder.ModuleDesignQuestion> BuildValidationQuestions(IEnumerable<string> errors)
        => errors
            .Select((error, index) => new DomainModuleBuilder.ModuleDesignQuestion($"validation-{index + 1}", $"Peux-tu préciser : {error}", true))
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

    private static IReadOnlyList<DomainModuleBuilder.ModuleDesignQuestion> MapQuestions(IReadOnlyList<ModuleDesignQuestionPayload>? questions)
    {
        if (questions is null)
        {
            return Array.Empty<DomainModuleBuilder.ModuleDesignQuestion>();
        }

        return questions
            .Where(q => !string.IsNullOrWhiteSpace(q.Question))
            .Select((q, index) =>
            {
                var id = string.IsNullOrWhiteSpace(q.Id) ? $"question-{index + 1}" : q.Id;
                return new DomainModuleBuilder.ModuleDesignQuestion(id, q.Question ?? string.Empty, q.Required ?? true, q.Hint);
            })
            .ToList();
    }

    private static IReadOnlyList<DomainModuleBuilder.ModuleDesignSource> MapSources(IReadOnlyList<ModuleDesignSourcePayload>? sources)
    {
        if (sources is null)
        {
            return Array.Empty<DomainModuleBuilder.ModuleDesignSource>();
        }

        return sources
            .Where(s => !string.IsNullOrWhiteSpace(s.Title))
            .Select(s => new DomainModuleBuilder.ModuleDesignSource(s.Title ?? string.Empty, ParseUrl(s.Url), s.Type))
            .ToList();
    }

    private static Uri? ParseUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed : null;

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
