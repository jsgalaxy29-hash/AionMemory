using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Linq;
using Aion.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace Aion.AI;
internal static class HttpClientNames
{
    public const string Llm = "aion-ai-llm";
    public const string Embeddings = "aion-ai-embeddings";
    public const string Transcription = "aion-ai-transcription";
    public const string Vision = "aion-ai-vision";
}
public sealed class HttpTextGenerationProvider : ILLMProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<HttpTextGenerationProvider> _logger;
    public HttpTextGenerationProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<HttpTextGenerationProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }
    public async Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue;
        var client = CreateClient(HttpClientNames.Llm, opts.LlmEndpoint ?? opts.BaseEndpoint);
        if (client.BaseAddress is null)
        {
            _logger.LogWarning("LLM endpoint not configured; returning stub response");
            return new LlmResponse($"[stub-llm] {prompt}", string.Empty, opts.LlmModel);
        }
        var payload = new
        {
            model = opts.LlmModel ?? opts.EmbeddingModel ?? "generic-llm",
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = opts.MaxTokens,
            temperature = opts.Temperature
        };
        var response = await HttpRetryHelper.SendWithRetryAsync(client, () => new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        }, _logger, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("LLM call failed with status {Status}; returning stub", response.StatusCode);
            return new LlmResponse($"[stub-llm-fallback] {prompt}", string.Empty, opts.LlmModel);
        }
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        return new LlmResponse(content ?? string.Empty, json, opts.LlmModel);
    }
    private HttpClient CreateClient(string clientName, string? endpoint)
    {
        var client = _httpClientFactory.CreateClient(clientName);
        EnsureClientConfigured(client, endpoint, _options.CurrentValue);
        return client;
    }
    internal static void EnsureClientConfigured(HttpClient client, string? endpoint, AionAiOptions options)
    {
        if (client.BaseAddress is null && Uri.TryCreate(endpoint ?? options.BaseEndpoint ?? string.Empty, UriKind.Absolute, out var uri))
        {
            client.BaseAddress = uri;
        }
        client.Timeout = options.RequestTimeout;
        if (!string.IsNullOrWhiteSpace(options.ApiKey) && client.DefaultRequestHeaders.Authorization is null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }
        foreach (var header in options.DefaultHeaders)
        {
            client.DefaultRequestHeaders.Remove(header.Key);
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }
    }
}
internal static class HttpRetryHelper
{
    internal static async Task<HttpResponseMessage> SendWithRetryAsync(HttpClient client, Func<HttpRequestMessage> requestFactory, ILogger logger, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = requestFactory();
            var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode || !ShouldRetry(response.StatusCode))
            {
                return response;
            }
            response.Dispose();
            var delay = GetDelay(response.Headers.RetryAfter?.Delta, attempt);
            logger.LogWarning("Request throttled (status {Status}); retrying in {Delay}s", response.StatusCode, delay.TotalSeconds);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
    }
    private static bool ShouldRetry(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;
    private static TimeSpan GetDelay(TimeSpan? retryAfter, int attempt)
        => retryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
}

internal static class JsonHelper
{
    public static string ExtractJson(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var cleaned = input.Trim();
        if (cleaned.StartsWith("```") && cleaned.EndsWith("```"))
        {
            cleaned = cleaned.Trim('`', '\n', '\r');
        }

        var start = cleaned.IndexOf('{');
        var end = cleaned.LastIndexOf('}');
        return start >= 0 && end >= start ? cleaned[start..(end + 1)] : cleaned;
    }
}


