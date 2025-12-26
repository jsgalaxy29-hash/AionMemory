using System.Diagnostics;
using System.Text.Json;
using Aion.AI.Observability;
using Aion.Domain;

namespace Aion.AI;

public sealed class HttpMockChatModel : IChatModel
{
    private readonly IAiCallLogService _callLogService;

    public HttpMockChatModel()
        : this(NoopAiCallLogService.Instance)
    {
    }

    public HttpMockChatModel(IAiCallLogService callLogService)
    {
        _callLogService = callLogService;
    }

    public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var content = $"[mock-chat] {prompt}";
        var response = new LlmResponse(content, content, "mock-chat");
        stopwatch.Stop();
        return LogAsync("chat", "Mock", "mock-chat", response, stopwatch, cancellationToken);
    }

    private async Task<LlmResponse> LogAsync(string operation, string provider, string model, LlmResponse response, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        await _callLogService.LogAsync(
            new AiCallLogEntry(provider, model, operation, null, null, stopwatch.Elapsed.TotalMilliseconds, AiCallStatus.Success),
            cancellationToken).ConfigureAwait(false);
        return response;
    }
}

public sealed class HttpMockEmbeddingsModel : IEmbeddingsModel
{
    private readonly IAiCallLogService _callLogService;

    public HttpMockEmbeddingsModel()
        : this(NoopAiCallLogService.Instance)
    {
    }

    public HttpMockEmbeddingsModel(IAiCallLogService callLogService)
    {
        _callLogService = callLogService;
    }

    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var seed = Math.Abs(text.GetHashCode());
        var vector = Enumerable.Range(0, 8)
            .Select(i => (float)((seed + i) % 997) / 997f)
            .ToArray();
        var response = new EmbeddingResult(vector, "mock-embedding", $"len:{text.Length}");
        stopwatch.Stop();
        return LogAsync("embeddings", "Mock", "mock-embedding", response, stopwatch, cancellationToken);
    }

    private async Task<EmbeddingResult> LogAsync(string operation, string provider, string model, EmbeddingResult response, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        await _callLogService.LogAsync(
            new AiCallLogEntry(provider, model, operation, null, null, stopwatch.Elapsed.TotalMilliseconds, AiCallStatus.Success),
            cancellationToken).ConfigureAwait(false);
        return response;
    }
}

public sealed class HttpMockTranscriptionModel : ITranscriptionModel
{
    private readonly IAiCallLogService _callLogService;

    public HttpMockTranscriptionModel()
        : this(NoopAiCallLogService.Instance)
    {
    }

    public HttpMockTranscriptionModel(IAiCallLogService callLogService)
    {
        _callLogService = callLogService;
    }

    public Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = new TranscriptionResult($"[mock-transcription] {fileName}", TimeSpan.Zero, "mock-transcription");
        stopwatch.Stop();
        return LogAsync("transcription", "Mock", "mock-transcription", response, stopwatch, cancellationToken);
    }

    private async Task<TranscriptionResult> LogAsync(string operation, string provider, string model, TranscriptionResult response, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        await _callLogService.LogAsync(
            new AiCallLogEntry(provider, model, operation, null, null, stopwatch.Elapsed.TotalMilliseconds, AiCallStatus.Success),
            cancellationToken).ConfigureAwait(false);
        return response;
    }
}

public sealed class HttpMockVisionModel : IVisionModel
{
    private readonly IAiCallLogService _callLogService;

    public HttpMockVisionModel()
        : this(NoopAiCallLogService.Instance)
    {
    }

    public HttpMockVisionModel(IAiCallLogService callLogService)
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
            summary = "Mock analysis (offline)"
        });
        var response = new S_VisionAnalysis
        {
            FileId = request.FileId,
            AnalysisType = request.AnalysisType,
            ResultJson = payload
        };
        stopwatch.Stop();
        return LogAsync("vision", "Mock", request.Model ?? "mock-vision", response, stopwatch, cancellationToken);
    }

    private async Task<S_VisionAnalysis> LogAsync(string operation, string provider, string model, S_VisionAnalysis response, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        await _callLogService.LogAsync(
            new AiCallLogEntry(provider, model, operation, null, null, stopwatch.Elapsed.TotalMilliseconds, AiCallStatus.Success),
            cancellationToken).ConfigureAwait(false);
        return response;
    }
}

public sealed class MockMemoryAnalyzer : IMemoryAnalyzer
{
    public Task<MemoryAnalysisResult> AnalyzeAsync(MemoryAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var topics = new[]
        {
            new MemoryTopic("Routine", new[] { "habits", "daily" }),
            new MemoryTopic("Objectifs", new[] { "goals" })
        };

        var links = request.Records
            .Take(2)
            .Select(r => r.Id)
            .ToArray();

        var explanation = new MemoryAnalysisExplanation(
            request.Records.Take(2).Select(record => new MemoryAnalysisSource(
                record.Id,
                record.Title,
                record.SourceType,
                record.Content.Length > 80 ? $"{record.Content[..80]}…" : record.Content)).ToArray(),
            new[] { new MemoryAnalysisRule("mock-similarity", "Les enregistrements partagent des mots-clés récurrents.") });

        var suggestions = links.Length == 2
            ? new[] { new MemoryLinkSuggestion(links[0], links[1], "Thèmes similaires", request.Records.First().SourceType, request.Records.Last().SourceType, explanation) }
            : Array.Empty<MemoryLinkSuggestion>();

        var summary = request.Records.Count == 0
            ? "Aucune donnée à analyser (mock)."
            : $"Mock summary for {request.Records.Count} records";

        return Task.FromResult(new MemoryAnalysisResult(summary, topics, suggestions, "mock", explanation));
    }
}
