using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Linq;
using Aion.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.AI;

public sealed class MistralTextGenerationProvider : ILLMProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<MistralTextGenerationProvider> _logger;

    public MistralTextGenerationProvider(IHttpClientFactory httpClientFactory, IOptionsMonitor<AionAiOptions> options, ILogger<MistralTextGenerationProvider> logger)
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
            _logger.LogWarning("Mistral endpoint not configured; returning stub response");
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

        var response = await HttpRetryHelper.SendWithRetryAsync(client, () => new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        }, _logger, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Mistral call failed with status {Status}; returning stub", response.StatusCode);
            var fallback = $"[mistral-fallback] {prompt}";
            return new LlmResponse(fallback, fallback, opts.LlmModel);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var contentResponse = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return new LlmResponse(contentResponse ?? string.Empty, json, opts.LlmModel);
    }

    internal HttpClient BuildClient(string clientName, string? endpoint)
    {
        var opts = _options.CurrentValue;
        var client = _httpClientFactory.CreateClient(clientName);
        var resolvedEndpoint = endpoint ?? opts.BaseEndpoint ?? "https://api.mistral.ai/v1/";

        if (client.BaseAddress is null && Uri.TryCreate(resolvedEndpoint, UriKind.Absolute, out var uri))
        {
            client.BaseAddress = uri;
        }

        client.Timeout = opts.RequestTimeout;

        if (!string.IsNullOrWhiteSpace(opts.ApiKey) && client.DefaultRequestHeaders.Authorization is null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);
        }

        foreach (var header in opts.DefaultHeaders)
        {
            client.DefaultRequestHeaders.Remove(header.Key);
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        return client;
    }
}

public sealed class MistralEmbeddingProvider : IEmbeddingProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly MistralTextGenerationProvider _clientBuilder;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<MistralEmbeddingProvider> _logger;

    public MistralEmbeddingProvider(MistralTextGenerationProvider clientBuilder, IOptionsMonitor<AionAiOptions> options, ILogger<MistralEmbeddingProvider> logger)
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
            _logger.LogWarning("Mistral embeddings endpoint not configured; returning stub vector");
            var vector = Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
            return new EmbeddingResult(vector, opts.EmbeddingModel ?? opts.LlmModel);
        }

        var payload = new { model = opts.EmbeddingModel ?? "mistral-embed", input = text };
        var response = await HttpRetryHelper.SendWithRetryAsync(client, () => new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json")
        }, _logger, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Mistral embeddings failed with status {Status}; returning stub", response.StatusCode);
            var vector = Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
            return new EmbeddingResult(vector, opts.EmbeddingModel ?? opts.LlmModel);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var values = doc.RootElement.GetProperty("data")[0].GetProperty("embedding").EnumerateArray().Select(e => e.GetSingle()).ToArray();
        return new EmbeddingResult(values, opts.EmbeddingModel ?? opts.LlmModel, json);
    }
}

public sealed class MistralAudioTranscriptionProvider : IAudioTranscriptionProvider
{
    private readonly MistralTextGenerationProvider _clientBuilder;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<MistralAudioTranscriptionProvider> _logger;

    public MistralAudioTranscriptionProvider(MistralTextGenerationProvider clientBuilder, IOptionsMonitor<AionAiOptions> options, ILogger<MistralAudioTranscriptionProvider> logger)
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
            _logger.LogWarning("Mistral transcription endpoint not configured; returning stub");
            return new TranscriptionResult($"[mistral-stub-transcription] {fileName}", TimeSpan.Zero, opts.TranscriptionModel ?? opts.LlmModel);
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(audioStream), "file", fileName);
        content.Add(new StringContent(opts.TranscriptionModel ?? opts.LlmModel ?? "whisper-large-v3"), "model");

        var response = await HttpRetryHelper.SendWithRetryAsync(client, () => new HttpRequestMessage(HttpMethod.Post, "audio/transcriptions")
        {
            Content = content
        }, _logger, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Mistral transcription failed with status {Status}; returning stub", response.StatusCode);
            return new TranscriptionResult($"[mistral-fallback-transcription] {fileName}", TimeSpan.Zero, opts.TranscriptionModel ?? opts.LlmModel);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
        return new TranscriptionResult(text, TimeSpan.Zero, opts.TranscriptionModel ?? opts.LlmModel);
    }
}
