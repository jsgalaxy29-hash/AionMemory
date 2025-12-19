using Aion.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Aion.AI.Providers.Mistral;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAionMistral(this IServiceCollection services)
    {
        services.AddSingleton<MistralTextGenerationProvider>();
        services.AddSingleton<MistralEmbeddingProvider>();
        services.AddScoped<MistralAudioTranscriptionProvider>();

        services.AddKeyedSingleton<ILLMProvider>(AiProviderNames.Mistral, sp => sp.GetRequiredService<MistralTextGenerationProvider>());
        services.AddKeyedSingleton<IEmbeddingProvider>(AiProviderNames.Mistral, sp => sp.GetRequiredService<MistralEmbeddingProvider>());
        services.AddKeyedScoped<IAudioTranscriptionProvider>(AiProviderNames.Mistral, sp => sp.GetRequiredService<MistralAudioTranscriptionProvider>());

        return services;
    }
}
