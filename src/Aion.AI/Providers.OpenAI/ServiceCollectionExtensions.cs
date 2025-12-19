using Aion.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Aion.AI.Providers.OpenAI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAionOpenAi(this IServiceCollection services)
    {
        services.AddSingleton<OpenAiTextGenerationProvider>();
        services.AddSingleton<OpenAiEmbeddingProvider>();
        services.AddScoped<OpenAiAudioTranscriptionProvider>();

        services.AddKeyedSingleton<ILLMProvider>(AiProviderNames.OpenAi, sp => sp.GetRequiredService<OpenAiTextGenerationProvider>());
        services.AddKeyedSingleton<IEmbeddingProvider>(AiProviderNames.OpenAi, sp => sp.GetRequiredService<OpenAiEmbeddingProvider>());
        services.AddKeyedScoped<IAudioTranscriptionProvider>(AiProviderNames.OpenAi, sp => sp.GetRequiredService<OpenAiAudioTranscriptionProvider>());

        return services;
    }
}
