using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using Aion.Domain;
using Aion.AI.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.AI;

public sealed class IntentRecognizer : IIntentDetector
{
    private readonly IChatModel _provider;
    private readonly ILogger<IntentRecognizer> _logger;
    private readonly IOptionsMonitor<AionAiOptions> _options;
    private readonly IOperationScopeFactory _operationScopeFactory;

    public IntentRecognizer(IChatModel provider, ILogger<IntentRecognizer> logger, IOptionsMonitor<AionAiOptions> options, IOperationScopeFactory operationScopeFactory)
    {
        _provider = provider;
        _logger = logger;
        _options = options;
        _operationScopeFactory = operationScopeFactory;
    }

    public async Task<IntentDetectionResult> DetectAsync(IntentDetectionRequest request, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var safePrompt = request.Input.ToSafeLogValue(options, _logger);
        using var operation = _operationScopeFactory.Start("AI.IntentDetection");
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["Operation"] = "AI.IntentDetection",
            ["CorrelationId"] = operation.Context.CorrelationId,
            ["OperationId"] = operation.Context.OperationId,
            ["Prompt"] = safePrompt
        }) ?? NullScope.Instance;
        var stopwatch = Stopwatch.StartNew();

        var context = request.Context ?? new Dictionary<string, string?>();
        var contextLines = context.Count == 0
            ? "aucun contexte"
            : string.Join(", ", context.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        var prompt = string.Join("\n", new[]
        {
            "Tu es l'orchestrateur d'intentions AION. Identifie l'intention principale de l'utilisateur.",
            "Contraintes :",
            "- Réponds UNIQUEMENT avec un JSON compact sans texte additionnel.",
            $"- Schéma attendu: {StructuredJsonSchemas.Intent.Description}",
            $"- Intentions typiques: {IntentCatalog.PromptIntents}.",
            $"Entrée: \"{request.Input}\"",
            $"Contexte: {contextLines}",
            $"Locale: {request.Locale}"
        });

        StructuredJsonResult structuredJson;
        try
        {
            structuredJson = await StructuredJsonResponseHandler.GetValidJsonAsync(
                _provider,
                prompt,
                StructuredJsonSchemas.Intent,
                _logger,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException oce) when (cancellationToken.IsCancellationRequested && oce.CancellationToken == cancellationToken)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Intent detection failed after {ElapsedMs}ms ({Prompt})", stopwatch.Elapsed.TotalMilliseconds, safePrompt);
            return BuildFallback(request.Input, null, ex.Message, 0.05);
        }

        if (structuredJson.IsValid && TryParseIntent(structuredJson.Json, out var parsed))
        {
            _logger.LogInformation("Intent detection parsed response in {ElapsedMs}ms (confidence={Confidence:F2})", stopwatch.Elapsed.TotalMilliseconds, parsed.Confidence);
            return parsed with { RawResponse = structuredJson.RawResponse ?? structuredJson.Json ?? string.Empty };
        }

        _logger.LogWarning("Intent parsing failed after {ElapsedMs}ms ({Prompt})", stopwatch.Elapsed.TotalMilliseconds, safePrompt);
        return BuildFallback(request.Input, structuredJson.RawResponse ?? structuredJson.Json);
    }
    private bool TryParseIntent(string? json, out IntentDetectionResult result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var intent = root.TryGetProperty("intent", out var intentProp) ? intentProp.GetString() : null;
            intent = string.IsNullOrWhiteSpace(intent) ? null : intent.Trim();
            var confidence = root.TryGetProperty("confidence", out var confidenceProp) ? confidenceProp.GetDouble() : 0.5;
            confidence = double.IsNaN(confidence) || double.IsInfinity(confidence) ? 0.5 : Math.Clamp(confidence, 0d, 1d);
            Dictionary<string, string> parameters = new(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("parameters", out var parametersProp) && parametersProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var parameter in parametersProp.EnumerateObject())
                {
                    parameters[parameter.Name] = parameter.Value.ValueKind == JsonValueKind.String
                        ? parameter.Value.GetString() ?? string.Empty
                        : parameter.Value.GetRawText();
                }
            }

            if (intent is null)
            {
                return false;
            }

            var normalizedIntent = IntentCatalog.Normalize(intent);
            if (normalizedIntent.Name != intent)
            {
                parameters["raw_intent"] = intent;
            }

            if (normalizedIntent.Name == IntentCatalog.Unknown && !IntentCatalog.IsUnknownName(intent))
            {
                return false;
            }

            result = new IntentDetectionResult(normalizedIntent.Name, parameters, confidence, json);
            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Unable to parse intent JSON");
            return false;
        }
    }

    private static IntentDetectionResult BuildFallback(string input, string? rawResponse, string? error = null, double confidence = 0.1)
    {
        var guessed = IntentHeuristics.Detect(input);
        var parameters = new Dictionary<string, string>
        {
            ["raw"] = input,
            ["fallback"] = "heuristic"
        };
        if (!string.IsNullOrWhiteSpace(error))
        {
            parameters["error"] = error;
        }

        return new IntentDetectionResult(guessed.Name, parameters, confidence, rawResponse ?? string.Empty);
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
