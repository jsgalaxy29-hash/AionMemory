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

        services.AddKeyedSingleton<IChatModel>(AiProviderNames.OpenAi, sp => sp.GetRequiredService<OpenAiTextGenerationProvider>());
        services.AddKeyedSingleton<IEmbeddingsModel>(AiProviderNames.OpenAi, sp => sp.GetRequiredService<OpenAiEmbeddingProvider>());
        services.AddKeyedScoped<ITranscriptionModel>(AiProviderNames.OpenAi, sp => sp.GetRequiredService<OpenAiAudioTranscriptionProvider>());

        return services;
    }
}
