using Aion.AI.ModuleBuilder;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Aion.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAionAi(this IServiceCollection services, IConfiguration configuration)
    {
        var optionsBuilder = services
            .AddOptions<AionAiOptions>()
            .Bind(configuration.GetSection("Aion:Ai"))
            .PostConfigure(o => o.Normalize());

        optionsBuilder.Services.AddSingleton<IValidateOptions<AionAiOptions>, AionAiOptionsValidator>();
        optionsBuilder.ValidateOnStart();

        services.TryAddSingleton<IOperationScopeFactory, NoopOperationScopeFactory>();
        services.AddHttpClient(HttpClientNames.Llm, ConfigureClient(HttpClientNames.Llm));
        services.AddHttpClient(HttpClientNames.Embeddings, ConfigureClient(HttpClientNames.Embeddings));
        services.AddHttpClient(HttpClientNames.Transcription, ConfigureClient(HttpClientNames.Transcription));
        services.AddHttpClient(HttpClientNames.Vision, ConfigureClient(HttpClientNames.Vision));

        services.AddSingleton<AiProviderSelector>();

        services.AddSingleton<HttpTextGenerationProvider>();
        services.AddSingleton<HttpEmbeddingProvider>();
        services.AddScoped<HttpAudioTranscriptionProvider>();
        services.AddSingleton<HttpVisionProvider>();
        services.AddScoped<VisionEngine>();

        services.AddSingleton<HttpMockChatModel>();
        services.AddSingleton<HttpMockEmbeddingsModel>();
        services.AddScoped<HttpMockTranscriptionModel>();
        services.AddSingleton<HttpMockVisionModel>();

        services.AddSingleton<EchoLlmProvider>();
        services.AddSingleton<DeterministicEmbeddingProvider>();
        services.AddScoped<StubAudioTranscriptionProvider>();
        services.AddSingleton<InactiveChatModel>();
        services.AddSingleton<InactiveEmbeddingsModel>();
        services.AddScoped<InactiveTranscriptionModel>();
        services.AddSingleton<InactiveVisionModel>();

        services.AddKeyedSingleton<IChatModel>(AiProviderNames.Http, sp => sp.GetRequiredService<HttpTextGenerationProvider>());
        services.AddKeyedSingleton<IEmbeddingsModel>(AiProviderNames.Http, sp => sp.GetRequiredService<HttpEmbeddingProvider>());
        services.AddKeyedScoped<ITranscriptionModel>(AiProviderNames.Http, sp => sp.GetRequiredService<HttpAudioTranscriptionProvider>());
        services.AddKeyedScoped<IVisionModel>(AiProviderNames.Http, sp => sp.GetRequiredService<VisionEngine>());

        services.AddKeyedSingleton<IChatModel>(AiProviderNames.Mock, sp => sp.GetRequiredService<HttpMockChatModel>());
        services.AddKeyedSingleton<IEmbeddingsModel>(AiProviderNames.Mock, sp => sp.GetRequiredService<HttpMockEmbeddingsModel>());
        services.AddKeyedScoped<ITranscriptionModel>(AiProviderNames.Mock, sp => sp.GetRequiredService<HttpMockTranscriptionModel>());
        services.AddKeyedSingleton<IVisionModel>(AiProviderNames.Mock, sp => sp.GetRequiredService<HttpMockVisionModel>());

        services.AddKeyedSingleton<IChatModel>(AiProviderNames.Local, sp => sp.GetRequiredService<EchoLlmProvider>());
        services.AddKeyedSingleton<IEmbeddingsModel>(AiProviderNames.Local, sp => sp.GetRequiredService<DeterministicEmbeddingProvider>());
        services.AddKeyedScoped<ITranscriptionModel>(AiProviderNames.Local, sp => sp.GetRequiredService<StubAudioTranscriptionProvider>());
        services.AddKeyedSingleton<IChatModel>(AiProviderNames.Inactive, sp => sp.GetRequiredService<InactiveChatModel>());
        services.AddKeyedSingleton<IEmbeddingsModel>(AiProviderNames.Inactive, sp => sp.GetRequiredService<InactiveEmbeddingsModel>());
        services.AddKeyedScoped<ITranscriptionModel>(AiProviderNames.Inactive, sp => sp.GetRequiredService<InactiveTranscriptionModel>());
        services.AddKeyedSingleton<IVisionModel>(AiProviderNames.Inactive, sp => sp.GetRequiredService<InactiveVisionModel>());

        services.AddScoped<AiModelFactory>();
        services.AddScoped<IChatModel>(sp => sp.GetRequiredService<AiModelFactory>());
        services.AddScoped<IEmbeddingsModel>(sp => sp.GetRequiredService<AiModelFactory>());
        services.AddScoped<ITranscriptionModel>(sp => sp.GetRequiredService<AiModelFactory>());
        services.AddScoped<IVisionModel>(sp => sp.GetRequiredService<AiModelFactory>());
        services.AddScoped<ILLMProvider>(sp => sp.GetRequiredService<IChatModel>());
        services.AddScoped<IEmbeddingProvider>(sp => sp.GetRequiredService<IEmbeddingsModel>());
        services.AddScoped<IAudioTranscriptionProvider>(sp => sp.GetRequiredService<ITranscriptionModel>());
        services.AddScoped<IAionVisionService>(sp => sp.GetRequiredService<IVisionModel>());
        services.AddScoped<IVisionService>(sp => sp.GetRequiredService<IVisionModel>());

        services.AddScoped<IIntentDetector, IntentRecognizer>();
        services.AddScoped<IModuleDesigner, ModuleDesigner>();
        services.AddScoped<IModuleSpecDesigner, ModuleSpecDesigner>();
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
}
