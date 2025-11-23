using Aion.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aion.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAionAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AionAiOptions>().Bind(configuration.GetSection("Aion:Ai"));

        services.AddHttpClient(HttpClientNames.Llm, ConfigureClient(HttpClientNames.Llm));
        services.AddHttpClient(HttpClientNames.Embeddings, ConfigureClient(HttpClientNames.Embeddings));
        services.AddHttpClient(HttpClientNames.Transcription, ConfigureClient(HttpClientNames.Transcription));
        services.AddHttpClient(HttpClientNames.Vision, ConfigureClient(HttpClientNames.Vision));

        services.AddSingleton<ITextGenerationProvider, HttpTextGenerationProvider>();
        services.AddSingleton<IEmbeddingProvider, HttpEmbeddingProvider>();
        services.AddScoped<IAudioTranscriptionProvider, HttpAudioTranscriptionProvider>();
        services.AddSingleton<HttpVisionProvider>();
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
}