public sealed class HttpEmbeddingProvider : IEmbeddingProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<HttpEmbeddingProvider> _logger;

    public HttpEmbeddingProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<HttpEmbeddingProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(HttpClientNames.Embeddings);
        HttpTextGenerationProvider.EnsureClientConfigured(client, opts.EmbeddingsEndpoint ?? opts.BaseEndpoint, opts);

        if (client.BaseAddress is null)
        {
            _logger.LogWarning("Embedding endpoint not configured; returning stub vector");
            var vector = Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
            return new EmbeddingResult(vector, opts.EmbeddingModel ?? opts.LlmModel);
        }

        var payload = new { model = opts.EmbeddingModel ?? opts.LlmModel ?? "generic-embedding", input = text };
        var response = await HttpRetryHelper.SendWithRetryAsync(client, () => new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        }, _logger, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Embedding call failed with status {Status}; returning stub", response.StatusCode);
            var vector = Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
            return new EmbeddingResult(vector, opts.EmbeddingModel ?? opts.LlmModel);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var values = doc.RootElement.GetProperty("data")[0].GetProperty("embedding").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        return new EmbeddingResult(values, opts.EmbeddingModel ?? opts.LlmModel, json);
    }
}
public sealed class HttpAudioTranscriptionProvider : IAudioTranscriptionProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<HttpAudioTranscriptionProvider> _logger;
    public HttpAudioTranscriptionProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<HttpAudioTranscriptionProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }
    public async Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(HttpClientNames.Transcription);
        HttpTextGenerationProvider.EnsureClientConfigured(client, opts.TranscriptionEndpoint ?? opts.BaseEndpoint, opts);
        if (client.BaseAddress is null)
        {
            _logger.LogWarning("Transcription endpoint not configured; returning stub transcription");
            return new TranscriptionResult($"[stub-transcription] {fileName}", TimeSpan.Zero, opts.TranscriptionModel);
        }
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(audioStream), "file", fileName);
        content.Add(new StringContent(opts.TranscriptionModel ?? opts.LlmModel ?? "whisper"), "model");
        var response = await HttpRetryHelper.SendWithRetryAsync(client, () => new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions")
        {
            Content = content
        }, _logger, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Transcription call failed with status {Status}; returning stub", response.StatusCode);
            return new TranscriptionResult($"[stub-transcription] {fileName}", TimeSpan.Zero, opts.TranscriptionModel);
        }
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
        return new TranscriptionResult(text, TimeSpan.Zero, opts.TranscriptionModel);
    }
}
public sealed class HttpVisionProvider : IVisionService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<HttpVisionProvider> _logger;
    public HttpVisionProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<HttpVisionProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }
    public async Task<S_VisionAnalysis> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(HttpClientNames.Vision);
        HttpTextGenerationProvider.EnsureClientConfigured(client, opts.VisionEndpoint ?? opts.BaseEndpoint, opts);
        if (client.BaseAddress is null)
        {
            _logger.LogWarning("Vision endpoint not configured; returning stub analysis");
            return BuildStub(request.FileId, request.AnalysisType, "Unconfigured vision endpoint");
        }
        var payload = new { fileId = request.FileId, analysisType = request.AnalysisType.ToString(), model = request.Model ?? opts.VisionModel ?? "vision-generic" };
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "vision/analyze")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        };
        var response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Vision call failed with status {Status}; returning stub", response.StatusCode);
            return BuildStub(request.FileId, request.AnalysisType, "Vision call failed");
        }
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new S_VisionAnalysis
        {
            FileId = request.FileId,
            AnalysisType = request.AnalysisType,
            ResultJson = json
        };
    }
    private static S_VisionAnalysis BuildStub(Guid fileId, VisionAnalysisType analysisType, string message)
        => new()
        {
            FileId = fileId,
            AnalysisType = analysisType,
            ResultJson = JsonSerializer.Serialize(new { summary = message, tags = new[] { "stub" } }, SerializerOptions)
        };
}
public sealed class IntentRecognizer : IIntentDetector
{
    private readonly ILLMProvider _provider;
    private readonly ILogger<IntentRecognizer> _logger;

