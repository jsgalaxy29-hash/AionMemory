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
public static class HttpClientNames
{
    public const string Llm = "aion-ai-llm";
    public const string Embeddings = "aion-ai-embeddings";
    public const string Transcription = "aion-ai-transcription";
    public const string Vision = "aion-ai-vision";
}
public sealed class HttpTextGenerationProvider : IChatModel
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<HttpTextGenerationProvider> _logger;
    private readonly IAiCallLogService _callLogService;
    public HttpTextGenerationProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<HttpTextGenerationProvider> logger, IAiCallLogService callLogService)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
        _callLogService = callLogService;
    }
    public async Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        const string operation = "chat";
        const string providerName = "Http";
        var stopwatch = Stopwatch.StartNew();
        var opts = _options.CurrentValue;
        var client = CreateClient(HttpClientNames.Llm, opts.LlmEndpoint ?? opts.BaseEndpoint);
        var status = AiCallStatus.Success;
        long? tokens = null;
        double? cost = null;
        try
        {
            if (client.BaseAddress is null)
            {
                _logger.LogWarning("LLM endpoint not configured; returning stub response");
                AiMetrics.RecordError(operation, providerName, opts.LlmModel);
                status = AiCallStatus.Inactive;
                return new LlmResponse($"[stub-llm] {prompt}", string.Empty, opts.LlmModel);
            }

            var payload = new
            {
                model = opts.LlmModel ?? opts.EmbeddingModel ?? "generic-llm",
                messages = new[] { new { role = "user", content = prompt } },
                max_tokens = opts.MaxTokens,
                temperature = opts.Temperature
            };

            var response = await AiHttpRetryPolicy.SendWithRetryAsync(
                client,
                () => new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
                },
                _logger,
                operation,
                providerName,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LLM call failed with status {Status}; returning stub", response.StatusCode);
                AiMetrics.RecordError(operation, providerName, opts.LlmModel);
                status = AiCallStatus.Fallback;
                return new LlmResponse($"[stub-llm-fallback] {prompt}", string.Empty, opts.LlmModel);
            }
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            AiMetrics.RecordUsageFromJson(json, operation, providerName, opts.LlmModel);
            (tokens, cost) = Observability.AiUsageParser.Extract(json);
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return new LlmResponse(content ?? string.Empty, json, opts.LlmModel);
        }
        catch (Exception)
        {
            status = AiCallStatus.Error;
            AiMetrics.RecordError(operation, providerName, opts.LlmModel);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            AiMetrics.RecordLatency(operation, providerName, opts.LlmModel, stopwatch.Elapsed);
            await _callLogService.LogAsync(
                new AiCallLogEntry(providerName, opts.LlmModel, operation, tokens, cost, stopwatch.Elapsed.TotalMilliseconds, status),
                cancellationToken).ConfigureAwait(false);
        }
    }
    private HttpClient CreateClient(string clientName, string? endpoint)
    {
        var client = _httpClientFactory.CreateClient(clientName);
        EnsureClientConfigured(client, endpoint, _options.CurrentValue);
        return client;
    }
    internal static void EnsureClientConfigured(HttpClient client, string? endpoint, AionAiOptions options)
    {
        AiHttpClientConfigurator.ConfigureClient(client, endpoint, options);
    }
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


