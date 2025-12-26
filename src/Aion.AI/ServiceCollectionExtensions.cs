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
        services.TryAddSingleton<IAiCallLogService, Observability.NoopAiCallLogService>();
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
        services.AddSingleton<OfflineChatModel>();
        services.AddSingleton<OfflineEmbeddingsModel>();
        services.AddScoped<OfflineTranscriptionModel>();
        services.AddSingleton<OfflineVisionModel>();

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

        services.AddKeyedSingleton<IChatModel>(AiProviderNames.Offline, sp => sp.GetRequiredService<OfflineChatModel>());
        services.AddKeyedSingleton<IEmbeddingsModel>(AiProviderNames.Offline, sp => sp.GetRequiredService<OfflineEmbeddingsModel>());
        services.AddKeyedScoped<ITranscriptionModel>(AiProviderNames.Offline, sp => sp.GetRequiredService<OfflineTranscriptionModel>());
        services.AddKeyedSingleton<IVisionModel>(AiProviderNames.Offline, sp => sp.GetRequiredService<OfflineVisionModel>());

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
        services.TryAddScoped<IAionVisionService>(sp => sp.GetRequiredService<IVisionModel>());
        services.TryAddScoped<IVisionService>(sp => sp.GetRequiredService<IVisionModel>());

        services.AddScoped<IIntentDetector, IntentRecognizer>();
        services.AddScoped<IIntentRouter, IntentRouter>();
        services.AddScoped<IModuleDesigner, ModuleDesigner>();
        services.AddScoped<IModuleSpecDesigner, ModuleSpecDesigner>();
        services.AddScoped<IModuleDesignService, ModuleDesignService>();
        services.AddScoped<ModuleDesignerService>();
        services.AddScoped<ICrudInterpreter, CrudInterpreter>();
        services.AddScoped<IAgendaInterpreter, AgendaInterpreter>();
        services.AddScoped<INoteInterpreter, NoteInterpreter>();
        services.AddScoped<IReportInterpreter, ReportInterpreter>();
        services.AddScoped<ITranscriptionMetadataInterpreter, TranscriptionMetadataInterpreter>();
        services.AddScoped<IMemoryAnalyzer, MemoryAnalyzer>();
        services.AddScoped<IMemoryContextBuilder, MemoryContextBuilder>();
        services.AddScoped<IChatAnswerer, ChatAnswerer>();
        services.AddScoped<INoteTaggingService, RuleBasedNoteTaggingService>();

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
            AiHttpClientConfigurator.ConfigureClient(client, endpoint, options);
        };
}
