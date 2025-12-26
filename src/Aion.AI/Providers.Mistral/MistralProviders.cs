using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Linq;
using Aion.Domain;
using Aion.AI.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.AI;

public sealed class MistralTextGenerationProvider : IChatModel
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<MistralTextGenerationProvider> _logger;
    private readonly IAiCallLogService _callLogService;

    public MistralTextGenerationProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<MistralTextGenerationProvider> logger, IAiCallLogService callLogService)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
        _callLogService = callLogService;
    }

    public async Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        const string operation = "chat";
        const string providerName = "Mistral";
        var stopwatch = Stopwatch.StartNew();
        var opts = _options.CurrentValue;
        var client = BuildClient(HttpClientNames.Llm, opts.LlmEndpoint ?? opts.BaseEndpoint);
        var status = AiCallStatus.Success;
        long? tokens = null;
        double? cost = null;

        try
        {
            if (client.BaseAddress is null)
            {
                _logger.LogWarning("Mistral endpoint not configured; returning stub response");
                AiMetrics.RecordError(operation, providerName, opts.LlmModel);
                status = AiCallStatus.Inactive;
                var content = $"[mistral-stub] {prompt}";
                return new LlmResponse(content, content, opts.LlmModel);
            }

            var payload = new
            {
                model = opts.LlmModel ?? "mistral-small-latest",
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
                _logger.LogWarning("Mistral call failed with status {Status}; returning stub", response.StatusCode);
                AiMetrics.RecordError(operation, providerName, opts.LlmModel);
                status = AiCallStatus.Fallback;
                var fallback = $"[mistral-fallback] {prompt}";
                return new LlmResponse(fallback, fallback, opts.LlmModel);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            AiMetrics.RecordUsageFromJson(json, operation, providerName, opts.LlmModel);
            (tokens, cost) = Observability.AiUsageParser.Extract(json);
            using var doc = JsonDocument.Parse(json);
            var contentResponse = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return new LlmResponse(contentResponse ?? string.Empty, json, opts.LlmModel);
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

    internal HttpClient BuildClient(string clientName, string? endpoint)
    {
        var opts = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(clientName);
        var resolvedEndpoint = endpoint ?? opts.BaseEndpoint ?? "https://api.mistral.ai/v1/";

        AiHttpClientConfigurator.ConfigureClient(client, resolvedEndpoint, opts);

        return client;
    }
}

public sealed class MistralEmbeddingProvider : IEmbeddingsModel
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly MistralTextGenerationProvider _clientBuilder;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<MistralEmbeddingProvider> _logger;
    private readonly IAiCallLogService _callLogService;

    public MistralEmbeddingProvider(MistralTextGenerationProvider clientBuilder, IOptionsMonitor<AionAiOptions> options, ILogger<MistralEmbeddingProvider> logger, IAiCallLogService callLogService)
    {
        _clientBuilder = clientBuilder;
        _options = options;
        _logger = logger;
        _callLogService = callLogService;
    }

    public async Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        const string operation = "embeddings";
        const string providerName = "Mistral";
        var stopwatch = Stopwatch.StartNew();
        var opts = _options.CurrentValue;
        var client = _clientBuilder.BuildClient(HttpClientNames.Embeddings, opts.EmbeddingsEndpoint ?? opts.BaseEndpoint);
        var status = AiCallStatus.Success;
        long? tokens = null;
        double? cost = null;

        try
        {
            if (client.BaseAddress is null)
            {
                _logger.LogWarning("Mistral embeddings endpoint not configured; returning stub vector");
                AiMetrics.RecordError(operation, providerName, opts.EmbeddingModel ?? opts.LlmModel);
                status = AiCallStatus.Inactive;
                var vector = Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
                return new EmbeddingResult(vector, opts.EmbeddingModel ?? opts.LlmModel);
            }

            var payload = new { model = opts.EmbeddingModel ?? "mistral-embed", input = text };
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
                _logger.LogWarning("Mistral embeddings failed with status {Status}; returning stub", response.StatusCode);
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

public sealed class MistralAudioTranscriptionProvider : ITranscriptionModel
{
    private readonly MistralTextGenerationProvider _clientBuilder;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<MistralAudioTranscriptionProvider> _logger;
    private readonly IAiCallLogService _callLogService;

    public MistralAudioTranscriptionProvider(MistralTextGenerationProvider clientBuilder, IOptionsMonitor<AionAiOptions> options, ILogger<MistralAudioTranscriptionProvider> logger, IAiCallLogService callLogService)
    {
        _clientBuilder = clientBuilder;
        _options = options;
        _logger = logger;
        _callLogService = callLogService;
    }

    public async Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        const string operation = "transcription";
        const string providerName = "Mistral";
        var stopwatch = Stopwatch.StartNew();
        var opts = _options.CurrentValue;
        var client = _clientBuilder.BuildClient(HttpClientNames.Transcription, opts.TranscriptionEndpoint ?? opts.BaseEndpoint);
        var status = AiCallStatus.Success;
        long? tokens = null;
        double? cost = null;

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(audioStream), "file", fileName);
        content.Add(new StringContent(opts.TranscriptionModel ?? opts.LlmModel ?? "whisper-large-v3"), "model");

        try
        {
            if (client.BaseAddress is null)
            {
                _logger.LogWarning("Mistral transcription endpoint not configured; returning stub");
                AiMetrics.RecordError(operation, providerName, opts.TranscriptionModel ?? opts.LlmModel);
                status = AiCallStatus.Inactive;
                return new TranscriptionResult($"[mistral-stub-transcription] {fileName}", TimeSpan.Zero, opts.TranscriptionModel ?? opts.LlmModel);
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
                _logger.LogWarning("Mistral transcription failed with status {Status}; returning stub", response.StatusCode);
                AiMetrics.RecordError(operation, providerName, opts.TranscriptionModel ?? opts.LlmModel);
                status = AiCallStatus.Fallback;
                return new TranscriptionResult($"[mistral-fallback-transcription] {fileName}", TimeSpan.Zero, opts.TranscriptionModel ?? opts.LlmModel);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            AiMetrics.RecordUsageFromJson(json, operation, providerName, opts.TranscriptionModel ?? opts.LlmModel);
            (tokens, cost) = Observability.AiUsageParser.Extract(json);
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
            return new TranscriptionResult(text, TimeSpan.Zero, opts.TranscriptionModel ?? opts.LlmModel);
        }
        catch (Exception)
        {
            status = AiCallStatus.Error;
            AiMetrics.RecordError(operation, providerName, opts.TranscriptionModel ?? opts.LlmModel);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            AiMetrics.RecordLatency(operation, providerName, opts.TranscriptionModel ?? opts.LlmModel, stopwatch.Elapsed);
            await _callLogService.LogAsync(
                new AiCallLogEntry(providerName, opts.TranscriptionModel ?? opts.LlmModel, operation, tokens, cost, stopwatch.Elapsed.TotalMilliseconds, status),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
