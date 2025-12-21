using Microsoft.Extensions.Options;

namespace Aion.AI;

public sealed class AionAiOptionsValidator : IValidateOptions<AionAiOptions>
{
    public ValidateOptionsResult Validate(string? name, AionAiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Normalize();

        if (!options.HasConfiguration())
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();

        if (options.DefaultHeaders is null)
        {
            errors.Add("DefaultHeaders must be provided.");
        }

        if (options.RequestTimeout <= TimeSpan.Zero)
        {
            errors.Add("RequestTimeout must be greater than zero.");
        }

        var provider = Normalize(options.Provider);
        var hasEndpoint = HasAnyEndpoint(options);

        if (provider == AiProviderNames.Http && !hasEndpoint)
        {
            errors.Add($"BaseEndpoint (or a specific Llm/Embeddings/Transcription/Vision endpoint) is required for provider '{provider}'.");
        }
        else if (provider != AiProviderNames.Mock
                 && provider != AiProviderNames.Local
                 && provider != AiProviderNames.OpenAi
                 && provider != AiProviderNames.Mistral
                 && !hasEndpoint)
        {
            errors.Add($"BaseEndpoint must be provided for custom AI provider '{provider}'.");
        }

        if ((provider == AiProviderNames.OpenAi || provider == AiProviderNames.Mistral) && string.IsNullOrWhiteSpace(options.ApiKey))
        {
            errors.Add($"ApiKey is required when using the '{provider}' AI provider.");
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }

    private static string Normalize(string? provider)
        => string.IsNullOrWhiteSpace(provider) ? AiProviderNames.Mock : provider.Trim().ToLowerInvariant();

    private static bool HasAnyEndpoint(AionAiOptions options)
        => !string.IsNullOrWhiteSpace(options.BaseEndpoint)
           || !string.IsNullOrWhiteSpace(options.LlmEndpoint)
           || !string.IsNullOrWhiteSpace(options.EmbeddingsEndpoint)
           || !string.IsNullOrWhiteSpace(options.TranscriptionEndpoint)
           || !string.IsNullOrWhiteSpace(options.VisionEndpoint);
}
