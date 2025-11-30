using System.Text.Json;
using Aion.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Adapters;

public static class AiAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddAiAdapters(this IServiceCollection services)
    {
        services.AddSingleton<ILLMProvider, EchoLlmProvider>();
        services.AddSingleton<IEmbeddingProvider, DeterministicEmbeddingProvider>();
        services.AddScoped<IAudioTranscriptionProvider, StubAudioTranscriptionProvider>();

        services.AddScoped<IIntentDetector, BasicIntentDetector>();
        services.AddScoped<IModuleDesigner, SimpleModuleDesigner>();
        services.AddScoped<ICrudInterpreter, SimpleCrudInterpreter>();
        services.AddScoped<IAgendaInterpreter, SimpleAgendaInterpreter>();
        services.AddScoped<INoteInterpreter, SimpleNoteInterpreter>();
        services.AddScoped<IReportInterpreter, SimpleReportInterpreter>();

        return services;
    }
}

public sealed class EchoLlmProvider : ILLMProvider
{
    public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var content = $"[stub] {prompt}";
        return Task.FromResult(new LlmResponse(content, content));
    }
}

public sealed class DeterministicEmbeddingProvider : IEmbeddingProvider
{
    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var seed = Math.Abs(text.GetHashCode());
        var vector = Enumerable.Range(0, 8)
            .Select(i => (float)((seed + i) % 100) / 10f)
            .ToArray();
        return Task.FromResult(new EmbeddingResult(vector, "stub-embedding"));
    }
}

public sealed class StubAudioTranscriptionProvider : IAudioTranscriptionProvider
{
    public Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TranscriptionResult($"Transcription simulée pour {fileName}", TimeSpan.Zero, "stub-transcription"));
    }
}

public sealed class BasicIntentDetector : IIntentDetector
{
    private readonly ILLMProvider _provider;
    private readonly ILogger<BasicIntentDetector> _logger;

    public BasicIntentDetector(ILLMProvider provider, ILogger<BasicIntentDetector> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<IntentDetectionResult> DetectAsync(IntentDetectionRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _provider.GenerateAsync($"Analyse l'intention: {request.Input}", cancellationToken).ConfigureAwait(false);
        var raw = response.RawResponse ?? response.Content;

        if (TryParseIntent(raw, out var parsed))
        {
            return parsed with { RawResponse = raw };
        }

        _logger.LogDebug("Intent not parsed, fallback to chat intent for '{Input}'", request.Input);
        return new IntentDetectionResult("chat", new Dictionary<string, string> { ["query"] = request.Input }, 0.42, raw);
    }

    private static bool TryParseIntent(string? raw, out IntentDetectionResult result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = raw.Trim();
        if (normalized.StartsWith("```") && normalized.EndsWith("```"))
        {
            normalized = normalized.Trim('`', '\n', '\r');
        }

        try
        {
            var json = JsonDocument.Parse(normalized);
            var root = json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("intent", out _)
                ? json.RootElement
                : json.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
                    ? choices[0].GetProperty("message").GetProperty("content")
                    : json.RootElement;

            var content = root.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(root.GetString() ?? "{}").RootElement
                : root;

            var intent = content.TryGetProperty("intent", out var intentProp)
                ? intentProp.GetString()
                : null;
            var confidence = content.TryGetProperty("confidence", out var confidenceProp)
                ? confidenceProp.GetDouble()
                : 0.5;
            var parameters = content.TryGetProperty("parameters", out var parametersProp)
                ? parametersProp.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty)
                : new Dictionary<string, string>();

            if (intent is null)
            {
                return false;
            }

            result = new IntentDetectionResult(intent, parameters, confidence, normalized);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed class SimpleModuleDesigner : IModuleDesigner
{
    public string? LastGeneratedJson { get; private set; }

    public Task<ModuleDesignResult> GenerateModuleAsync(ModuleDesignRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = request.Prompt;
        var module = new S_Module
        {
            Name = prompt.Length > 0 ? prompt[..Math.Min(prompt.Length, 40)] : (request.ModuleNameHint ?? "Module IA"),
            Description = $"Module généré à partir de: {prompt}",
            EntityTypes =
            [
                new S_EntityType
                {
                    Name = "Item",
                    PluralName = "Items",
                    Fields =
                    [
                        new S_Field { Name = "Title", Label = "Titre", DataType = EnumSFieldType.String },
                        new S_Field { Name = "Notes", Label = "Notes", DataType = EnumSFieldType.String }
                    ]
                }
            ]
        };

        LastGeneratedJson = JsonSerializer.Serialize(module);
        return Task.FromResult(new ModuleDesignResult(module, LastGeneratedJson));
    }
}

public sealed class SimpleCrudInterpreter : ICrudInterpreter
{
    public Task<CrudInterpretation> GenerateQueryAsync(CrudQueryRequest request, CancellationToken cancellationToken = default)
    {
        var filters = new Dictionary<string, string?> { ["intent"] = request.Intent };
        var payload = new Dictionary<string, string?> { ["module"] = request.Module.Name };
        return Task.FromResult(new CrudInterpretation("query", filters, payload, request.Intent));
    }
}

public sealed class SimpleAgendaInterpreter : IAgendaInterpreter
{
    public Task<S_Event> CreateEventAsync(string input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new S_Event
        {
            Title = input,
            StartsAt = DateTimeOffset.Now.AddHours(1),
            EndsAt = DateTimeOffset.Now.AddHours(2)
        });
    }
}

public sealed class SimpleNoteInterpreter : INoteInterpreter
{
    public Task<S_Note> RefineNoteAsync(string title, string content, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new S_Note { Title = title, Content = $"{content}\n\n[Amélioré]" });
    }
}

public sealed class SimpleReportInterpreter : IReportInterpreter
{
    public Task<ReportBuildResult> BuildReportAsync(ReportBuildRequest request, CancellationToken cancellationToken = default)
    {
        var definition = new S_ReportDefinition
        {
            ModuleId = request.ModuleId,
            Name = request.Description.Length > 0 ? request.Description[..Math.Min(request.Description.Length, 80)] : "Rapport IA",
            QueryDefinition = "{}",
            Visualization = request.PreferredVisualization
        };

        var raw = JsonSerializer.Serialize(new { request.Description, request.ModuleId, request.PreferredVisualization });
        return Task.FromResult(new ReportBuildResult(definition, raw));
    }
}
