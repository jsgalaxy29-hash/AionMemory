using System.Text;
using System.Text.Json;
using Aion.Domain;
using Microsoft.Extensions.Logging;

namespace Aion.AI;

public sealed class ChatAnswerer : IChatAnswerer
{
    private readonly IChatModel _chatModel;
    private readonly IMemoryContextBuilder _contextBuilder;
    private readonly ILogger<ChatAnswerer> _logger;

    public ChatAnswerer(IChatModel chatModel, IMemoryContextBuilder contextBuilder, ILogger<ChatAnswerer> logger)
    {
        _chatModel = chatModel;
        _contextBuilder = contextBuilder;
        _logger = logger;
    }

    public async Task<AssistantAnswer> AnswerAsync(AssistantAnswerRequest request, CancellationToken cancellationToken = default)
    {
        var context = await _contextBuilder.BuildAsync(request.Context, cancellationToken).ConfigureAwait(false);
        if (context.IsEmpty)
        {
            var message = "Je n'ai trouvé aucune information en mémoire. Réponse impossible sans données.";
            return new AssistantAnswer(message, Array.Empty<Guid>(), context, message, UsedFallback: true);
        }

        var prompt = BuildPrompt(request, context);
        var response = await _chatModel.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        var parsed = TryParseResponse(response.RawResponse ?? response.Content ?? string.Empty, context, out var result)
            ? result
            : BuildFallback(context, response.RawResponse ?? response.Content ?? string.Empty);

        return parsed with { RawResponse = response.RawResponse ?? response.Content ?? string.Empty };
    }

    private string BuildPrompt(AssistantAnswerRequest request, MemoryContextResult context)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Tu es l'assistant AION. Langue: {request.Locale}.");
        builder.AppendLine("Tu DOIS répondre uniquement avec les éléments ci-dessous. Cite les sources via leurs RecordId dans [brackets].");
        builder.AppendLine("Si une information manque, dis-le explicitement et n'invente rien.");
        builder.AppendLine("Réponds uniquement en JSON compact: {\"message\":\"...\",\"citations\":[\"guid\"],\"fallback\":false}.");
        builder.AppendLine("Contexte mémoire (records, history, insights):");

        foreach (var item in context.All)
        {
            builder.AppendLine($"- id:{item.RecordId} type:{item.SourceType} titre:{item.Title} extrait:{item.Snippet} score:{item.Score:F2}");
        }

        builder.AppendLine($"Question utilisateur: {request.Question}");
        builder.AppendLine("Fin du contexte. Ne sors pas de ces informations.");
        return builder.ToString();
    }

    private bool TryParseResponse(string raw, MemoryContextResult context, out AssistantAnswer answer)
    {
        answer = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            var cleaned = JsonHelper.ExtractJson(raw);
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var message = root.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String
                ? messageProp.GetString() ?? string.Empty
                : cleaned.Trim();

            var fallback = root.TryGetProperty("fallback", out var fallbackProp) && fallbackProp.ValueKind == JsonValueKind.True;

            var citations = root.TryGetProperty("citations", out var citationsProp) && citationsProp.ValueKind == JsonValueKind.Array
                ? ParseCitations(citationsProp)
                : Array.Empty<Guid>();

            var filteredCitations = citations.Where(id => context.All.Any(c => c.RecordId == id)).ToArray();

            answer = new AssistantAnswer(message, filteredCitations, context, cleaned, fallback);
            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "ChatAnswerer: unable to parse model response");
            return false;
        }
    }

    private AssistantAnswer BuildFallback(MemoryContextResult context, string raw)
    {
        var message = "Réponse restreinte: contexte insuffisant ou réponse du modèle invalide.";
        return new AssistantAnswer(message, context.All.Select(c => c.RecordId).ToArray(), context, raw, UsedFallback: true);
    }

    private static IReadOnlyCollection<Guid> ParseCitations(JsonElement citationsElement)
    {
        var list = new List<Guid>();
        foreach (var element in citationsElement.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var id))
            {
                list.Add(id);
            }
        }

        return list;
    }
}
