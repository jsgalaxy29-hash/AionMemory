using System.Diagnostics;
using System.Text.Json;
using Aion.AI.Observability;
using Aion.Domain;

namespace Aion.AI;

public sealed class OfflineChatModel : IChatModel
{
    private static readonly string[] Replies =
    [
        "(Mode hors-ligne) Je ne peux pas répondre sans connexion.",
        "(Mode hors-ligne) Je ne peux pas accéder aux services distants.",
        "(Mode hors-ligne) Réponse indisponible sans connexion réseau."
    ];

    private readonly IAiCallLogService _callLogService;

    public OfflineChatModel()
        : this(NoopAiCallLogService.Instance)
    {
    }

    public OfflineChatModel(IAiCallLogService callLogService)
    {
        _callLogService = callLogService;
    }

    public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var content = Replies[Random.Shared.Next(Replies.Length)];
        var response = new LlmResponse(content, content, "offline");
        stopwatch.Stop();
        return LogAsync("chat", "Offline", "offline", response, stopwatch, cancellationToken);
    }

    private async Task<LlmResponse> LogAsync(string operation, string provider, string model, LlmResponse response, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        await _callLogService.LogAsync(
            new AiCallLogEntry(provider, model, operation, null, null, stopwatch.Elapsed.TotalMilliseconds, AiCallStatus.Inactive),
            cancellationToken).ConfigureAwait(false);
        return response;
    }
}

public sealed class OfflineEmbeddingsModel : IEmbeddingsModel
{
    private static readonly float[] EmptyVector = new float[8];
    private readonly IAiCallLogService _callLogService;

    public OfflineEmbeddingsModel()
        : this(NoopAiCallLogService.Instance)
    {
    }

    public OfflineEmbeddingsModel(IAiCallLogService callLogService)
    {
        _callLogService = callLogService;
    }

    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = new EmbeddingResult(EmptyVector, "offline-embedding", "offline");
        stopwatch.Stop();
        return LogAsync("embeddings", "Offline", "offline-embedding", response, stopwatch, cancellationToken);
    }

    private async Task<EmbeddingResult> LogAsync(string operation, string provider, string model, EmbeddingResult response, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        await _callLogService.LogAsync(
            new AiCallLogEntry(provider, model, operation, null, null, stopwatch.Elapsed.TotalMilliseconds, AiCallStatus.Inactive),
            cancellationToken).ConfigureAwait(false);
        return response;
    }
}

public sealed class OfflineTranscriptionModel : ITranscriptionModel
{
    private readonly IAiCallLogService _callLogService;

    public OfflineTranscriptionModel()
        : this(NoopAiCallLogService.Instance)
    {
    }

    public OfflineTranscriptionModel(IAiCallLogService callLogService)
    {
        _callLogService = callLogService;
    }

    public Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = new TranscriptionResult("(Mode hors-ligne) Transcription indisponible.", TimeSpan.Zero, "offline-transcription");
        stopwatch.Stop();
        return LogAsync("transcription", "Offline", "offline-transcription", response, stopwatch, cancellationToken);
    }

    private async Task<TranscriptionResult> LogAsync(string operation, string provider, string model, TranscriptionResult response, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        await _callLogService.LogAsync(
            new AiCallLogEntry(provider, model, operation, null, null, stopwatch.Elapsed.TotalMilliseconds, AiCallStatus.Inactive),
            cancellationToken).ConfigureAwait(false);
        return response;
    }
}

public sealed class OfflineVisionModel : IVisionModel
{
    private readonly IAiCallLogService _callLogService;

    public OfflineVisionModel()
        : this(NoopAiCallLogService.Instance)
    {
    }

    public OfflineVisionModel(IAiCallLogService callLogService)
    {
        _callLogService = callLogService;
    }

    public Task<S_VisionAnalysis> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var payload = JsonSerializer.Serialize(new
        {
            request.FileId,
            request.AnalysisType,
            summary = "(Mode hors-ligne) Analyse indisponible."
        });

        var response = new S_VisionAnalysis
        {
            FileId = request.FileId,
            AnalysisType = request.AnalysisType,
            ResultJson = payload
        };
        stopwatch.Stop();
        return LogAsync("vision", "Offline", request.Model ?? "offline-vision", response, stopwatch, cancellationToken);
    }

    private async Task<S_VisionAnalysis> LogAsync(string operation, string provider, string model, S_VisionAnalysis response, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        await _callLogService.LogAsync(
            new AiCallLogEntry(provider, model, operation, null, null, stopwatch.Elapsed.TotalMilliseconds, AiCallStatus.Inactive),
            cancellationToken).ConfigureAwait(false);
        return response;
    }
}
