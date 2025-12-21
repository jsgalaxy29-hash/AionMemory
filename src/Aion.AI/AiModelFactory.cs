using Aion.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aion.AI;

/// <summary>
/// Central factory that routes AI model calls to the configured provider while keeping a mock fallback for offline mode.
/// </summary>
public sealed class AiModelFactory : IChatModel, IEmbeddingsModel, ITranscriptionModel, IVisionModel
{
    private readonly IServiceProvider _services;
    private readonly AiProviderSelector _selector;
    private readonly ILogger<AiModelFactory> _logger;

    public AiModelFactory(IServiceProvider services, AiProviderSelector selector, ILogger<AiModelFactory> logger)
    {
        _services = services;
        _selector = selector;
        _logger = logger;
    }

    public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
        => Resolve<IChatModel>("chat").GenerateAsync(prompt, cancellationToken);

    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
        => Resolve<IEmbeddingsModel>("embeddings").EmbedAsync(text, cancellationToken);

    public Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
        => Resolve<ITranscriptionModel>("transcription").TranscribeAsync(audioStream, fileName, cancellationToken);

    public Task<S_VisionAnalysis> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
        => Resolve<IVisionModel>("vision").AnalyzeAsync(request, cancellationToken);

    private T Resolve<T>(string capability)
    {
        var status = _selector.GetStatus();
        var providerName = status.IsConfigured ? status.ActiveProvider : AiProviderNames.Inactive;
        var resolved = _services.GetKeyedService<T>(providerName);
        if (resolved is not null)
        {
            return resolved;
        }

        if (!string.Equals(providerName, AiProviderNames.Mock, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("{Capability} provider '{Provider}' is not registered; using mock provider instead", capability, providerName);
        }

        if (!status.IsConfigured)
        {
            return _services.GetRequiredKeyedService<T>(AiProviderNames.Inactive);
        }

        return _services.GetRequiredKeyedService<T>(AiProviderNames.Mock);
    }
}
