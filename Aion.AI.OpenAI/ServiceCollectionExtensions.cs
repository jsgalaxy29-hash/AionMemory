using Aion.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aion.AI.OpenAI;

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
        if (string.Equals(options.Provider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            return sp.GetRequiredService<OpenAiTextGenerationProvider>();
        }

        if (string.Equals(options.Provider, "mistral", StringComparison.OrdinalIgnoreCase))
        {
            return sp.GetRequiredService<MistralTextGenerationProvider>();
        }

        return sp.GetRequiredService<HttpTextGenerationProvider>();
    }

    private static IEmbeddingProvider ResolveEmbeddingProvider(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptionsMonitor<AionAiOptions>>().CurrentValue;
        if (string.Equals(options.Provider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            return sp.GetRequiredService<OpenAiEmbeddingProvider>();
        }

        if (string.Equals(options.Provider, "mistral", StringComparison.OrdinalIgnoreCase))
        {
            return sp.GetRequiredService<MistralEmbeddingProvider>();
        }

        return sp.GetRequiredService<HttpEmbeddingProvider>();
    }

    private static IAudioTranscriptionProvider ResolveTranscriptionProvider(IServiceProvider sp)
    {
        var options = sp.GetRequiredService<IOptionsMonitor<AionAiOptions>>().CurrentValue;
        if (string.Equals(options.Provider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            return sp.GetRequiredService<OpenAiAudioTranscriptionProvider>();
        }

        if (string.Equals(options.Provider, "mistral", StringComparison.OrdinalIgnoreCase))
        {
            return sp.GetRequiredService<MistralAudioTranscriptionProvider>();
        }

        return sp.GetRequiredService<HttpAudioTranscriptionProvider>();
    }
}
