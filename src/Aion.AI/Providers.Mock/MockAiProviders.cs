using System.Text.Json;
using Aion.Domain;

namespace Aion.AI;

public sealed class HttpMockChatModel : IChatModel
{
    public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var content = $"[mock-chat] {prompt}";
        return Task.FromResult(new LlmResponse(content, content, "mock-chat"));
    }
}

public sealed class HttpMockEmbeddingsModel : IEmbeddingsModel
{
    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var seed = Math.Abs(text.GetHashCode());
        var vector = Enumerable.Range(0, 8)
            .Select(i => (float)((seed + i) % 997) / 997f)
            .ToArray();
        return Task.FromResult(new EmbeddingResult(vector, "mock-embedding", $"len:{text.Length}"));
    }
}

public sealed class HttpMockTranscriptionModel : ITranscriptionModel
{
    public Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TranscriptionResult($"[mock-transcription] {fileName}", TimeSpan.Zero, "mock-transcription"));
    }
}

public sealed class HttpMockVisionModel : IVisionModel
{
    public Task<S_VisionAnalysis> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            request.FileId,
            request.AnalysisType,
            summary = "Mock analysis (offline)"
        });

        return Task.FromResult(new S_VisionAnalysis
        {
            FileId = request.FileId,
            AnalysisType = request.AnalysisType,
            ResultJson = payload
        });
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

        var suggestions = links.Length == 2
            ? new[] { new MemoryLinkSuggestion(links[0], links[1], "Thèmes similaires", request.Records.First().SourceType, request.Records.Last().SourceType) }
            : Array.Empty<MemoryLinkSuggestion>();

        var summary = request.Records.Count == 0
            ? "Aucune donnée à analyser (mock)."
            : $"Mock summary for {request.Records.Count} records";

        return Task.FromResult(new MemoryAnalysisResult(summary, topics, suggestions, "mock"));
    }
}
