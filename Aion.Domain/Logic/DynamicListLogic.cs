using System.Text.Json;

namespace Aion.Domain.Logic;

public static class DynamicListLogic
{
    public static Dictionary<string, object?> DeserializePayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, object?>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?> { ["raw"] = json };
        }
    }
}
