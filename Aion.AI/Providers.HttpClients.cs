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

