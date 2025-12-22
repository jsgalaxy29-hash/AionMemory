namespace Aion.AI;

public sealed record AionAiOptions
{
    private string? _baseEndpoint;
    private string? _llmModel;
    private string? _provider;
    private bool HasConfiguredProvider => !string.IsNullOrWhiteSpace(_provider);

    public string? BaseEndpoint
    {
        get => _baseEndpoint;
        init => _baseEndpoint = Normalize(value);
    }

    public string? Endpoint
    {
        get => _baseEndpoint;
        init => _baseEndpoint = Normalize(value);
    }

    public string? ApiKey { get; init; }

    public string? LlmEndpoint { get; init; }

    public string? LlmModel
    {
        get => _llmModel;
        init => _llmModel = Normalize(value);
    }

    public string? Model
    {
        get => _llmModel;
        init => _llmModel = Normalize(value);
    }

    public string Provider
    {
        get => _provider ?? AiProviderNames.Mock;
        init => _provider = Normalize(value);
    }

    public string? Organization { get; init; }
    public int? MaxTokens { get; init; }
    public double Temperature { get; init; } = 0.2;
    public bool EnablePromptTracing { get; init; }

    public string? EmbeddingsEndpoint { get; init; }
    public string? EmbeddingModel { get; init; }

    public string? TranscriptionEndpoint { get; init; }
    public string? TranscriptionModel { get; init; }

    public string? VisionEndpoint { get; init; }
    public string? VisionModel { get; init; }

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public IDictionary<string, string> DefaultHeaders { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        _baseEndpoint = FirstNonEmpty(_baseEndpoint, LlmEndpoint, EmbeddingsEndpoint, TranscriptionEndpoint, VisionEndpoint);
        _provider ??= AiProviderNames.Mock;
    }

    internal bool HasConfiguration()
        => HasConfiguredProvider
           || !string.IsNullOrWhiteSpace(_baseEndpoint)
           || !string.IsNullOrWhiteSpace(_llmModel)
           || !string.IsNullOrWhiteSpace(ApiKey)
           || !string.IsNullOrWhiteSpace(Organization)
           || !string.IsNullOrWhiteSpace(EmbeddingsEndpoint)
           || !string.IsNullOrWhiteSpace(TranscriptionEndpoint)
           || !string.IsNullOrWhiteSpace(VisionEndpoint);

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return Normalize(value);
            }
        }

        return null;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
