using System;

namespace Aion.AI;

/// <summary>
/// Represents the current AI configuration status for consumers that need to gate usage.
/// </summary>
public sealed record AiConfigurationStatus(bool IsConfigured, string ActiveProvider, string? Reason)
{
    public static AiConfigurationStatus Inactive(string? reason = null)
        => new(false, AiProviderNames.Inactive, reason ?? "IA inactive : configurez Aion:Ai (ApiKey/BaseEndpoint)." );
}

/// <summary>
/// Raised when an AI capability is invoked while no provider is configured.
/// </summary>
public sealed class AiUnavailableException : InvalidOperationException
{
    public AiUnavailableException(AiConfigurationStatus status, string? message = null)
        : base(message ?? status.Reason ?? "AI provider is not configured.")
    {
        Status = status;
    }

    public AiConfigurationStatus Status { get; }
}
