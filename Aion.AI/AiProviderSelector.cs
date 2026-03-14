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

    public bool HasConfiguredProvider => _options.CurrentValue.HasConfiguration();

    public AiConfigurationStatus GetStatus()
    {
        var options = _options.CurrentValue;
        if (!options.HasConfiguration())
        {
            return AiConfigurationStatus.Inactive("Aucune configuration AI détectée (Aion:Ai).");
        }

        var provider = Normalize(options.Provider);
        if (provider is AiProviderNames.Mock or AiProviderNames.Local or AiProviderNames.Offline)
        {
            return new AiConfigurationStatus(true, provider, null);
        }

        if (!HasRemoteConfiguration(options))
        {
            return AiConfigurationStatus.Inactive("Aucune clé ni endpoint pour l'IA distante.");
        }

        return new AiConfigurationStatus(true, ResolveProviderName(options), null);
    }

    public string ResolveProviderName()
    {
        var options = _options.CurrentValue;
        return ResolveProviderName(options);
    }

    private string ResolveProviderName(AionAiOptions options)
    {
        var normalized = Normalize(options.Provider);

        if (!options.HasConfiguration())
        {
            return AiProviderNames.Inactive;
        }

        return normalized switch
        {
            AiProviderNames.OpenAi => AiProviderNames.OpenAi,
            AiProviderNames.Mistral => AiProviderNames.Mistral,
            AiProviderNames.Http => AiProviderNames.Http,
            AiProviderNames.Mock => AiProviderNames.Mock,
            AiProviderNames.Local => AiProviderNames.Local,
            AiProviderNames.Offline => AiProviderNames.Offline,
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
