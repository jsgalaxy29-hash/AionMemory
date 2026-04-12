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
            ResultJson = JsonSerializer.Serialize(BuildStubPayload(fileId, analysisType, message), SerializerOptions)
        };

    private static object BuildStubPayload(Guid fileId, VisionAnalysisType analysisType, string message)
    {
        return analysisType switch
        {
            VisionAnalysisType.Classification => new
            {
                fileId,
                analysisType,
                summary = message,
                labels = new[]
                {
                    new { label = "document", score = 0.2 },
                    new { label = "invoice", score = 0.1 }
                }
            },
            VisionAnalysisType.Tagging => new
            {
                fileId,
                analysisType,
                summary = message,
                tags = new[] { "stub" }
            },
            _ => new
            {
                fileId,
                analysisType,
                summary = message,
                ocrText = message
            }
        };
    }
}
