namespace Aion.AI;

public sealed record AionAiOptions
{
    private string? _baseEndpoint;
    private string? _llmModel;

    public string? BaseEndpoint
    {
        get => _baseEndpoint;
        init => _baseEndpoint = value;
    }

    public string? Endpoint
    {
        get => _baseEndpoint;
        init => _baseEndpoint = value;
    }

    public string? ApiKey { get; init; }

    public string? LlmEndpoint { get; init; }

    public string? LlmModel
    {
        get => _llmModel;
        init => _llmModel = value;
    }

    public string? Model
    {
        get => _llmModel;
        init => _llmModel = value;
    }
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
