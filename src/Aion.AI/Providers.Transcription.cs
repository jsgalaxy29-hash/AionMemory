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