    public IntentRecognizer(ILLMProvider provider, ILogger<IntentRecognizer> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<IntentDetectionResult> DetectAsync(IntentDetectionRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
Analyse l'intention utilisateur pour l'entrée suivante et réponds uniquement en JSON compact :
{"intent":"<type>","parameters":{...},"confidence":0.0}
Message: "{{request.Input}}"
""";

        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        var json = JsonHelper.ExtractJson(response.Content);

        try
        {
            var result = JsonSerializer.Deserialize<IntentRecognitionResultInternal>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (result is not null)
            {
                return new IntentDetectionResult(
                    result.Intent ?? "unknown",
                    result.Parameters ?? new Dictionary<string, string>(),
                    result.Confidence ?? 0.5,
                    response.RawResponse ?? json);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse intent response, returning fallback structure");
        }

        return new IntentDetectionResult("unknown", new Dictionary<string, string> { ["raw"] = request.Input }, 0.1, response.RawResponse ?? json);
    }
    private sealed class IntentRecognitionResultInternal
    {
        public string? Intent { get; set; }
        public Dictionary<string, string>? Parameters { get; set; }
        public double? Confidence { get; set; }
    }
}
public sealed class ModuleDesigner : IModuleDesigner
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    private readonly ILLMProvider _provider;
    private readonly ILogger<ModuleDesigner> _logger;
    public ModuleDesigner(ILLMProvider provider, ILogger<ModuleDesigner> logger)
    {
        _provider = provider;
        _logger = logger;
    }
    public string? LastGeneratedJson { get; private set; }
    public async Task<ModuleDesignResult> GenerateModuleAsync(ModuleDesignRequest request, CancellationToken cancellationToken = default)
    {
        var generationPrompt = $$"""
Tu es l'orchestrateur AION. Génère STRICTEMENT du JSON compact sans texte additionnel avec ce schéma :
{
  "module": { "name": "", "pluralName": "", "icon": "" },
  "entities": [
    {
      "name": "",
      "pluralName": "",
      "icon": "",
      "fields": [ { "name": "", "label": "", "type": "Text|Number|Decimal|Boolean|Date|DateTime|Lookup|File|Note|Json|Tags|Calculated" } ]
    }
  ],
  "relations": [ { "fromEntity": "", "toEntity": "", "fromField": "", "kind": "OneToMany|ManyToMany", "isBidirectional": false } ]
}
Description utilisateur: {{request.Prompt}}
Ne réponds que par du JSON valide.
""";
        var response = await _provider.GenerateAsync(generationPrompt, cancellationToken).ConfigureAwait(false);
        LastGeneratedJson = JsonHelper.ExtractJson(response.Content);
        try
        {
            var design = JsonSerializer.Deserialize<ModuleDesignSchema>(LastGeneratedJson, SerializerOptions);
            if (design is not null)
            {
                return new ModuleDesignResult(BuildModule(design, request), LastGeneratedJson ?? string.Empty);
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
            var fields = entity.Fields?.Count > 0 ? BuildFields(entityType, entity.Fields) : BuildDefaultFields(entityType);
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
    private static IEnumerable<S_Field> BuildFields(S_EntityType entityType, IEnumerable<DesignField> fields)
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
                DataType = EnumSFieldType.String,
                IsRequired = true,
                IsSearchable = true,
                IsListVisible = true
            },
            new()
            {
                EntityTypeId = entityType?.Id ?? Guid.Empty,
                Name = "Créé le",
                Label = "Créé le",
                DataType = EnumSFieldType.Date
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
    private static EnumSFieldType MapFieldType(string? type) => type?.ToLowerInvariant() switch
    {
        "number" or "int" or "integer" => EnumSFieldType.Int,
        "decimal" or "float" or "double" => EnumSFieldType.Decimal,
        "bool" or "boolean" => EnumSFieldType.Bool,
        "date" or "datetime" or "timestamp" => EnumSFieldType.Date,
        "lookup" or "relation" => EnumSFieldType.Relation,
        "file" or "image" or "photo" => EnumSFieldType.File,
        "enum" => EnumSFieldType.Enum,
        _ => EnumSFieldType.String
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
public sealed class CrudInterpreter : ICrudInterpreter
{
    private readonly ILLMProvider _provider;
    private readonly ILogger<CrudInterpreter> _logger;

    public CrudInterpreter(ILLMProvider provider, ILogger<CrudInterpreter> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<CrudInterpretation> GenerateQueryAsync(CrudQueryRequest request, CancellationToken cancellationToken = default)
    {
        var fieldNames = string.Join(", ", request.Module.EntityTypes.SelectMany(e => e.Fields.Select(f => f.Name)));
        var prompt = $$$"""
Tu es un assistant qui traduit les requêtes utilisateur en opérations CRUD pour le module suivant:
Module: {{request.Module.Name}}
Champs: {{fieldNames}}
Réponds par une instruction JSON avec {"action":"create|update|delete|query","filters":{},"payload":{}} pour: {{request.Intent}}
""";

        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        var cleaned = JsonHelper.ExtractJson(response.Content);

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            _logger.LogWarning("Empty CRUD interpretation, returning stub query");
            return new CrudInterpretation("query", new Dictionary<string, string?> { ["raw"] = request.Intent }, new Dictionary<string, string?>(), response.RawResponse ?? string.Empty);
        }

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;
            var action = root.TryGetProperty("action", out var actionProperty) ? actionProperty.GetString() ?? "query" : "query";
            var filters = ExtractStringDictionary(root, "filters");
            var payload = ExtractStringDictionary(root, "payload");
            return new CrudInterpretation(action, filters, payload, response.RawResponse ?? cleaned);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Unable to parse CRUD interpretation, returning fallback");
            return new CrudInterpretation("query", new Dictionary<string, string?> { ["raw"] = request.Intent }, new Dictionary<string, string?>(), response.RawResponse ?? cleaned);
        }
    }

    private static Dictionary<string, string?> ExtractStringDictionary(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string?>();
        }

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in element.EnumerateObject())
        {
            result[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => prop.Value.GetRawText()
            };
        }

        return result;
    }
}
public sealed class AgendaInterpreter : IAgendaInterpreter
{
    private readonly ILLMProvider _provider;

    public AgendaInterpreter(ILLMProvider provider)
    {
        _provider = provider;
    }
    public async Task<S_Event> CreateEventAsync(string input, CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
Génère un événement JSON {"title":"","start":"ISO","end":"ISO|null","reminder":"ISO|null"} pour: {{input}}
""";
        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        try
        {
            var evt = JsonSerializer.Deserialize<AgendaResponse>(JsonHelper.ExtractJson(response.Content), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (evt is not null)
            {
                return new S_Event
                {
                    Title = evt.Title ?? input,
                    Description = input,
                    Start = ParseDate(evt.Start) ?? DateTimeOffset.UtcNow,
                    End = ParseDate(evt.End),
                    ReminderAt = ParseDate(evt.Reminder)
                };
            }
        }
        catch (JsonException)
        {
        }
        return new S_Event { Title = input, Start = DateTimeOffset.UtcNow };
    }
    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, out var date) ? date : null;
    private sealed class AgendaResponse
    {
        public string? Title { get; set; }
        public string? Start { get; set; }
        public string? End { get; set; }
        public string? Reminder { get; set; }
    }
}
public sealed class NoteInterpreter : INoteInterpreter
{
    private readonly ILLMProvider _provider;

