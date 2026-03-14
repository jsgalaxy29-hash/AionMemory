using System;
using System.Collections;
using System.Text.Json;

namespace Aion.AI;

/// <summary>
/// Utility methods for deterministic JSON serialization, parsing and extraction from LLM responses.
/// </summary>
public static class JsonHelper
{
    /// <summary>
    /// Shared JSON options used across AI orchestrators.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultOptions = CreateDefaultOptions();

    /// <summary>
    /// Serializes an instance to JSON using shared options.
    /// </summary>
    /// <typeparam name="T">Type to serialize.</typeparam>
    /// <param name="value">Value to serialize.</param>
    /// <returns>Serialized JSON payload.</returns>
    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, DefaultOptions);

    /// <summary>
    /// Deserializes JSON into a typed instance using shared options.
    /// </summary>
    /// <typeparam name="T">Target type.</typeparam>
    /// <param name="json">JSON payload.</param>
    /// <returns>The deserialized instance or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">Thrown when JSON is invalid for <typeparamref name="T"/>.</exception>
    public static T? Deserialize<T>(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<T>(json, DefaultOptions);
    }

    /// <summary>
    /// Attempts to deserialize JSON into a typed instance.
    /// </summary>
    /// <typeparam name="T">Target type.</typeparam>
    /// <param name="json">JSON payload.</param>
    /// <param name="result">Deserialized value when successful.</param>
    /// <returns><see langword="true"/> when deserialization succeeds; otherwise <see langword="false"/>.</returns>
    public static bool TryDeserialize<T>(string? json, out T? result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            result = JsonSerializer.Deserialize<T>(json, DefaultOptions);
            return result is not null;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts the first complete JSON object (<c>{ ... }</c>) from raw LLM text.
    /// </summary>
    /// <param name="rawText">Raw text containing an embedded JSON object.</param>
    /// <returns>Extracted JSON object text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rawText"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no valid JSON object can be extracted.</exception>
    public static string ExtractJsonObject(string rawText)
    {
        ArgumentNullException.ThrowIfNull(rawText);

        if (TryExtractFirstValidPayload(rawText, '{', '}', JsonValueKind.Object, out var json))
        {
            return json;
        }

        throw new InvalidOperationException("No valid JSON object was found in the provided text.");
    }

    /// <summary>
    /// Extracts the first complete JSON array (<c>[ ... ]</c>) from raw LLM text.
    /// </summary>
    /// <param name="rawText">Raw text containing an embedded JSON array.</param>
    /// <returns>Extracted JSON array text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rawText"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no valid JSON array can be extracted.</exception>
    public static string ExtractJsonArray(string rawText)
    {
        ArgumentNullException.ThrowIfNull(rawText);

        if (TryExtractFirstValidPayload(rawText, '[', ']', JsonValueKind.Array, out var json))
        {
            return json;
        }

        throw new InvalidOperationException("No valid JSON array was found in the provided text.");
    }

    /// <summary>
    /// Extracts the first valid JSON object or array from raw LLM text.
    /// </summary>
    /// <param name="rawText">Raw text containing JSON.</param>
    /// <returns>Extracted JSON payload, or trimmed input when extraction fails.</returns>
    public static string ExtractJson(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        var firstObjectIndex = rawText.IndexOf('{');
        var firstArrayIndex = rawText.IndexOf('[');

        if (firstObjectIndex < 0 && firstArrayIndex < 0)
        {
            return rawText.Trim();
        }

        var preferObject = firstArrayIndex < 0 || (firstObjectIndex >= 0 && firstObjectIndex < firstArrayIndex);

        if (preferObject)
        {
            if (TryExtractFirstValidPayload(rawText, '{', '}', JsonValueKind.Object, out var jsonObject))
            {
                return jsonObject;
            }

            if (TryExtractFirstValidPayload(rawText, '[', ']', JsonValueKind.Array, out var jsonArray))
            {
                return jsonArray;
            }
        }
        else
        {
            if (TryExtractFirstValidPayload(rawText, '[', ']', JsonValueKind.Array, out var jsonArray))
            {
                return jsonArray;
            }

            if (TryExtractFirstValidPayload(rawText, '{', '}', JsonValueKind.Object, out var jsonObject))
            {
                return jsonObject;
            }
        }

        return rawText.Trim();
    }

    /// <summary>
    /// Attempts to parse a JSON string into a <see cref="JsonDocument"/>.
    /// </summary>
    /// <param name="json">JSON payload.</param>
    /// <param name="document">Parsed document when successful.</param>
    /// <returns><see langword="true"/> when parsing succeeds; otherwise <see langword="false"/>.</returns>
    public static bool TryParseDocument(string? json, out JsonDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            document = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether a text looks like a valid JSON payload.
    /// </summary>
    /// <param name="text">Text to inspect.</param>
    /// <returns><see langword="true"/> when the text appears to be valid JSON object or array.</returns>
    public static bool LooksLikeJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        if (span.Length < 2)
        {
            return false;
        }

        var startsLikeJson = (span[0] == '{' && span[^1] == '}') || (span[0] == '[' && span[^1] == ']');
        if (!startsLikeJson)
        {
            return false;
        }

        return TryParseDocument(span.ToString(), out _);
    }

    /// <summary>
    /// Attempts to deserialize from JSON embedded in an LLM answer.
    /// </summary>
    /// <typeparam name="T">Target type.</typeparam>
    /// <param name="rawText">Raw text containing embedded JSON.</param>
    /// <returns>The deserialized object, or <see langword="null"/> when deserialization fails.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rawText"/> is <see langword="null"/>.</exception>
    public static T? DeserializeFromEmbeddedJson<T>(string rawText)
    {
        ArgumentNullException.ThrowIfNull(rawText);

        var expectsArray = typeof(T) != typeof(string)
            && typeof(IEnumerable).IsAssignableFrom(typeof(T))
            && typeof(T) != typeof(object);

        string extracted;
        try
        {
            extracted = expectsArray ? ExtractJsonArray(rawText) : ExtractJsonObject(rawText);
        }
        catch (InvalidOperationException)
        {
            extracted = expectsArray ? ExtractJsonObject(rawText) : ExtractJsonArray(rawText);
        }

        return Deserialize<T>(extracted);
    }

    private static JsonSerializerOptions CreateDefaultOptions()
        => new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

    private static bool TryExtractFirstValidPayload(
        string rawText,
        char opening,
        char closing,
        JsonValueKind expectedKind,
        out string json)
    {
        json = string.Empty;

        for (var start = 0; start < rawText.Length; start++)
        {
            if (rawText[start] != opening)
            {
                continue;
            }

            if (!TryFindBalancedSegment(rawText, start, opening, closing, out var end))
            {
                continue;
            }

            var candidate = rawText.Substring(start, end - start + 1);
            if (!TryParseDocument(candidate, out var document) || document is null)
            {
                continue;
            }

            using (document)
            {
                if (document.RootElement.ValueKind != expectedKind)
                {
                    continue;
                }
            }

            json = candidate;
            return true;
        }

        return false;
    }

    private static bool TryFindBalancedSegment(string text, int startIndex, char opening, char closing, out int endIndex)
    {
        endIndex = -1;
        var depth = 0;
        var inString = false;
        var isEscaped = false;

        for (var index = startIndex; index < text.Length; index++)
        {
            var current = text[index];

            if (inString)
            {
                if (isEscaped)
                {
                    isEscaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    isEscaped = true;
                    continue;
                }

                if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == opening)
            {
                depth++;
                continue;
            }

            if (current == closing)
            {
                depth--;
                if (depth == 0)
                {
                    endIndex = index;
                    return true;
                }

                if (depth < 0)
                {
                    return false;
                }
            }
        }

        return false;
    }
}
