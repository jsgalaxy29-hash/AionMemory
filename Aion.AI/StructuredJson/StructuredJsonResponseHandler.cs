using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Aion.AI;

internal delegate bool StructuredJsonValidator(JsonElement element, out string? error);

internal sealed record StructuredJsonSchema(string Name, string Description, StructuredJsonValidator Validator, int MaxAttempts = 3)
{
    public string BuildCorrectionPrompt(string invalidJson, string? error)
        => $"""
Tu es un assistant qui corrige des réponses IA.
Corrige UNIQUEMENT le JSON ci-dessous pour respecter strictement le schéma suivant:
{Description}
Erreur détectée: {error ?? "Non conforme"}
JSON à corriger:
{invalidJson}
Réponds uniquement avec le JSON valide, sans texte additionnel.
""";
}

internal sealed record StructuredJsonResult(string? Json, string? RawResponse, bool IsValid, int Attempts, string? Error);

internal static class StructuredJsonResponseHandler
{
    public static async Task<StructuredJsonResult> GetValidJsonAsync(
        ILLMProvider provider,
        string initialPrompt,
        StructuredJsonSchema schema,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var prompt = initialPrompt;
        string? lastJson = null;
        string? lastError = null;
        string? lastRaw = null;

        for (var attempt = 1; attempt <= schema.MaxAttempts; attempt++)
        {
            var response = await provider.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
            lastRaw = response.RawResponse ?? response.Content;
            lastJson = JsonHelper.ExtractJson(response.Content);

            if (TryValidate(lastJson, schema, out var error))
            {
                return new StructuredJsonResult(lastJson, lastRaw, true, attempt, null);
            }

            lastError = error;
            if (attempt < schema.MaxAttempts)
            {
                prompt = schema.BuildCorrectionPrompt(lastJson, error);
                continue;
            }
        }

        logger.LogWarning("Structured JSON validation failed after {Attempts} attempts for {Schema}", schema.MaxAttempts, schema.Name);
        return new StructuredJsonResult(lastJson, lastRaw, false, schema.MaxAttempts, lastError);
    }

    private static bool TryValidate(string? json, StructuredJsonSchema schema, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "JSON vide";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                root = root[0];
            }

            if (!schema.Validator(root, out error))
            {
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
