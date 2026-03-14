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

        services.AddKeyedSingleton<IChatModel>(AiProviderNames.Mistral, sp => sp.GetRequiredService<MistralTextGenerationProvider>());
        services.AddKeyedSingleton<IEmbeddingsModel>(AiProviderNames.Mistral, sp => sp.GetRequiredService<MistralEmbeddingProvider>());
        services.AddKeyedScoped<ITranscriptionModel>(AiProviderNames.Mistral, sp => sp.GetRequiredService<MistralAudioTranscriptionProvider>());

        return services;
    }
}
