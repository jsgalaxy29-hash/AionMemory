using Aion.AI.ModuleBuilder;
using Aion.AI.Adapters;
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

        services.AddKeyedSingleton<IChatModel>(AiProviderNames.Http, (sp, _) => sp.GetRequiredService<HttpTextGenerationProvider>());
        services.AddKeyedSingleton<IEmbeddingsModel>(AiProviderNames.Http, (sp, _) => sp.GetRequiredService<HttpEmbeddingProvider>());
        services.AddKeyedScoped<ITranscriptionModel>(AiProviderNames.Http, (sp, _) => sp.GetRequiredService<HttpAudioTranscriptionProvider>());
        services.AddKeyedScoped<IVisionModel>(AiProviderNames.Http, (sp, _) => sp.GetRequiredService<VisionEngine>());

        services.AddKeyedSingleton<IChatModel>(AiProviderNames.Mock, (sp, _) => sp.GetRequiredService<HttpMockChatModel>());
        services.AddKeyedSingleton<IEmbeddingsModel>(AiProviderNames.Mock, (sp, _) => sp.GetRequiredService<HttpMockEmbeddingsModel>());
        services.AddKeyedScoped<ITranscriptionModel>(AiProviderNames.Mock, (sp, _) => sp.GetRequiredService<HttpMockTranscriptionModel>());
        services.AddKeyedSingleton<IVisionModel>(AiProviderNames.Mock, (sp, _) => sp.GetRequiredService<HttpMockVisionModel>());

        services.AddKeyedSingleton<IChatModel>(AiProviderNames.Offline, (sp, _) => sp.GetRequiredService<OfflineChatModel>());
        services.AddKeyedSingleton<IEmbeddingsModel>(AiProviderNames.Offline, (sp, _) => sp.GetRequiredService<OfflineEmbeddingsModel>());
        services.AddKeyedScoped<ITranscriptionModel>(AiProviderNames.Offline, (sp, _) => sp.GetRequiredService<OfflineTranscriptionModel>());
        services.AddKeyedSingleton<IVisionModel>(AiProviderNames.Offline, (sp, _) => sp.GetRequiredService<OfflineVisionModel>());

        services.AddKeyedSingleton<IChatModel>(AiProviderNames.Local, (sp, _) => sp.GetRequiredService<EchoLlmProvider>());
        services.AddKeyedSingleton<IEmbeddingsModel>(AiProviderNames.Local, (sp, _) => sp.GetRequiredService<DeterministicEmbeddingProvider>());
        services.AddKeyedScoped<ITranscriptionModel>(AiProviderNames.Local, (sp, _) => sp.GetRequiredService<StubAudioTranscriptionProvider>());
        services.AddKeyedSingleton<IChatModel>(AiProviderNames.Inactive, (sp, _) => sp.GetRequiredService<InactiveChatModel>());
        services.AddKeyedSingleton<IEmbeddingsModel>(AiProviderNames.Inactive, (sp, _) => sp.GetRequiredService<InactiveEmbeddingsModel>());
        services.AddKeyedScoped<ITranscriptionModel>(AiProviderNames.Inactive, (sp, _) => sp.GetRequiredService<InactiveTranscriptionModel>());
        services.AddKeyedSingleton<IVisionModel>(AiProviderNames.Inactive, (sp, _) => sp.GetRequiredService<InactiveVisionModel>());

        services.AddScoped<AiModelFactory>();
        services.AddScoped<IChatModel>(sp => sp.GetRequiredService<AiModelFactory>());
        services.AddScoped<IEmbeddingsModel>(sp => sp.GetRequiredService<AiModelFactory>());
        services.AddScoped<ITranscriptionModel>(sp => sp.GetRequiredService<AiModelFactory>());
        services.AddScoped<IVisionModel>(sp => sp.GetRequiredService<AiModelFactory>());
        services.AddScoped<ILLMProvider>(sp => sp.GetRequiredService<IChatModel>());
        services.AddScoped<IEmbeddingProvider>(sp => sp.GetRequiredService<IEmbeddingsModel>());
        services.AddScoped<IAudioTranscriptionProvider>(sp => sp.GetRequiredService<ITranscriptionModel>());
        services.TryAddScoped<IAionVisionService>(sp => sp.GetRequiredService<VisionEngine>());
        services.TryAddScoped<IVisionService>(sp => sp.GetRequiredService<IVisionModel>());

        services.AddScoped<IIntentDetector, IntentRecognizer>();
        services.AddScoped<IIntentRouter, IntentRouter>();
        services.AddScoped<IModuleDesigner, ModuleDesigner>();
        services.AddScoped<IModuleSpecDesigner, ModuleSpecDesigner>();
        services.AddScoped<IModuleDesignService, ModuleDesignService>();
        services.AddScoped<IModuleDesignerService, ModuleDesignerService>();
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
