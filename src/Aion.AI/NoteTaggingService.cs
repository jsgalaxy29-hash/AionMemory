using Aion.Domain;

namespace Aion.AI;

public sealed class RuleBasedNoteTaggingService : INoteTaggingService
{
    private static readonly (string Tag, string[] Keywords)[] Rules =
    [
        ("tache", ["todo", "à faire", "a faire", "rappel", "checklist"]),
        ("rdv", ["rendez-vous", "rendez vous", "rdv", "meeting", "réunion", "reunion", "call"]),
        ("idee", ["idée", "idee", "inspiration", "concept"]),
        ("finance", ["facture", "paiement", "budget", "achat", "prix", "devis"]),
        ("sante", ["santé", "sante", "médecin", "medecin", "sport", "bien-être", "bien etre"]),
        ("voyage", ["voyage", "vol", "train", "hôtel", "hotel", "itinéraire", "itineraire"]),
        ("projet", ["projet", "roadmap", "objectif", "milestone", "livrable"]),
        ("bug", ["bug", "erreur", "incident", "crash", "anomalie"])
    ];

    public Task<IReadOnlyCollection<string>> SuggestTagsAsync(string title, string content, CancellationToken cancellationToken = default)
    {
        var corpus = $"{title} {content}".Trim();
        if (string.IsNullOrWhiteSpace(corpus))
        {
            return Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
        }

        var lower = corpus.ToLowerInvariant();
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in Rules)
        {
            if (rule.Keywords.Any(keyword => lower.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                tags.Add(rule.Tag);
            }
        }

        return Task.FromResult<IReadOnlyCollection<string>>(tags.OrderBy(t => t).ToList());
    }
}