    public NoteInterpreter(ILLMProvider provider)
    {
        _provider = provider;
    }
    public async Task<S_Note> RefineNoteAsync(string title, string content, CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
Nettoie et synthétise la note suivante. Réponds uniquement avec le texte amélioré.
Titre: {{title}}
Contenu:
{{content}}
""";
        var refined = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        return new S_Note { Title = title, Content = refined.Content, Source = NoteSourceType.Generated, CreatedAt = DateTimeOffset.UtcNow };
    }
}
public sealed class ReportInterpreter : IReportInterpreter
{
    private readonly ILLMProvider _provider;

    public ReportInterpreter(ILLMProvider provider)
    {
        _provider = provider;
    }
    public async Task<ReportBuildResult> BuildReportAsync(ReportBuildRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
Construis une requête JSON {"query":"...","visualization":"table|chart"} pour un rapport: {{description}}
""";
        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        try
        {
            var report = JsonSerializer.Deserialize<ReportResponse>(JsonHelper.ExtractJson(response.Content), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (report is not null)
            {
                return new ReportBuildResult(
                    new S_ReportDefinition
                    {
                        ModuleId = request.ModuleId,
                        Name = request.Description,
                        QueryDefinition = report.Query ?? "select *",
                        Visualization = report.Visualization ?? request.PreferredVisualization
                    },
                    response.RawResponse ?? response.Content);
            }
        }
        catch (JsonException)
        {
        }
        return new ReportBuildResult(new S_ReportDefinition { ModuleId = request.ModuleId, Name = request.Description, QueryDefinition = "select *", Visualization = request.PreferredVisualization }, response.RawResponse ?? response.Content);
    }
    private sealed class ReportResponse
    {
        public string? Query { get; set; }
        public string? Visualization { get; set; }
    }
}
public sealed class VisionEngine : IAionVisionService
{
    private readonly HttpVisionProvider _visionProvider;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<VisionEngine> _logger;
    public VisionEngine(HttpVisionProvider visionProvider, IFileStorageService fileStorage, ILogger<VisionEngine> logger)
    {
        _visionProvider = visionProvider;
        _fileStorage = fileStorage;
        _logger = logger;
    }
    public async Task<S_VisionAnalysis> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = await _fileStorage.OpenAsync(request.FileId, cancellationToken).ConfigureAwait(false);
            if (stream is null)
            {
                _logger.LogWarning("Unable to open file {FileId} for vision analysis", request.FileId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open file {FileId}; continuing with remote call", request.FileId);
        }
        return await _visionProvider.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
