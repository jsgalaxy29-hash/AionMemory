using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Aion.Domain;
using Aion.AI.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aion.AI;

/// <summary>
/// Provider générique prêt à être branché sur un endpoint LLM compatible (OpenAI-like).
/// Les appels réseau sont encapsulés dans HttpClientFactory pour être facilement testés
/// et remplacés.
/// </summary>
public sealed class DefaultAionAiProvider : ILLMProvider, IEmbeddingProvider, IAudioTranscriptionProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AionAiOptions _options;
    private readonly ILogger<DefaultAionAiProvider> _logger;
    private readonly IAiCallLogService _callLogService;

    public DefaultAionAiProvider(IHttpClientFactory httpClientFactory, IOptions<AionAiOptions> options, ILogger<DefaultAionAiProvider> logger, IAiCallLogService? callLogService = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
        _callLogService = callLogService ?? NoopAiCallLogService.Instance;
    }

    public async Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var endpoint = _options.LlmEndpoint ?? _options.BaseEndpoint;
        var stopwatch = Stopwatch.StartNew();
        var status = AiCallStatus.Success;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogWarning("Endpoint IA non configuré, retour d'une réponse stub.");
            status = AiCallStatus.Inactive;
            var content1 = $"[stub] {_options.LlmModel ?? "model"}: {prompt}";
            stopwatch.Stop();
            await _callLogService.LogAsync(new AiCallLogEntry("Default", _options.LlmModel, "chat", null, null, stopwatch.Elapsed.TotalMilliseconds, status), cancellationToken).ConfigureAwait(false);
            return new LlmResponse(content1, content1, _options.LlmModel);
        }

        var client = CreateClient(endpoint, includeOrganizationHeader: true);

        var payload = new { model = _options.LlmModel ?? "generic-llm", messages = new[] { new { role = "user", content = prompt } } };
        var response = await AiHttpRetryPolicy.SendWithRetryAsync(
            client,
            () => new HttpRequestMessage(HttpMethod.Post, BuildUri(client, "chat/completions"))
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
            },
            _logger,
            "chat",
            "Default",
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Appel IA non réussi ({Status}) - bascule en mode stub", response.StatusCode);
            status = AiCallStatus.Fallback;
            var content2 = $"[stub-fallback] {prompt}";
            stopwatch.Stop();
            await _callLogService.LogAsync(new AiCallLogEntry("Default", _options.LlmModel, "chat", null, null, stopwatch.Elapsed.TotalMilliseconds, status), cancellationToken).ConfigureAwait(false);
            return new LlmResponse(content2, content2, _options.LlmModel);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var usage = AiUsageParser.Extract(json);
        stopwatch.Stop();
        await _callLogService.LogAsync(new AiCallLogEntry("Default", _options.LlmModel, "chat", usage.tokens, usage.cost, stopwatch.Elapsed.TotalMilliseconds, status), cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return new LlmResponse(content ?? string.Empty, json, _options.LlmModel);
    }

    public async Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var endpoint = _options.EmbeddingsEndpoint ?? _options.BaseEndpoint;
        var stopwatch = Stopwatch.StartNew();
        var status = AiCallStatus.Success;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogWarning("Embedding endpoint not configured, returning deterministic stub vector.");
            status = AiCallStatus.Inactive;
            var vector = Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
            stopwatch.Stop();
            await _callLogService.LogAsync(new AiCallLogEntry("Default", _options.EmbeddingModel ?? _options.LlmModel, "embeddings", null, null, stopwatch.Elapsed.TotalMilliseconds, status), cancellationToken).ConfigureAwait(false);
            return new EmbeddingResult(vector, _options.EmbeddingModel ?? _options.LlmModel);
        }

        var client = CreateClient(endpoint, includeOrganizationHeader: true);

        var payload = new { model = _options.EmbeddingModel ?? _options.LlmModel ?? "generic-embedding", input = text };
        var response = await AiHttpRetryPolicy.SendWithRetryAsync(
            client,
            () => new HttpRequestMessage(HttpMethod.Post, BuildUri(client, "embeddings"))
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
            },
            _logger,
            "embeddings",
            "Default",
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var usage = AiUsageParser.Extract(json);
        stopwatch.Stop();
        await _callLogService.LogAsync(new AiCallLogEntry("Default", _options.EmbeddingModel ?? _options.LlmModel, "embeddings", usage.tokens, usage.cost, stopwatch.Elapsed.TotalMilliseconds, status), cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var values = doc.RootElement.GetProperty("data")[0].GetProperty("embedding").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        return new EmbeddingResult(values, _options.EmbeddingModel ?? _options.LlmModel, json);
    }

    public async Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        var endpoint = _options.TranscriptionEndpoint ?? _options.BaseEndpoint;
        var stopwatch = Stopwatch.StartNew();
        var status = AiCallStatus.Success;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _logger.LogWarning("Transcription endpoint not configured, returning stub result.");
            status = AiCallStatus.Inactive;
            stopwatch.Stop();
            await _callLogService.LogAsync(new AiCallLogEntry("Default", _options.TranscriptionModel ?? _options.LlmModel, "transcription", null, null, stopwatch.Elapsed.TotalMilliseconds, status), cancellationToken).ConfigureAwait(false);
            return new TranscriptionResult("Transcription stub", TimeSpan.Zero, _options.TranscriptionModel ?? _options.LlmModel);
        }

        var client = CreateClient(endpoint, includeOrganizationHeader: true);

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(audioStream), "file", fileName);
        content.Add(new StringContent(_options.TranscriptionModel ?? _options.LlmModel ?? "generic-transcription"), "model");

        var response = await AiHttpRetryPolicy.SendWithRetryAsync(
            client,
            () => new HttpRequestMessage(HttpMethod.Post, BuildUri(client, "audio/transcriptions"))
            {
                Content = content
            },
            _logger,
            "transcription",
            "Default",
            cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var usage = AiUsageParser.Extract(json);
        stopwatch.Stop();
        await _callLogService.LogAsync(new AiCallLogEntry("Default", _options.TranscriptionModel ?? _options.LlmModel, "transcription", usage.tokens, usage.cost, stopwatch.Elapsed.TotalMilliseconds, status), cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
        return new TranscriptionResult(text, TimeSpan.Zero, _options.TranscriptionModel ?? _options.LlmModel);
    }

    private HttpClient CreateClient(string? endpoint, bool includeOrganizationHeader)
    {
        var client = _httpClientFactory.CreateClient("aion-ai");
        AiHttpClientConfigurator.ConfigureClient(client, endpoint, _options, includeOrganizationHeader);
        return client;
    }

    private static Uri BuildUri(HttpClient client, string relativePath)
    {
        if (client.BaseAddress is null)
        {
            return new Uri(relativePath, UriKind.RelativeOrAbsolute);
        }

        return new Uri(client.BaseAddress, relativePath);
    }
}

