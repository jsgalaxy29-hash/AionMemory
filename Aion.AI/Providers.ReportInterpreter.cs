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

public sealed class ReportInterpreter : IReportInterpreter
{
    private static readonly HashSet<string> AllowedVisualizations = new(StringComparer.OrdinalIgnoreCase) { "table", "chart", "number", "list" };
    private readonly IChatModel _provider;
    private readonly ILogger<HttpTextGenerationProvider> _logger;

    public ReportInterpreter(IChatModel provider, ILogger<HttpTextGenerationProvider> logger)
    {
        _provider = provider;
        _logger = logger;
    }
    public async Task<ReportBuildResult> BuildReportAsync(ReportBuildRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = $@"Génère la définition d'un rapport AION. Réponds UNIQUEMENT en JSON compact suivant {{""query"":""SQL ou pseudo-SQL"",""visualization"":""table|chart|number|list""}}.
Description: {request.Description}
ModuleId: {request.ModuleId}
Préférence de visualisation: {request.PreferredVisualization ?? "none"}";
        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        var cleaned = JsonHelper.ExtractJson(response.Content);
        if (TryParseReport(request, cleaned, out var parsed))
        {
            return parsed with { RawResponse = response.RawResponse ?? cleaned };
        }

        return new ReportBuildResult(new S_ReportDefinition { ModuleId = request.ModuleId, Name = request.Description, QueryDefinition = "select *", Visualization = request.PreferredVisualization }, response.RawResponse ?? response.Content);
    }

    private bool TryParseReport(ReportBuildRequest request, string cleaned, out ReportBuildResult result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                ? doc.RootElement[0]
                : doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var content = choices[0].GetProperty("message").GetProperty("content").GetString();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    return TryParseReport(request, JsonHelper.ExtractJson(content), out result);
                }
            }

            var query = root.TryGetProperty("query", out var queryProp) ? queryProp.GetString() : null;
            var visualization = root.TryGetProperty("visualization", out var vizProp) ? vizProp.GetString() : null;
            visualization = NormalizeVisualization(visualization, request.PreferredVisualization);
            var reportDefinition = new S_ReportDefinition
            {
                ModuleId = request.ModuleId,
                Name = request.Description,
                QueryDefinition = string.IsNullOrWhiteSpace(query) ? "select *" : query,
                Visualization = visualization
            };

            result = new ReportBuildResult(reportDefinition, cleaned);
            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse report response");
            return false;
        }
    }
    private sealed class ReportResponse
    {
        public string? Query { get; set; }
        public string? Visualization { get; set; }
    }

    private static string? NormalizeVisualization(string? visualization, string? preferred)
    {
        if (string.IsNullOrWhiteSpace(visualization))
        {
            return preferred;
        }

        var normalized = visualization.Trim();
        return AllowedVisualizations.Contains(normalized) ? normalized : preferred;
    }
}
