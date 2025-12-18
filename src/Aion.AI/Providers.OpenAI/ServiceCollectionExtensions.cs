using Aion.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aion.AI.Providers.OpenAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAionOpenAi(this IServiceCollection services)
    {
        services.AddSingleton<OpenAiTextGenerationProvider>();
        services.AddSingleton<OpenAiEmbeddingProvider>();
        services.AddScoped<OpenAiAudioTranscriptionProvider>();

        services.AddSingleton<ILLMProvider>(ResolveTextProvider);
        services.AddSingleton<IEmbeddingProvider>(ResolveEmbeddingProvider);
        services.AddScoped<IAudioTranscriptionProvider>(ResolveTranscriptionProvider);

        return services;
    }

    private static ILLMProvider ResolveTextProvider(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptionsMonitor<AionAiOptions>>().CurrentValue;
        if (!string.Equals(options.Provider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            return sp.GetRequiredService<HttpTextGenerationProvider>();
        }

        return sp.GetRequiredService<OpenAiTextGenerationProvider>();
    }

    private static IEmbeddingProvider ResolveEmbeddingProvider(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptionsMonitor<AionAiOptions>>().CurrentValue;
        if (!string.Equals(options.Provider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            return sp.GetRequiredService<HttpEmbeddingProvider>();
        }

        return sp.GetRequiredService<OpenAiEmbeddingProvider>();
    }

    private static IAudioTranscriptionProvider ResolveTranscriptionProvider(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptionsMonitor<AionAiOptions>>().CurrentValue;
        if (!string.Equals(options.Provider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            return sp.GetRequiredService<HttpAudioTranscriptionProvider>();
        }

        return sp.GetRequiredService<OpenAiAudioTranscriptionProvider>();
    }
}
