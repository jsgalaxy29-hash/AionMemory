using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.AI;

/// <summary>
/// Centralizes AI provider selection so only one strategy decides which implementation is active.
/// </summary>
public sealed class AiProviderSelector
{
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly ILogger<AiProviderSelector> _logger;

    public AiProviderSelector(IOptionsMonitor<AionAiOptions> options, ILogger<AiProviderSelector> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool HasConfiguredProvider => HasRemoteConfiguration(_options.CurrentValue);

    public string ResolveProviderName()
    {
        var options = _options.CurrentValue;
        var normalized = Normalize(options.Provider);

        if (!HasRemoteConfiguration(options))
        {
            return normalized == AiProviderNames.Local ? AiProviderNames.Local : AiProviderNames.Mock;
        }

        return normalized switch
        {
            AiProviderNames.OpenAi => AiProviderNames.OpenAi,
            AiProviderNames.Mistral => AiProviderNames.Mistral,
            AiProviderNames.Http => AiProviderNames.Http,
            AiProviderNames.Mock => AiProviderNames.Mock,
            AiProviderNames.Local => AiProviderNames.Local,
            _ => LogAndFallback(normalized)
        };
    }

    private static string Normalize(string? provider)
        => string.IsNullOrWhiteSpace(provider) ? AiProviderNames.Mock : provider.Trim().ToLowerInvariant();

    private static bool HasRemoteConfiguration(AionAiOptions options)
        => !string.IsNullOrWhiteSpace(options.ApiKey) || !string.IsNullOrWhiteSpace(options.BaseEndpoint);

    private string LogAndFallback(string? provider)
    {
        _logger.LogWarning("AI provider '{Provider}' is not recognized; defaulting to mock provider", provider ?? "(null)");
        return AiProviderNames.Mock;
    }
}
