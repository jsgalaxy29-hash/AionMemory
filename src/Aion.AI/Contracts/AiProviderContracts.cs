namespace Aion.AI;

/// <summary>
/// Low-level AI provider results kept in the AI layer to avoid leaking provider formats into Domain.
/// </summary>
public readonly record struct TranscriptionResult(string Text, TimeSpan Duration, string? Model = null);

public sealed record LlmResponse(string Content, string RawResponse, string? Model = null);

public sealed record EmbeddingResult(float[] Vector, string? Model = null, string? RawResponse = null);

public interface IAudioTranscriptionProvider
{
    Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default);
}

public interface ILLMProvider
{
    Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default);
}

public interface IEmbeddingProvider
{
    Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
