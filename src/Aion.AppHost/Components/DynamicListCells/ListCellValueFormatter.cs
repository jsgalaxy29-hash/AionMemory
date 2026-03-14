using System.Globalization;
using System.Text.Json;

namespace Aion.AppHost.Components.DynamicListCells;

internal static class ListCellValueFormatter
{
    public static string ToText(object? value)
    {
        return value switch
        {
            null => string.Empty,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonElement element => element.ToString(),
            _ => value.ToString() ?? string.Empty
        };
    }

    public static string ToNumber(object? value)
        => TryGetLong(value, out var parsed) ? parsed.ToString(CultureInfo.InvariantCulture) : ToText(value);

    public static string ToDecimal(object? value)
        => TryGetDecimal(value, out var parsed) ? parsed.ToString("0.##", CultureInfo.InvariantCulture) : ToText(value);

    public static string ToBoolean(object? value)
        => TryGetBool(value, out var parsed) ? (parsed ? "Oui" : "Non") : string.Empty;

    public static string ToDate(object? value)
        => TryGetDate(value, out var parsed) ? parsed.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : ToText(value);

    public static string ToJsonSummary(object? value)
    {
        var text = value switch
        {
            JsonElement element => element.ToString(),
            null => string.Empty,
            _ => value.ToString() ?? string.Empty
        };

        if (text.Length <= 80)
        {
            return text;
        }

        return $"{text[..77]}...";
    }

    private static bool TryGetLong(object? value, out long parsed)
    {
        switch (value)
        {
            case long longValue:
                parsed = longValue;
                return true;
            case int intValue:
                parsed = intValue;
                return true;
            case decimal decimalValue:
                parsed = (long)decimalValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out parsed):
                return true;
            default:
                parsed = default;
                return false;
        }
    }

    private static bool TryGetDecimal(object? value, out decimal parsed)
    {
        switch (value)
        {
            case decimal decimalValue:
                parsed = decimalValue;
                return true;
            case double doubleValue:
                parsed = (decimal)doubleValue;
                return true;
            case float floatValue:
                parsed = (decimal)floatValue;
                return true;
            case int intValue:
                parsed = intValue;
                return true;
            case long longValue:
                parsed = longValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out parsed):
                return true;
            default:
                parsed = default;
                return false;
        }
    }

    private static bool TryGetBool(object? value, out bool parsed)
    {
        switch (value)
        {
            case bool boolValue:
                parsed = boolValue;
                return true;
            case JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False:
                parsed = element.GetBoolean();
                return true;
            default:
                parsed = default;
                return false;
        }
    }

    private static bool TryGetDate(object? value, out DateTime parsed)
    {
        switch (value)
        {
            case DateTime dateValue:
                parsed = dateValue;
                return true;
            case DateTimeOffset dateTimeOffset:
                parsed = dateTimeOffset.DateTime;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String && DateTime.TryParse(element.GetString(), out parsed):
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var timestamp):
                parsed = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
                return true;
            default:
                parsed = default;
                return false;
        }
    }
}
