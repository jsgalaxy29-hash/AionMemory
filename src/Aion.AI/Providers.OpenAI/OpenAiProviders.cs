using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Aion.Domain;
using Aion.AI.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.AI;

/// <summary>
/// Provider spécialisé pour OpenAI (ou compatible) avec gestion basique du rate-limit.
/// </summary>
public sealed class OpenAiTextGenerationProvider : IChatModel
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<OpenAiTextGenerationProvider> _logger;
    private readonly IAiCallLogService _callLogService;

    public OpenAiTextGenerationProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<OpenAiTextGenerationProvider> logger, IAiCallLogService callLogService)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
        _callLogService = callLogService;
    }

    public async Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        const string operation = "chat";
        const string providerName = "OpenAI";
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
                _logger.LogWarning("OpenAI endpoint not configured; returning stub response");
                AiMetrics.RecordError(operation, providerName, opts.LlmModel);
                status = AiCallStatus.Inactive;
                var content3 = $"[openai-stub] {prompt}";
                return new LlmResponse(content3, content3, opts.LlmModel);
            }

            var payload = new
            {
                model = opts.LlmModel ?? "gpt-4o-mini",
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
                _logger.LogWarning("OpenAI call failed with status {Status}; returning stub", response.StatusCode);
                AiMetrics.RecordError(operation, providerName, opts.LlmModel);
                status = AiCallStatus.Fallback;
                var content4 = $"[openai-fallback] {prompt}";
                return new LlmResponse(content4, content4, opts.LlmModel);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            AiMetrics.RecordUsageFromJson(json, operation, providerName, opts.LlmModel);
            (tokens, cost) = Observability.AiUsageParser.Extract(json);
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
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

    internal HttpClient BuildClient(string clientName, string? endpoint)
    {
        var opts = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(clientName);

        AiHttpClientConfigurator.ConfigureClient(client, endpoint ?? opts.BaseEndpoint ?? "https://api.openai.com/v1/", opts, includeOrganizationHeader: true);

        return client;
    }
}

public sealed class OpenAiEmbeddingProvider : IEmbeddingsModel
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenAiTextGenerationProvider _clientBuilder;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<OpenAiEmbeddingProvider> _logger;
    private readonly IAiCallLogService _callLogService;

    public OpenAiEmbeddingProvider(OpenAiTextGenerationProvider clientBuilder, IOptionsMonitor<AionAiOptions> options, ILogger<OpenAiEmbeddingProvider> logger, IAiCallLogService callLogService)
    {
        _clientBuilder = clientBuilder;
        _options = options;
        _logger = logger;
        _callLogService = callLogService;
    }

    public async Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        const string operation = "embeddings";
        const string providerName = "OpenAI";
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
                _logger.LogWarning("OpenAI embeddings endpoint not configured; returning stub");
                AiMetrics.RecordError(operation, providerName, opts.EmbeddingModel ?? opts.LlmModel);
                status = AiCallStatus.Inactive;
                var vector1 = Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
                return new EmbeddingResult(vector1, opts.EmbeddingModel ?? opts.LlmModel);
            }

            var payload = new { model = opts.EmbeddingModel ?? "text-embedding-3-small", input = text };
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
                _logger.LogWarning("OpenAI embeddings failed with status {Status}; returning stub", response.StatusCode);
                AiMetrics.RecordError(operation, providerName, opts.EmbeddingModel ?? opts.LlmModel);
                status = AiCallStatus.Fallback;
                var vector2 = Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
                return new EmbeddingResult(vector2, opts.EmbeddingModel ?? opts.LlmModel);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            AiMetrics.RecordUsageFromJson(json, operation, providerName, opts.EmbeddingModel ?? opts.LlmModel);
            (tokens, cost) = Observability.AiUsageParser.Extract(json);
            using var doc = JsonDocument.Parse(json);
            var vector = doc.RootElement.GetProperty("data")[0].GetProperty("embedding").EnumerateArray().Select(e => e.GetSingle()).ToArray();
            return new EmbeddingResult(vector, opts.EmbeddingModel ?? opts.LlmModel, json);
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

public sealed class OpenAiAudioTranscriptionProvider : ITranscriptionModel
{
    private readonly OpenAiTextGenerationProvider _clientBuilder;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<OpenAiAudioTranscriptionProvider> _logger;
    private readonly IAiCallLogService _callLogService;

    public OpenAiAudioTranscriptionProvider(OpenAiTextGenerationProvider clientBuilder, IOptionsMonitor<AionAiOptions> options, ILogger<OpenAiAudioTranscriptionProvider> logger, IAiCallLogService callLogService)
    {
        _clientBuilder = clientBuilder;
        _options = options;
        _logger = logger;
        _callLogService = callLogService;
    }

    public async Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        const string operation = "transcription";
        const string providerName = "OpenAI";
        var stopwatch = Stopwatch.StartNew();
        var opts = _options.CurrentValue;
        var client = _clientBuilder.BuildClient(HttpClientNames.Transcription, opts.TranscriptionEndpoint ?? opts.BaseEndpoint);
        var status = AiCallStatus.Success;
        long? tokens = null;
        double? cost = null;

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(audioStream), "file", fileName);
        content.Add(new StringContent(opts.TranscriptionModel ?? "whisper-1"), "model");

        try
        {
            if (client.BaseAddress is null)
            {
                _logger.LogWarning("OpenAI transcription endpoint not configured; returning stub");
                AiMetrics.RecordError(operation, providerName, opts.TranscriptionModel ?? "whisper-1");
                status = AiCallStatus.Inactive;
                return new TranscriptionResult("[openai-stub-transcription]", TimeSpan.Zero, opts.TranscriptionModel ?? "whisper-1");
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
                _logger.LogWarning("OpenAI transcription failed with status {Status}; returning stub", response.StatusCode);
                AiMetrics.RecordError(operation, providerName, opts.TranscriptionModel ?? "whisper-1");
                status = AiCallStatus.Fallback;
                return new TranscriptionResult("[openai-fallback-transcription]", TimeSpan.Zero, opts.TranscriptionModel ?? "whisper-1");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            AiMetrics.RecordUsageFromJson(json, operation, providerName, opts.TranscriptionModel ?? "whisper-1");
            (tokens, cost) = Observability.AiUsageParser.Extract(json);
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
            return new TranscriptionResult(text, TimeSpan.Zero, opts.TranscriptionModel ?? "whisper-1");
        }
        catch (Exception)
        {
            status = AiCallStatus.Error;
            AiMetrics.RecordError(operation, providerName, opts.TranscriptionModel ?? "whisper-1");
            throw;
        }
        finally
        {
            stopwatch.Stop();
            AiMetrics.RecordLatency(operation, providerName, opts.TranscriptionModel ?? "whisper-1", stopwatch.Elapsed);
            await _callLogService.LogAsync(
                new AiCallLogEntry(providerName, opts.TranscriptionModel ?? "whisper-1", operation, tokens, cost, stopwatch.Elapsed.TotalMilliseconds, status),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
