using System.Linq;
using System.Text.Json;
using Aion.Domain;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services;

public sealed class VisionSuggestionService : IVisionSuggestionService
{
    private static readonly IReadOnlyDictionary<string, string> LabelModuleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["invoice"] = "Finances",
        ["receipt"] = "Finances",
        ["bill"] = "Finances",
        ["budget"] = "Finances",
        ["bank"] = "Finances",
        ["garden"] = "Potager",
        ["plant"] = "Potager",
        ["vegetable"] = "Potager",
        ["contact"] = "Contacts",
        ["person"] = "Contacts",
        ["portrait"] = "Contacts",
        ["profile"] = "Contacts",
        ["travel"] = "Voyages",
        ["flight"] = "Voyages",
        ["hotel"] = "Voyages",
        ["recipe"] = "Cuisine",
        ["food"] = "Cuisine"
    };

    private readonly IMetadataService _metadata;
    private readonly ILogger<VisionSuggestionService> _logger;

    public VisionSuggestionService(IMetadataService metadata, ILogger<VisionSuggestionService> logger)
    {
        _metadata = metadata;
        _logger = logger;
    }

    public async Task<VisionSuggestionResult> SuggestModulesAsync(S_VisionAnalysis analysis, CancellationToken cancellationToken = default)
    {
        var labels = ExtractLabels(analysis.ResultJson);
        if (labels.Count == 0)
        {
            return new VisionSuggestionResult(labels, Array.Empty<VisionModuleSuggestion>());
        }

        var modules = (await _metadata.GetModulesAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var moduleLookup = modules.ToDictionary(module => Normalize(module.Name), module => module);
        var suggestions = new List<VisionModuleSuggestion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var label in labels)
        {
            var normalizedLabel = Normalize(label.Label);
            if (string.IsNullOrWhiteSpace(normalizedLabel))
            {
                continue;
            }

            foreach (var (keyword, moduleSlug) in LabelModuleMap)
            {
                if (!normalizedLabel.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddSuggestion(moduleSlug, label, moduleLookup, suggestions, seen);
            }

            foreach (var module in modules)
            {
                var moduleKey = Normalize(module.Name);
                if (string.IsNullOrWhiteSpace(moduleKey))
                {
                    continue;
                }

                if (normalizedLabel.Contains(moduleKey, StringComparison.OrdinalIgnoreCase))
                {
                    AddSuggestion(module.Name, label, moduleLookup, suggestions, seen);
                }
            }
        }

        return new VisionSuggestionResult(labels, suggestions);
    }

    private static void AddSuggestion(
        string moduleSlug,
        VisionLabel label,
        IReadOnlyDictionary<string, S_Module> moduleLookup,
        ICollection<VisionModuleSuggestion> suggestions,
        ISet<string> seen)
    {
        if (!seen.Add(moduleSlug))
        {
            return;
        }

        var normalized = Normalize(moduleSlug);
        moduleLookup.TryGetValue(normalized, out var module);
        var moduleName = module?.Name ?? moduleSlug;
        var reason = $"Le label '{label.Label}' correspond au module '{moduleName}'.";

        suggestions.Add(new VisionModuleSuggestion(
            module?.Id,
            moduleSlug,
            moduleName,
            reason,
            label.Label,
            label.Confidence));
    }

    private IReadOnlyCollection<VisionLabel> ExtractLabels(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return Array.Empty<VisionLabel>();
        }

        try
        {
            using var document = JsonDocument.Parse(resultJson);
            return ExtractLabels(document.RootElement);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Unable to parse vision JSON for labels");
            return Array.Empty<VisionLabel>();
        }
    }

    private static IReadOnlyCollection<VisionLabel> ExtractLabels(JsonElement root)
    {
        var labels = new List<VisionLabel>();
        if (root.ValueKind == JsonValueKind.Object)
        {
            ExtractFromProperty(root, "labels", labels);
            ExtractFromProperty(root, "tags", labels);
            ExtractFromProperty(root, "classes", labels);
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            ExtractFromArray(root, labels);
        }

        return labels
            .Where(label => !string.IsNullOrWhiteSpace(label.Label))
            .DistinctBy(label => label.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ExtractFromProperty(JsonElement root, string name, ICollection<VisionLabel> labels)
    {
        if (root.TryGetProperty(name, out var element))
        {
            ExtractFromArray(element, labels);
        }
    }

    private static void ExtractFromArray(JsonElement element, ICollection<VisionLabel> labels)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in element.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                    labels.Add(new VisionLabel(item.GetString() ?? string.Empty));
                    break;
                case JsonValueKind.Object:
                    var label = TryGetString(item, "label")
                        ?? TryGetString(item, "name")
                        ?? TryGetString(item, "tag")
                        ?? TryGetString(item, "class");
                    var confidence = TryGetDouble(item, "confidence")
                        ?? TryGetDouble(item, "score")
                        ?? TryGetDouble(item, "probability");
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        labels.Add(new VisionLabel(label, confidence));
                    }
                    break;
            }
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Number when prop.TryGetDouble(out var value) => value,
            JsonValueKind.String when double.TryParse(prop.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static string Normalize(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
