using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Aion.AI;
using Aion.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.AI.Adapters;

public static class AiAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddAiAdapters(this IServiceCollection services)
    {
        services.AddOptions<AionAiOptions>();
        services.TryAddSingleton<IOperationScopeFactory, NoopOperationScopeFactory>();
        services.TryAddSingleton<EchoLlmProvider>();
        services.TryAddSingleton<DeterministicEmbeddingProvider>();
        services.TryAddScoped<StubAudioTranscriptionProvider>();
        services.TryAddSingleton<IChatModel>(sp => sp.GetRequiredService<EchoLlmProvider>());
        services.TryAddSingleton<ILLMProvider>(sp => sp.GetRequiredService<IChatModel>());
        services.TryAddSingleton<IEmbeddingsModel>(sp => sp.GetRequiredService<DeterministicEmbeddingProvider>());
        services.TryAddSingleton<IEmbeddingProvider>(sp => sp.GetRequiredService<IEmbeddingsModel>());
        services.TryAddScoped<ITranscriptionModel>(sp => sp.GetRequiredService<StubAudioTranscriptionProvider>());
        services.TryAddScoped<IAudioTranscriptionProvider>(sp => sp.GetRequiredService<ITranscriptionModel>());

        services.TryAddScoped<IIntentDetector, BasicIntentDetector>();
        services.TryAddScoped<IModuleDesigner, SimpleModuleDesigner>();
        services.TryAddScoped<ICrudInterpreter, SimpleCrudInterpreter>();
        services.TryAddScoped<IAgendaInterpreter, SimpleAgendaInterpreter>();
        services.TryAddScoped<INoteInterpreter, SimpleNoteInterpreter>();
        services.TryAddScoped<IReportInterpreter, SimpleReportInterpreter>();
        services.TryAddScoped<ITranscriptionMetadataInterpreter, TranscriptionMetadataInterpreter>();
        services.TryAddScoped<IMemoryAnalyzer, MemoryAnalyzer>();
        services.TryAddScoped<IMemoryContextBuilder, MemoryContextBuilder>();
        services.TryAddScoped<IChatAnswerer, ChatAnswerer>();

        return services;
    }
}

public sealed class EchoLlmProvider : IChatModel
{
    public Task<LlmResponse> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var content = $"[stub] {prompt}";
        return Task.FromResult(new LlmResponse(content, content));
    }
}

public sealed class DeterministicEmbeddingProvider : IEmbeddingsModel
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

public sealed class StubAudioTranscriptionProvider : ITranscriptionModel
{
    public Task<TranscriptionResult> TranscribeAsync(Stream audioStream, string fileName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new TranscriptionResult($"Transcription simulée pour {fileName}", TimeSpan.Zero, "stub-transcription"));
    }
}

public sealed class BasicIntentDetector : IIntentDetector
{
    private readonly IChatModel _provider;
    private readonly ILogger<BasicIntentDetector> _logger;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly IOperationScopeFactory _operationScopeFactory;

    public BasicIntentDetector(IChatModel provider, ILogger<BasicIntentDetector> logger, IOptionsMonitor<AionAiOptions> options, IOperationScopeFactory operationScopeFactory)
    {
        _provider = provider;
        _logger = logger;
        _options = options;
        _operationScopeFactory = operationScopeFactory;
    }

    public async Task<IntentDetectionResult> DetectAsync(IntentDetectionRequest request, CancellationToken cancellationToken = default)
    {
        var safePrompt = request.Input.ToSafeLogValue(_options.CurrentValue, _logger);
        using var operation = _operationScopeFactory.Start("AI.BasicIntent");
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Operation"] = "AI.BasicIntent",
            ["CorrelationId"] = operation.Context.CorrelationId,
            ["OperationId"] = operation.Context.OperationId,
            ["Prompt"] = safePrompt
        }) ?? NullScope.Instance;
        var stopwatch = Stopwatch.StartNew();

        var prompt = $"Analyse l'intention: {request.Input}. Réponds uniquement en JSON strict: {StructuredJsonSchemas.Intent.Description}";
        var structured = await StructuredJsonResponseHandler.GetValidJsonAsync(
            _provider,
            prompt,
            StructuredJsonSchemas.Intent,
            _logger,
            cancellationToken).ConfigureAwait(false);
        var raw = structured.RawResponse ?? structured.Json ?? string.Empty;
        var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

        if (structured.IsValid && TryParseIntent(structured.Json, out var parsed))
        {
            _logger.LogInformation("Intent detection parsed response in {ElapsedMs}ms via {Provider}", elapsedMs, _provider.GetType().Name);
            return parsed with { RawResponse = raw };
        }

        _logger.LogWarning("Intent not parsed, fallback to chat intent after {ElapsedMs}ms ({Prompt})", elapsedMs, safePrompt);
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

        var trimmedStart = normalized.TrimStart();
        if (trimmedStart.Length == 0)
        {
            return false;
        }

        var firstChar = trimmedStart[0];
        if (firstChar is not ('{' or '[' or '"'))
        {
            return false;
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

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
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
                        new S_Field { Name = "Title", Label = "Titre", DataType = FieldDataType.Text },
                        new S_Field { Name = "Notes", Label = "Notes", DataType = FieldDataType.Text }
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
            Start = DateTimeOffset.Now.AddHours(1),
            End = DateTimeOffset.Now.AddHours(2)
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
