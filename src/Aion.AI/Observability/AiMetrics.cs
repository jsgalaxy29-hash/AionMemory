using System.Diagnostics.Metrics;

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
        var usage = AiUsageParser.Extract(rawJson);
        if (usage.tokens is { } tokens)
        {
            Tokens.Add(tokens, BuildTags(operation, provider, model));
        }

        if (usage.cost is { } cost)
        {
            Costs.Record(cost, BuildTags(operation, provider, model));
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

}
