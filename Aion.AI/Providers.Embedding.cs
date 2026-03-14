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

            var payload = new { model = opts.EmbeddingModel ?? opts.LlmModel ?? "generic-embedding", input = EmbeddingPayload.NormalizeInput(text) };
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
            if (!EmbeddingPayload.TryReadVector(json, out var values))
            {
                _logger.LogWarning("Embedding response payload is invalid or empty; returning stub");
                AiMetrics.RecordError(operation, providerName, opts.EmbeddingModel ?? opts.LlmModel);
                status = AiCallStatus.Fallback;
                values = Enumerable.Range(0, 8).Select(i => (float)(text.Length + i)).ToArray();
            }

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
