using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aion.Domain;
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

    public OpenAiTextGenerationProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<OpenAiTextGenerationProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue;
        var client = BuildClient(HttpClientNames.Llm, opts.LlmEndpoint ?? opts.BaseEndpoint);

        if (client.BaseAddress is null)
        {
            _logger.LogWarning("OpenAI endpoint not configured; returning stub response");
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

        var response = await OpenAiRetryHelper.SendWithRetryAsync(client, () => new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        }, _logger, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenAI call failed with status {Status}; returning stub", response.StatusCode);
            var content4 = $"[openai-fallback] {prompt}";
            return new LlmResponse(content4, content4, opts.LlmModel);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return new LlmResponse(content ?? string.Empty, json, opts.LlmModel);
    }

    internal HttpClient BuildClient(string clientName, string? endpoint)
    {
        var opts = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(clientName);

        if (client.BaseAddress is null && Uri.TryCreate(endpoint ?? opts.BaseEndpoint ?? "https://api.openai.com/v1/", UriKind.Absolute, out var uri))
        {
            client.BaseAddress = uri;
        }

        client.Timeout = opts.RequestTimeout;

        if (!string.IsNullOrWhiteSpace(opts.ApiKey) && client.DefaultRequestHeaders.Authorization is null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);
        }

        if (!string.IsNullOrWhiteSpace(opts.Organization))
        {
            client.DefaultRequestHeaders.Remove("OpenAI-Organization");
            client.DefaultRequestHeaders.Add("OpenAI-Organization", opts.Organization);
        }

        foreach (var header in opts.DefaultHeaders)
        {
            client.DefaultRequestHeaders.Remove(header.Key);
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        return client;
    }
}

public sealed class OpenAiEmbeddingProvider : IEmbeddingsModel
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenAiTextGenerationProvider _clientBuilder;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<OpenAiEmbeddingProvider> _logger;

    public OpenAiEmbeddingProvider(OpenAiTextGenerationProvider clientBuilder, IOptionsMonitor<AionAiOptions> options, ILogger<OpenAiEmbeddingProvider> logger)
    {
        _clientBuilder = clientBuilder;
        _options = options;
        _logger = logger;
    }

    public async Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue;
        var client = _clientBuilder.BuildClient(HttpClientNames.Embeddings, opts.EmbeddingsEndpoint ?? opts.BaseEndpoint);

        if (client.BaseAddress is null)
        {
            _logger.LogWarning("OpenAI embeddings endpoint not configured; returning stub");
            var vector1 = Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
            return new EmbeddingResult(vector1, opts.EmbeddingModel ?? opts.LlmModel);
        }

        var payload = new { model = opts.EmbeddingModel ?? "text-embedding-3-small", input = text };
        var response = await OpenAiRetryHelper.SendWithRetryAsync(client, () => new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        }, _logger, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenAI embeddings failed with status {Status}; returning stub", response.StatusCode);
            var vector2 = Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
            return new EmbeddingResult(vector2, opts.EmbeddingModel ?? opts.LlmModel);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var vector = doc.RootElement.GetProperty("data")[0].GetProperty("embedding").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        return new EmbeddingResult(vector, opts.EmbeddingModel ?? opts.LlmModel, json);
    }
}

public sealed class OpenAiAudioTranscriptionProvider : ITranscriptionModel
{
    private readonly OpenAiTextGenerationProvider _clientBuilder;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<OpenAiAudioTranscriptionProvider> _logger;

    public OpenAiAudioTranscriptionProvider(OpenAiTextGenerationProvider clientBuilder, IOptionsMonitor<AionAiOptions> options, ILogger<OpenAiAudioTranscriptionProvider> logger)
    {
        _clientBuilder = clientBuilder;
        _options = options;
        _logger = logger;
    }

    public async Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue;
        var client = _clientBuilder.BuildClient(HttpClientNames.Transcription, opts.TranscriptionEndpoint ?? opts.BaseEndpoint);

        if (client.BaseAddress is null)
        {
            _logger.LogWarning("OpenAI transcription endpoint not configured; returning stub");
            return new TranscriptionResult("[openai-stub-transcription]", TimeSpan.Zero, opts.TranscriptionModel ?? "whisper-1");
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(audioStream), "file", fileName);
        content.Add(new StringContent(opts.TranscriptionModel ?? "whisper-1"), "model");

        var response = await OpenAiRetryHelper.SendWithRetryAsync(client, () => new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions")
        {
            Content = content
        }, _logger, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenAI transcription failed with status {Status}; returning stub", response.StatusCode);
            return new TranscriptionResult("[openai-fallback-transcription]", TimeSpan.Zero, opts.TranscriptionModel ?? "whisper-1");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
        return new TranscriptionResult(text, TimeSpan.Zero, opts.TranscriptionModel ?? "whisper-1");
    }
}

internal static class OpenAiRetryHelper
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

            var delay = GetRetryDelay(response.Headers.RetryAfter?.Delta, attempt);
            logger.LogWarning("OpenAI request throttled (status {Status}), retrying in {Delay}s", response.StatusCode, delay.TotalSeconds);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static TimeSpan GetRetryDelay(TimeSpan? retryAfter, int attempt)
        => retryAfter ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
}
