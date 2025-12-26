using System.Text.Json;
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

    public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var content = Replies[Random.Shared.Next(Replies.Length)];
        return Task.FromResult(new LlmResponse(content, content, "offline"));
    }
}

public sealed class OfflineEmbeddingsModel : IEmbeddingsModel
{
    private static readonly float[] EmptyVector = new float[8];

    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new EmbeddingResult(EmptyVector, "offline-embedding", "offline"));
    }
}

public sealed class OfflineTranscriptionModel : ITranscriptionModel
{
    public Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TranscriptionResult("(Mode hors-ligne) Transcription indisponible.", TimeSpan.Zero, "offline-transcription"));
    }
}

public sealed class OfflineVisionModel : IVisionModel
{
    public Task<S_VisionAnalysis> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            request.FileId,
            request.AnalysisType,
            summary = "(Mode hors-ligne) Analyse indisponible."
        });

        return Task.FromResult(new S_VisionAnalysis
        {
            FileId = request.FileId,
            AnalysisType = request.AnalysisType,
            ResultJson = payload
        });
    }
}
