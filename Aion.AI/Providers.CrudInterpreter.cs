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

public sealed class CrudInterpreter : ICrudInterpreter
{
    private readonly IChatModel _provider;
    private readonly ILogger<CrudInterpreter> _logger;
    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase) { "create", "update", "delete", "query" };

    public CrudInterpreter(IChatModel provider, ILogger<CrudInterpreter> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<CrudInterpretation> GenerateQueryAsync(CrudQueryRequest request, CancellationToken cancellationToken = default)
    {
        var fieldDescriptions = request.Module.EntityTypes
            .SelectMany(e => e.Fields.Select(f => $"{e.Name}.{f.Name}:{f.DataType}"));
        var prompt = $@"Tu traduis les instructions utilisateur en opérations CRUD structurées.
Renvoie UNIQUEMENT un JSON respectant {{""action"":""create|update|delete|query"",""filters"":{{}},""payload"":{{}}}}.
Module: {request.Module.Name}
Champs disponibles: {string.Join(", ", fieldDescriptions)}
Requête: {request.Intent}";

        var response = await _provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        var cleaned = JsonHelper.ExtractJson(response.Content);

        if (TryParseCrud(cleaned, out var interpretation))
        {
            return interpretation with { RawResponse = response.RawResponse ?? cleaned };
        }

        _logger.LogWarning("Unable to parse CRUD interpretation, returning fallback");
        return new CrudInterpretation("query", new Dictionary<string, string?> { ["raw"] = request.Intent }, new Dictionary<string, string?>(), response.RawResponse ?? cleaned);
    }

    private bool TryParseCrud(string cleaned, out CrudInterpretation interpretation)
    {
        interpretation = default;
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
            var action = root.TryGetProperty("action", out var actionProperty) ? actionProperty.GetString() ?? "query" : "query";
            action = NormalizeAction(action);
            var filters = ExtractStringDictionary(root, "filters");
            var payload = ExtractStringDictionary(root, "payload");
            interpretation = new CrudInterpretation(action, filters, payload, cleaned);
            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "CRUD interpretation parse error");
            return false;
        }
    }

    private static string NormalizeAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return "query";
        }

        var normalized = action.Trim();
        return AllowedActions.Contains(normalized) ? normalized : "query";
    }

    private static Dictionary<string, string?> ExtractStringDictionary(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string?>();
        }

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in element.EnumerateObject())
        {
            result[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => prop.Value.GetRawText()
            };
        }

        return result;
    }
}
