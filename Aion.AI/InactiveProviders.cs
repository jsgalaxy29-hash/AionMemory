using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Aion.AI.Observability;
using Aion.Domain;
using Microsoft.Extensions.Logging;

namespace Aion.AI;

/// <summary>
/// No-op providers returned when AI is not configured. They fail fast with a controlled exception.
/// </summary>
internal sealed class InactiveChatModel : IChatModel
{
    private readonly AiConfigurationStatus _status;
    private readonly ILogger<InactiveChatModel> _logger;
    private readonly IAiCallLogService _callLogService;

    public InactiveChatModel(AiProviderSelector selector, ILogger<InactiveChatModel> logger, IAiCallLogService callLogService)
    {
        _status = selector.GetStatus();
        _logger = logger;
        _callLogService = callLogService;
    }

    public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var exception = BuildException("chat");
        _ = _callLogService.LogAsync(new AiCallLogEntry("Inactive", _status.ActiveProvider, "chat", null, null, 0, AiCallStatus.Inactive), cancellationToken);
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
    private readonly IAiCallLogService _callLogService;

    public InactiveEmbeddingsModel(AiProviderSelector selector, ILogger<InactiveEmbeddingsModel> logger, IAiCallLogService callLogService)
    {
        _status = selector.GetStatus();
        _logger = logger;
        _callLogService = callLogService;
    }

    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var exception = BuildException("embeddings");
        _ = _callLogService.LogAsync(new AiCallLogEntry("Inactive", _status.ActiveProvider, "embeddings", null, null, 0, AiCallStatus.Inactive), cancellationToken);
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
    private readonly IAiCallLogService _callLogService;

    public InactiveTranscriptionModel(AiProviderSelector selector, ILogger<InactiveTranscriptionModel> logger, IAiCallLogService callLogService)
    {
        _status = selector.GetStatus();
        _logger = logger;
        _callLogService = callLogService;
    }

    public Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        var exception = BuildException("transcription");
        _ = _callLogService.LogAsync(new AiCallLogEntry("Inactive", _status.ActiveProvider, "transcription", null, null, 0, AiCallStatus.Inactive), cancellationToken);
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
    private readonly IAiCallLogService _callLogService;

    public InactiveVisionModel(AiProviderSelector selector, ILogger<InactiveVisionModel> logger, IAiCallLogService callLogService)
    {
        _status = selector.GetStatus();
        _logger = logger;
        _callLogService = callLogService;
    }

    public Task<S_VisionAnalysis> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var exception = BuildException("vision");
        _ = _callLogService.LogAsync(new AiCallLogEntry("Inactive", _status.ActiveProvider, "vision", null, null, 0, AiCallStatus.Inactive), cancellationToken);
        return Task.FromException<S_VisionAnalysis>(exception);
    }

    private AiUnavailableException BuildException(string capability)
    {
        _logger.LogWarning("AI {Capability} requested while inactive: {Reason}", capability, _status.Reason);
        return new AiUnavailableException(_status, $"IA inactive ({capability}). {_status.Reason}");
    }
}
