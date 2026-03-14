using System.Text.Json;

namespace Aion.AppHost.Components.DynamicFields;

internal static class FieldValueConverters
{
    public static string? ToStringValue(object? value)
    {
        return value switch
        {
            null => null,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element => element.ToString(),
            _ => value.ToString()
        };
    }

    public static long? ToLongValue(object? value)
    {
        return value switch
        {
            null => null,
            long longValue => longValue,
            int intValue => intValue,
            decimal decimalValue => (long)decimalValue,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var parsed) => parsed,
            _ => null
        };
    }

    public static decimal? ToDecimalValue(object? value)
    {
        return value switch
        {
            null => null,
            decimal decimalValue => decimalValue,
            double doubleValue => (decimal)doubleValue,
            float floatValue => (decimal)floatValue,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var parsed) => parsed,
            _ => null
        };
    }

    public static bool ToBoolValue(object? value)
    {
        return value switch
        {
            bool boolValue => boolValue,
            JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            _ => false
        };
    }

    public static DateTime? ToDateTimeValue(object? value)
    {
        return value switch
        {
            DateTime dateValue => dateValue,
            JsonElement element when element.ValueKind == JsonValueKind.String && DateTime.TryParse(element.GetString(), out var parsed) => parsed,
            JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var timestamp) => DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime,
            _ => null
        };
    }
}
