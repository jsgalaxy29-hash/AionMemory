using System;
using System.Linq;
using System.Text.Json;

namespace Aion.AI;

internal static class EmbeddingPayload
{
    public static string NormalizeInput(string? text)
        => string.IsNullOrWhiteSpace(text) ? " " : text;

    public static bool TryReadVector(string json, out float[] vector)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind is not JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            vector = Array.Empty<float>();
            return false;
        }

        var first = data[0];
        if (!first.TryGetProperty("embedding", out var embedding) || embedding.ValueKind is not JsonValueKind.Array)
        {
            vector = Array.Empty<float>();
            return false;
        }

        vector = embedding.EnumerateArray().Select(e => e.GetSingle()).ToArray();
        return vector.Length > 0;
    }
}
