using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAionAi(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<AionAiOptions>()
            .Bind(configuration.GetSection("Aion:Ai"))
            .PostConfigure(o => o.Normalize())
            .Validate(o => o.RequestTimeout > TimeSpan.Zero, "RequestTimeout must be positive.")
            .Validate(o => o.DefaultHeaders is not null, "DefaultHeaders collection must be configured.")
            .ValidateOnStart();

        services.AddHttpClient(HttpClientNames.Llm, ConfigureClient(HttpClientNames.Llm));
        services.AddHttpClient(HttpClientNames.Embeddings, ConfigureClient(HttpClientNames.Embeddings));
        services.AddHttpClient(HttpClientNames.Transcription, ConfigureClient(HttpClientNames.Transcription));
        services.AddHttpClient(HttpClientNames.Vision, ConfigureClient(HttpClientNames.Vision));

        services.AddSingleton<AiProviderSelector>();

        services.AddSingleton<HttpTextGenerationProvider>();
        services.AddSingleton<HttpEmbeddingProvider>();
        services.AddScoped<HttpAudioTranscriptionProvider>();
        services.AddSingleton<HttpVisionProvider>();

        services.AddSingleton<EchoLlmProvider>();
        services.AddSingleton<DeterministicEmbeddingProvider>();
        services.AddScoped<StubAudioTranscriptionProvider>();

        services.AddKeyedSingleton<ILLMProvider>(AiProviderNames.Http, sp => sp.GetRequiredService<HttpTextGenerationProvider>());
        services.AddKeyedSingleton<IEmbeddingProvider>(AiProviderNames.Http, sp => sp.GetRequiredService<HttpEmbeddingProvider>());
        services.AddKeyedScoped<IAudioTranscriptionProvider>(AiProviderNames.Http, sp => sp.GetRequiredService<HttpAudioTranscriptionProvider>());

        services.AddKeyedSingleton<ILLMProvider>(AiProviderNames.Local, sp => sp.GetRequiredService<EchoLlmProvider>());
        services.AddKeyedSingleton<IEmbeddingProvider>(AiProviderNames.Local, sp => sp.GetRequiredService<DeterministicEmbeddingProvider>());
        services.AddKeyedScoped<IAudioTranscriptionProvider>(AiProviderNames.Local, sp => sp.GetRequiredService<StubAudioTranscriptionProvider>());

        services.AddSingleton<ILLMProvider>(ResolveProvider<ILLMProvider>);
        services.AddSingleton<IEmbeddingProvider>(ResolveProvider<IEmbeddingProvider>);
        services.AddScoped<IAudioTranscriptionProvider>(ResolveProvider<IAudioTranscriptionProvider>);
        services.AddScoped<IAionVisionService, VisionEngine>();
        services.AddScoped<IVisionService>(sp => sp.GetRequiredService<IAionVisionService>());

        services.AddScoped<IIntentDetector, IntentRecognizer>();
        services.AddScoped<IModuleDesigner, ModuleDesigner>();
        services.AddScoped<ICrudInterpreter, CrudInterpreter>();
        services.AddScoped<IAgendaInterpreter, AgendaInterpreter>();
        services.AddScoped<INoteInterpreter, NoteInterpreter>();
        services.AddScoped<IReportInterpreter, ReportInterpreter>();

        return services;
    }

    private static Action<IServiceProvider, HttpClient> ConfigureClient(string clientName)
        => (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<AionAiOptions>>().CurrentValue;
            var endpoint = clientName switch
            {
                HttpClientNames.Llm => options.LlmEndpoint ?? options.BaseEndpoint,
                HttpClientNames.Embeddings => options.EmbeddingsEndpoint ?? options.BaseEndpoint,
                HttpClientNames.Transcription => options.TranscriptionEndpoint ?? options.BaseEndpoint,
                HttpClientNames.Vision => options.VisionEndpoint ?? options.BaseEndpoint,
                _ => options.BaseEndpoint
            };

            if (Uri.TryCreate(endpoint ?? string.Empty, UriKind.Absolute, out var uri))
            {
                client.BaseAddress = uri;
            }

            client.Timeout = options.RequestTimeout;

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
            }

            foreach (var header in options.DefaultHeaders)
            {
                client.DefaultRequestHeaders.Remove(header.Key);
                client.DefaultRequestHeaders.Add(header.Key, header.Value);
            }
        };

    private static T ResolveProvider<T>(IServiceProvider serviceProvider)
    {
        var providerName = serviceProvider.GetRequiredService<AiProviderSelector>().ResolveProviderName();
        var resolved = serviceProvider.GetKeyedService<T>(providerName);
        if (resolved is not null)
        {
            return resolved;
        }

        if (!string.Equals(providerName, AiProviderNames.Http, StringComparison.OrdinalIgnoreCase))
        {
            serviceProvider.GetService<ILogger<AiProviderSelector>>()?.LogWarning("AI provider '{Provider}' not registered; falling back to HTTP provider", providerName);
        }

        return serviceProvider.GetRequiredKeyedService<T>(AiProviderNames.Http);
    }
}