public sealed class DefaultIntentRecognizer : IIntentDetector
{
    private readonly ILLMProvider _provider;

    public DefaultIntentRecognizer(ILLMProvider provider)
    {
        _provider = provider;
    }

    public async Task<IntentDetectionResult> DetectAsync(IntentDetectionRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = $"Analyse l'intention utilisateur pour: {request.Input}";
        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        return new IntentDetectionResult(IntentCatalog.Chat, new Dictionary<string, string> { ["prompt"] = request.Input }, 0.5, response.RawResponse ?? response.Content);
    }
}

public sealed class DefaultModuleDesigner : IModuleDesigner
{
    private readonly ILLMProvider _provider;

    public string? LastGeneratedJson { get; private set; }

    public DefaultModuleDesigner(ILLMProvider provider)
    {
        _provider = provider;
    }

    public async Task<ModuleDesignResult> GenerateModuleAsync(ModuleDesignRequest request, CancellationToken cancellationToken = default)
    {
        var generationPrompt = $"Propose un module AION (entités/champs) pour: {request.Prompt}. La réponse doit être un JSON valide suivant ce schéma: {StructuredJsonSchemas.LegacyModuleDesign.Description}";
        var structured = await StructuredJsonResponseHandler.GetValidJsonAsync(
            _provider,
            generationPrompt,
            StructuredJsonSchemas.LegacyModuleDesign,
            NullLogger<DefaultModuleDesigner>.Instance,
            cancellationToken).ConfigureAwait(false);
        LastGeneratedJson = structured.Json?.Trim();

        if (structured.IsValid && !string.IsNullOrWhiteSpace(LastGeneratedJson))
        {
            try
            {
                var parsedModule = JsonSerializer.Deserialize<S_Module>(LastGeneratedJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (parsedModule is not null)
                {
                    return new ModuleDesignResult(parsedModule, LastGeneratedJson);
                }
            }
            catch (JsonException)
            {
                // Ignored: we fallback to a minimal module below.
            }
        }

        var fallback = new S_Module
        {
            Name = string.IsNullOrWhiteSpace(request.ModuleNameHint) ? (string.IsNullOrWhiteSpace(request.Prompt) ? "Module IA" : request.Prompt) : request.ModuleNameHint,
            Description = "Module généré automatiquement",
            EntityTypes = new List<S_EntityType>()
            {
                new()
                {
                    Name = "Item",
                    PluralName = "Items",
                    Fields = new List<S_Field>
                    {
                        new() { Name = "Titre", Label = "Titre", DataType = FieldDataType.Text, IsRequired = true }
                    }
                }
            }
        };
        return new ModuleDesignResult(fallback, LastGeneratedJson ?? string.Empty);
    }
}
