using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Aion.AI;

/// <summary>
/// No-op providers returned when AI is not configured. They fail fast with a controlled exception.
/// </summary>
internal sealed class InactiveChatModel : IChatModel
{
    private readonly AiConfigurationStatus _status;
    private readonly ILogger<InactiveChatModel> _logger;

    public InactiveChatModel(AiProviderSelector selector, ILogger<InactiveChatModel> logger)
    {
        _status = selector.GetStatus();
        _logger = logger;
    }

    public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var exception = BuildException("chat");
        return Task.FromException<LlmResponse>(exception);
    }

    private AiUnavailableException BuildException(string capability)
    {
        _logger.LogWarning("AI {Capability} requested while inactive: {Reason}", capability, _status.Reason);
        return new AiUnavailableException(_status, $"IA inactive ({capability}). {_status.Reason}");
    }
}

internal sealed class InactiveEmbeddingsModel : IEmbeddingsModel
{
    private readonly AiConfigurationStatus _status;
    private readonly ILogger<InactiveEmbeddingsModel> _logger;

    public InactiveEmbeddingsModel(AiProviderSelector selector, ILogger<InactiveEmbeddingsModel> logger)
    {
        _status = selector.GetStatus();
        _logger = logger;
    }

    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var exception = BuildException("embeddings");
        return Task.FromException<EmbeddingResult>(exception);
    }

    private AiUnavailableException BuildException(string capability)
    {
        _logger.LogWarning("AI {Capability} requested while inactive: {Reason}", capability, _status.Reason);
        return new AiUnavailableException(_status, $"IA inactive ({capability}). {_status.Reason}");
    }
}

internal sealed class InactiveTranscriptionModel : ITranscriptionModel
{
    private readonly AiConfigurationStatus _status;
    private readonly ILogger<InactiveTranscriptionModel> _logger;

    public InactiveTranscriptionModel(AiProviderSelector selector, ILogger<InactiveTranscriptionModel> logger)
    {
        _status = selector.GetStatus();
        _logger = logger;
    }

    public Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        var exception = BuildException("transcription");
        return Task.FromException<TranscriptionResult>(exception);
    }

    private AiUnavailableException BuildException(string capability)
    {
        _logger.LogWarning("AI {Capability} requested while inactive: {Reason}", capability, _status.Reason);
        return new AiUnavailableException(_status, $"IA inactive ({capability}). {_status.Reason}");
    }
}

internal sealed class InactiveVisionModel : IVisionModel
{
    private readonly AiConfigurationStatus _status;
    private readonly ILogger<InactiveVisionModel> _logger;

    public InactiveVisionModel(AiProviderSelector selector, ILogger<InactiveVisionModel> logger)
    {
        _status = selector.GetStatus();
        _logger = logger;
    }

    public Task<S_VisionAnalysis> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var exception = BuildException("vision");
        return Task.FromException<S_VisionAnalysis>(exception);
    }

    private AiUnavailableException BuildException(string capability)
    {
        _logger.LogWarning("AI {Capability} requested while inactive: {Reason}", capability, _status.Reason);
        return new AiUnavailableException(_status, $"IA inactive ({capability}). {_status.Reason}");
    }
}
