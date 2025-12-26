using System.Diagnostics.Metrics;
using System.Text.Json;

namespace Aion.AI.Observability;

internal static class AiMetrics
{
    private static readonly Meter Meter = new("AionMemory.AI");
    private static readonly Histogram<double> RequestDurationMs = Meter.CreateHistogram<double>(
        "aion.ai.request.duration_ms",
        unit: "ms",
        description: "Latency of AI provider requests.");
    private static readonly Counter<long> RequestErrors = Meter.CreateCounter<long>(
        "aion.ai.request.errors",
        description: "AI provider request errors.");
    private static readonly Counter<long> RequestRetries = Meter.CreateCounter<long>(
        "aion.ai.request.retries",
        description: "AI provider retry attempts.");
    private static readonly Counter<long> Tokens = Meter.CreateCounter<long>(
        "aion.ai.tokens",
        description: "AI token usage when reported by providers.");
    private static readonly Histogram<double> Costs = Meter.CreateHistogram<double>(
        "aion.ai.request.cost",
        unit: "currency",
        description: "AI request cost when providers return it.");

    internal static void RecordLatency(string operation, string provider, string? model, TimeSpan elapsed)
    {
        RequestDurationMs.Record(elapsed.TotalMilliseconds, BuildTags(operation, provider, model));
    }

    internal static void RecordError(string operation, string provider, string? model)
    {
        RequestErrors.Add(1, BuildTags(operation, provider, model));
    }

    internal static void RecordRetry(string operation, string provider)
    {
        RequestRetries.Add(1, new TagList
        {
            { "operation", operation },
            { "provider", provider }
        });
    }

    internal static void RecordUsageFromJson(string? rawJson, string operation, string provider, string? model)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (TryGetLong(usage, "total_tokens", out var totalTokens))
            {
                Tokens.Add(totalTokens, BuildTags(operation, provider, model));
            }
            else
            {
                var promptTokens = TryGetLong(usage, "prompt_tokens", out var prompt) ? prompt : 0;
                var completionTokens = TryGetLong(usage, "completion_tokens", out var completion) ? completion : 0;
                var sum = promptTokens + completionTokens;
                if (sum > 0)
                {
                    Tokens.Add(sum, BuildTags(operation, provider, model));
                }
            }

            if (TryGetDouble(usage, "total_cost", out var cost)
                || TryGetDouble(usage, "cost", out cost)
                || TryGetDouble(usage, "total_cost_usd", out cost))
            {
                Costs.Record(cost, BuildTags(operation, provider, model));
            }
        }
        catch (JsonException)
        {
        }
    }

    private static TagList BuildTags(string operation, string provider, string? model)
    {
        var tags = new TagList
        {
            { "operation", operation },
            { "provider", provider }
        };

        if (!string.IsNullOrWhiteSpace(model))
        {
            tags.Add("model", model);
        }

        return tags;
    }

    private static bool TryGetLong(JsonElement element, string propertyName, out long value)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }
}
