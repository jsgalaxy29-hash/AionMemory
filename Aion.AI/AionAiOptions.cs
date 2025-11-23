namespace Aion.AI;

public sealed record AionAiOptions
{
    public string? BaseEndpoint { get; init; }
    public string? ApiKey { get; init; }

    public string? LlmEndpoint { get; init; }
    public string? LlmModel { get; init; }
    public int? MaxTokens { get; init; }
    public double Temperature { get; init; } = 0.2;

    public string? EmbeddingsEndpoint { get; init; }
    public string? EmbeddingModel { get; init; }

    public string? TranscriptionEndpoint { get; init; }
    public string? TranscriptionModel { get; init; }

    public string? VisionEndpoint { get; init; }
    public string? VisionModel { get; init; }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public IDictionary<string, string> DefaultHeaders { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
