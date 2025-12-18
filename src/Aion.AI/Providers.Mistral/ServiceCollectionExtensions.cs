using Aion.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aion.AI.Providers.Mistral;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAionMistral(this IServiceCollection services)
    {
        services.AddSingleton<MistralTextGenerationProvider>();
        services.AddSingleton<MistralEmbeddingProvider>();
        services.AddScoped<MistralAudioTranscriptionProvider>();

        services.AddSingleton<ILLMProvider>(ResolveTextProvider);
        services.AddSingleton<IEmbeddingProvider>(ResolveEmbeddingProvider);
        services.AddScoped<IAudioTranscriptionProvider>(ResolveTranscriptionProvider);

        return services;
    }

    private static ILLMProvider ResolveTextProvider(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptionsMonitor<AionAiOptions>>().CurrentValue;
        if (!string.Equals(options.Provider, "mistral", StringComparison.OrdinalIgnoreCase))
        {
            return sp.GetRequiredService<HttpTextGenerationProvider>();
        }

        return sp.GetRequiredService<MistralTextGenerationProvider>();
    }

    private static IEmbeddingProvider ResolveEmbeddingProvider(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptionsMonitor<AionAiOptions>>().CurrentValue;
        if (!string.Equals(options.Provider, "mistral", StringComparison.OrdinalIgnoreCase))
        {
            return sp.GetRequiredService<HttpEmbeddingProvider>();
        }

        return sp.GetRequiredService<MistralEmbeddingProvider>();
    }

    private static IAudioTranscriptionProvider ResolveTranscriptionProvider(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptionsMonitor<AionAiOptions>>().CurrentValue;
        if (!string.Equals(options.Provider, "mistral", StringComparison.OrdinalIgnoreCase))
        {
            return sp.GetRequiredService<HttpAudioTranscriptionProvider>();
        }

        return sp.GetRequiredService<MistralAudioTranscriptionProvider>();
    }
}