public sealed class HttpEmbeddingProvider : IEmbeddingsModel
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<HttpEmbeddingProvider> _logger;
    private readonly IAiCallLogService _callLogService;

    public HttpEmbeddingProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<HttpEmbeddingProvider> logger, IAiCallLogService callLogService)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
        _callLogService = callLogService;
    }

    public async Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        const string operation = "embeddings";
        const string providerName = "Http";
        var stopwatch = Stopwatch.StartNew();
        var opts = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(HttpClientNames.Embeddings);
        HttpTextGenerationProvider.EnsureClientConfigured(client, opts.EmbeddingsEndpoint ?? opts.BaseEndpoint, opts);
        var status = AiCallStatus.Success;
        long? tokens = null;
        double? cost = null;

        try
        {
            if (client.BaseAddress is null)
            {
                _logger.LogWarning("Embedding endpoint not configured; returning stub vector");
                AiMetrics.RecordError(operation, providerName, opts.EmbeddingModel ?? opts.LlmModel);
                status = AiCallStatus.Inactive;
                var vector = Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
                return new EmbeddingResult(vector, opts.EmbeddingModel ?? opts.LlmModel);
            }

            var payload = new { model = opts.EmbeddingModel ?? opts.LlmModel ?? "generic-embedding", input = text };
            var response = await AiHttpRetryPolicy.SendWithRetryAsync(
                client,
                () => new HttpRequestMessage(HttpMethod.Post, "embeddings")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
                },
                _logger,
                operation,
                providerName,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Embedding call failed with status {Status}; returning stub", response.StatusCode);
                AiMetrics.RecordError(operation, providerName, opts.EmbeddingModel ?? opts.LlmModel);
                status = AiCallStatus.Fallback;
                var vector = Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
                return new EmbeddingResult(vector, opts.EmbeddingModel ?? opts.LlmModel);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            AiMetrics.RecordUsageFromJson(json, operation, providerName, opts.EmbeddingModel ?? opts.LlmModel);
            (tokens, cost) = Observability.AiUsageParser.Extract(json);
            using var doc = JsonDocument.Parse(json);
            var values = doc.RootElement.GetProperty("data")[0].GetProperty("embedding").EnumerateArray().Select(e => e.GetSingle()).ToArray();
            return new EmbeddingResult(values, opts.EmbeddingModel ?? opts.LlmModel, json);
        }
        catch (Exception)
        {
            status = AiCallStatus.Error;
            AiMetrics.RecordError(operation, providerName, opts.EmbeddingModel ?? opts.LlmModel);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            AiMetrics.RecordLatency(operation, providerName, opts.EmbeddingModel ?? opts.LlmModel, stopwatch.Elapsed);
            await _callLogService.LogAsync(
                new AiCallLogEntry(providerName, opts.EmbeddingModel ?? opts.LlmModel, operation, tokens, cost, stopwatch.Elapsed.TotalMilliseconds, status),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
public sealed class HttpAudioTranscriptionProvider : ITranscriptionModel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<HttpAudioTranscriptionProvider> _logger;
    private readonly IAiCallLogService _callLogService;
    public HttpAudioTranscriptionProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<HttpAudioTranscriptionProvider> logger, IAiCallLogService callLogService)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
        _callLogService = callLogService;
    }
    public async Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        const string operation = "transcription";
        const string providerName = "Http";
        var stopwatch = Stopwatch.StartNew();
        var opts = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(HttpClientNames.Transcription);
        HttpTextGenerationProvider.EnsureClientConfigured(client, opts.TranscriptionEndpoint ?? opts.BaseEndpoint, opts);
        var status = AiCallStatus.Success;
        long? tokens = null;
        double? cost = null;
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(audioStream), "file", fileName);
        content.Add(new StringContent(opts.TranscriptionModel ?? opts.LlmModel ?? "whisper"), "model");
        try
        {
            if (client.BaseAddress is null)
            {
                _logger.LogWarning("Transcription endpoint not configured; returning stub transcription");
                AiMetrics.RecordError(operation, providerName, opts.TranscriptionModel);
                status = AiCallStatus.Inactive;
                return new TranscriptionResult($"[stub-transcription] {fileName}", TimeSpan.Zero, opts.TranscriptionModel);
            }
            var response = await AiHttpRetryPolicy.SendWithRetryAsync(
                client,
                () => new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions")
                {
                    Content = content
                },
                _logger,
                operation,
                providerName,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Transcription call failed with status {Status}; returning stub", response.StatusCode);
                AiMetrics.RecordError(operation, providerName, opts.TranscriptionModel);
                status = AiCallStatus.Fallback;
                return new TranscriptionResult($"[stub-transcription] {fileName}", TimeSpan.Zero, opts.TranscriptionModel);
            }
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            AiMetrics.RecordUsageFromJson(json, operation, providerName, opts.TranscriptionModel);
            (tokens, cost) = Observability.AiUsageParser.Extract(json);
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
            return new TranscriptionResult(text, TimeSpan.Zero, opts.TranscriptionModel);
        }
        catch (Exception)
        {
            status = AiCallStatus.Error;
            AiMetrics.RecordError(operation, providerName, opts.TranscriptionModel);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            AiMetrics.RecordLatency(operation, providerName, opts.TranscriptionModel, stopwatch.Elapsed);
            await _callLogService.LogAsync(
                new AiCallLogEntry(providerName, opts.TranscriptionModel, operation, tokens, cost, stopwatch.Elapsed.TotalMilliseconds, status),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
public sealed class HttpVisionProvider : IVisionModel
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<HttpVisionProvider> _logger;
    private readonly IAiCallLogService _callLogService;
    public HttpVisionProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<HttpVisionProvider> logger, IAiCallLogService callLogService)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
        _callLogService = callLogService;
    }
    public async Task<S_VisionAnalysis> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        const string operation = "vision";
        const string providerName = "Http";
        var stopwatch = Stopwatch.StartNew();
        var opts = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(HttpClientNames.Vision);
        HttpTextGenerationProvider.EnsureClientConfigured(client, opts.VisionEndpoint ?? opts.BaseEndpoint, opts);
        var status = AiCallStatus.Success;
        long? tokens = null;
        double? cost = null;
        var payload = new { fileId = request.FileId, analysisType = request.AnalysisType.ToString(), model = request.Model ?? opts.VisionModel ?? "vision-generic" };
        try
        {
            if (client.BaseAddress is null)
            {
                _logger.LogWarning("Vision endpoint not configured; returning stub analysis");
                AiMetrics.RecordError(operation, providerName, request.Model ?? opts.VisionModel);
                status = AiCallStatus.Inactive;
                return BuildStub(request.FileId, request.AnalysisType, "Unconfigured vision endpoint");
            }
            using var response = await AiHttpRetryPolicy.SendWithRetryAsync(
                client,
                () => new HttpRequestMessage(HttpMethod.Post, "vision/analyze")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
                },
                _logger,
                operation,
                providerName,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Vision call failed with status {Status}; returning stub", response.StatusCode);
                AiMetrics.RecordError(operation, providerName, request.Model ?? opts.VisionModel);
                status = AiCallStatus.Fallback;
                return BuildStub(request.FileId, request.AnalysisType, "Vision call failed");
            }
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            AiMetrics.RecordUsageFromJson(json, operation, providerName, request.Model ?? opts.VisionModel);
            (tokens, cost) = Observability.AiUsageParser.Extract(json);
            return new S_VisionAnalysis
            {
                FileId = request.FileId,
                AnalysisType = request.AnalysisType,
                ResultJson = json
            };
        }
        catch (Exception)
        {
            status = AiCallStatus.Error;
            AiMetrics.RecordError(operation, providerName, request.Model ?? opts.VisionModel);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            AiMetrics.RecordLatency(operation, providerName, request.Model ?? opts.VisionModel, stopwatch.Elapsed);
            await _callLogService.LogAsync(
                new AiCallLogEntry(providerName, request.Model ?? opts.VisionModel, operation, tokens, cost, stopwatch.Elapsed.TotalMilliseconds, status),
                cancellationToken).ConfigureAwait(false);
        }
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
    private readonly IChatModel _provider;
    private readonly ILogger<IntentRecognizer> _logger;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly IOperationScopeFactory _operationScopeFactory;

    public IntentRecognizer(IChatModel provider, ILogger<IntentRecognizer> logger, IOptionsMonitor<AionAiOptions> options, IOperationScopeFactory operationScopeFactory)
    {
        _provider = provider;
        _logger = logger;
        _options = options;
        _operationScopeFactory = operationScopeFactory;
    }

    public async Task<IntentDetectionResult> DetectAsync(IntentDetectionRequest request, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var safePrompt = request.Input.ToSafeLogValue(options, _logger);
        using var operation = _operationScopeFactory.Start("AI.IntentDetection");
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Operation"] = "AI.IntentDetection",
            ["CorrelationId"] = operation.Context.CorrelationId,
            ["OperationId"] = operation.Context.OperationId,
            ["Prompt"] = safePrompt
        }) ?? NullScope.Instance;
        var stopwatch = Stopwatch.StartNew();

        var context = request.Context ?? new Dictionary<string, string?>();
        var contextLines = context.Count == 0
            ? "aucun contexte"
            : string.Join(", ", context.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        var prompt = string.Join("\n", new[]
        {
            "Tu es l'orchestrateur d'intentions AION. Identifie l'intention principale de l'utilisateur.",
            "Contraintes :",
            "- Réponds UNIQUEMENT avec un JSON compact sans texte additionnel.",
            $"- Schéma attendu: {StructuredJsonSchemas.Intent.Description}",
            $"- Intentions typiques: {IntentCatalog.PromptIntents}.",
            $"Entrée: \"{request.Input}\"",
            $"Contexte: {contextLines}",
            $"Locale: {request.Locale}"
        });

        StructuredJsonResult structuredJson;
        try
        {
            structuredJson = await StructuredJsonResponseHandler.GetValidJsonAsync(
                _provider,
                prompt,
                StructuredJsonSchemas.Intent,
                _logger,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested && oce.CancellationToken == cancellationToken)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Intent detection failed after {ElapsedMs}ms ({Prompt})", stopwatch.Elapsed.TotalMilliseconds, safePrompt);
            return BuildFallback(request.Input, null, ex.Message, 0.05);
        }

        if (structuredJson.IsValid && TryParseIntent(structuredJson.Json, out var parsed))
        {
            _logger.LogInformation("Intent detection parsed response in {ElapsedMs}ms (confidence={Confidence:F2})", stopwatch.Elapsed.TotalMilliseconds, parsed.Confidence);
            return parsed with { RawResponse = structuredJson.RawResponse ?? structuredJson.Json ?? string.Empty };
        }

        _logger.LogWarning("Intent parsing failed after {ElapsedMs}ms ({Prompt})", stopwatch.Elapsed.TotalMilliseconds, safePrompt);
        return BuildFallback(request.Input, structuredJson.RawResponse ?? structuredJson.Json);
    }
    private bool TryParseIntent(string? json, out IntentDetectionResult result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var intent = root.TryGetProperty("intent", out var intentProp) ? intentProp.GetString() : null;
            intent = string.IsNullOrWhiteSpace(intent) ? null : intent.Trim();
            var confidence = root.TryGetProperty("confidence", out var confidenceProp) ? confidenceProp.GetDouble() : 0.5;
            confidence = double.IsNaN(confidence) || double.IsInfinity(confidence) ? 0.5 : Math.Clamp(confidence, 0d, 1d);
            Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("parameters", out var parametersProp) && parametersProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var parameter in parametersProp.EnumerateObject())
                {
                    parameters[parameter.Name] = parameter.Value.ValueKind == JsonValueKind.String
                        ? parameter.Value.GetString() ?? string.Empty
                        : parameter.Value.GetRawText();
                }
            }

            if (intent is null)
            {
                return false;
            }

            var normalizedIntent = IntentCatalog.Normalize(intent);
            if (normalizedIntent.Name != intent)
            {
                parameters["raw_intent"] = intent;
            }

            if (normalizedIntent.Name == IntentCatalog.Unknown && !IntentCatalog.IsUnknownName(intent))
            {
                return false;
            }

            result = new IntentDetectionResult(normalizedIntent.Name, parameters, confidence, json);
            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Unable to parse intent JSON");
            return false;
        }
    }

    private static IntentDetectionResult BuildFallback(string input, string? rawResponse, string? error = null, double confidence = 0.1)
    {
        var guessed = IntentHeuristics.Detect(input);
        var parameters = new Dictionary<string, string>
        {
            ["raw"] = input,
            ["fallback"] = "heuristic"
        };
        if (!string.IsNullOrWhiteSpace(error))
        {
            parameters["error"] = error;
        }

        return new IntentDetectionResult(guessed.Name, parameters, confidence, rawResponse);
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
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
public sealed class CrudInterpreter : ICrudInterpreter
{
    private readonly IChatModel _provider;
    private readonly ILogger<CrudInterpreter> _logger;
    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase) { "create", "update", "delete", "query" };

    public CrudInterpreter(IChatModel provider, ILogger<CrudInterpreter> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<CrudInterpretation> GenerateQueryAsync(CrudQueryRequest request, CancellationToken cancellationToken = default)
    {
        var fieldDescriptions = request.Module.EntityTypes
            .SelectMany(e => e.Fields.Select(f => $"{e.Name}.{f.Name}:{f.DataType}"));
        var prompt = $@"Tu traduis les instructions utilisateur en opérations CRUD structurées.
Renvoie UNIQUEMENT un JSON respectant {{""action"":""create|update|delete|query"",""filters"":{{}},""payload"":{{}}}}.
Module: {request.Module.Name}
Champs disponibles: {string.Join(", ", fieldDescriptions)}
Requête: {request.Intent}";

        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        var cleaned = JsonHelper.ExtractJson(response.Content);

        if (TryParseCrud(cleaned, out var interpretation))
        {
            return interpretation with { RawResponse = response.RawResponse ?? cleaned };
        }

        _logger.LogWarning("Unable to parse CRUD interpretation, returning fallback");
        return new CrudInterpretation("query", new Dictionary<string, string?> { ["raw"] = request.Intent }, new Dictionary<string, string?>(), response.RawResponse ?? cleaned);
    }

    private bool TryParseCrud(string cleaned, out CrudInterpretation interpretation)
    {
        interpretation = default;
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                ? doc.RootElement[0]
                : doc.RootElement;
            var action = root.TryGetProperty("action", out var actionProperty) ? actionProperty.GetString() ?? "query" : "query";
            action = NormalizeAction(action);
            var filters = ExtractStringDictionary(root, "filters");
            var payload = ExtractStringDictionary(root, "payload");
            interpretation = new CrudInterpretation(action, filters, payload, cleaned);
            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "CRUD interpretation parse error");
            return false;
        }
    }

    private static string NormalizeAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return "query";
        }

        var normalized = action.Trim();
        return AllowedActions.Contains(normalized) ? normalized : "query";
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
    private readonly IChatModel _provider;

    public AgendaInterpreter(IChatModel provider)
    {
        _provider = provider;
    }
    public async Task<S_Event> CreateEventAsync(string input, CancellationToken cancellationToken = default)
    {
        var prompt = $@"Génère un événement JSON {{""title"":"""",""start"":""ISO"",""end"":""ISO|null"",""reminder"":""ISO|null""}} pour: {input}";
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
    private readonly IChatModel _provider;

    public NoteInterpreter(IChatModel provider)
    {
        _provider = provider;
    }
    public async Task<S_Note> RefineNoteAsync(string title, string content, CancellationToken cancellationToken = default)
    {
        var prompt = $@"Nettoie et synthétise la note suivante. Réponds uniquement avec le texte amélioré.
Titre: {title}
Contenu:
{content}";
        var refined = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        return new S_Note { Title = title, Content = refined.Content, Source = NoteSourceType.Generated, CreatedAt = DateTimeOffset.UtcNow };
    }
}
public sealed class ReportInterpreter : IReportInterpreter
{
    private static readonly HashSet<string> AllowedVisualizations = new(StringComparer.OrdinalIgnoreCase) { "table", "chart", "number", "list" };
    private readonly IChatModel _provider;
    private readonly ILogger<HttpTextGenerationProvider> _logger;

    public ReportInterpreter(IChatModel provider, ILogger<HttpTextGenerationProvider> logger)
    {
        _provider = provider;
        _logger = logger;
    }
    public async Task<ReportBuildResult> BuildReportAsync(ReportBuildRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = $@"Génère la définition d'un rapport AION. Réponds UNIQUEMENT en JSON compact suivant {{""query"":""SQL ou pseudo-SQL"",""visualization"":""table|chart|number|list""}}.
Description: {request.Description}
ModuleId: {request.ModuleId}
Préférence de visualisation: {request.PreferredVisualization ?? "none"}";
        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        var cleaned = JsonHelper.ExtractJson(response.Content);
        if (TryParseReport(request, cleaned, out var parsed))
        {
            return parsed with { RawResponse = response.RawResponse ?? cleaned };
        }

        return new ReportBuildResult(new S_ReportDefinition { ModuleId = request.ModuleId, Name = request.Description, QueryDefinition = "select *", Visualization = request.PreferredVisualization }, response.RawResponse ?? response.Content);
    }

    private bool TryParseReport(ReportBuildRequest request, string cleaned, out ReportBuildResult result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                ? doc.RootElement[0]
                : doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var content = choices[0].GetProperty("message").GetProperty("content").GetString();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    return TryParseReport(request, JsonHelper.ExtractJson(content), out result);
                }
            }

            var query = root.TryGetProperty("query", out var queryProp) ? queryProp.GetString() : null;
            var visualization = root.TryGetProperty("visualization", out var vizProp) ? vizProp.GetString() : null;
            visualization = NormalizeVisualization(visualization, request.PreferredVisualization);
            var reportDefinition = new S_ReportDefinition
            {
                ModuleId = request.ModuleId,
                Name = request.Description,
                QueryDefinition = string.IsNullOrWhiteSpace(query) ? "select *" : query,
                Visualization = visualization
            };

            result = new ReportBuildResult(reportDefinition, cleaned);
            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse report response");
            return false;
        }
    }
    private sealed class ReportResponse
    {
        public string? Query { get; set; }
        public string? Visualization { get; set; }
    }

    private static string? NormalizeVisualization(string? visualization, string? preferred)
    {
        if (string.IsNullOrWhiteSpace(visualization))
        {
            return preferred;
        }

        var normalized = visualization.Trim();
        return AllowedVisualizations.Contains(normalized) ? normalized : preferred;
    }
}
public sealed class VisionEngine : IVisionModel, IAionVisionService
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
