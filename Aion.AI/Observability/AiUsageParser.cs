using System.Text.Json;

namespace Aion.AI.Observability;

internal static class AiUsageParser
{
    internal static (long? tokens, double? cost) Extract(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            long? tokens = null;
            if (TryGetLong(usage, "total_tokens", out var totalTokens))
            {
                tokens = totalTokens > 0 ? totalTokens : null;
            }
            else
            {
                var promptTokens = TryGetLong(usage, "prompt_tokens", out var prompt) ? prompt : 0;
                var completionTokens = TryGetLong(usage, "completion_tokens", out var completion) ? completion : 0;
                var sum = promptTokens + completionTokens;
                tokens = sum > 0 ? sum : null;
            }

            double? cost = null;
            if (TryGetDouble(usage, "total_cost", out var parsedCost)
                || TryGetDouble(usage, "cost", out parsedCost)
                || TryGetDouble(usage, "total_cost_usd", out parsedCost))
            {
                cost = parsedCost;
            }

            return (tokens, cost);
        }
        catch (JsonException)
        {
            return (null, null);
        }
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
