namespace Aion.AI;

internal static class IntentHeuristics
{
    public static IntentClass Detect(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return IntentCatalog.Normalize(IntentCatalog.Chat);
        }

        var normalized = input.Trim().ToLowerInvariant();

        if (ContainsAny(normalized, "rendez-vous", "agenda", "calendrier", "meeting", "rdv", "réunion"))
        {
            return IntentCatalog.Normalize(IntentCatalog.Agenda);
        }

        if (ContainsAny(normalized, "note", "notes", "mémo", "memo"))
        {
            return IntentCatalog.Normalize(IntentCatalog.Note);
        }

        if (ContainsAny(normalized, "rapport", "report", "analyse", "tableau"))
        {
            return IntentCatalog.Normalize(IntentCatalog.Report);
        }

        if (ContainsAny(normalized, "module", "entité", "schema", "schéma", "structure"))
        {
            return IntentCatalog.Normalize(IntentCatalog.Module);
        }

        if (ContainsAny(normalized, "ajoute", "crée", "insère", "modifie", "met à jour", "supprime", "liste", "trouve", "recherche"))
        {
            return IntentCatalog.Normalize(IntentCatalog.Data);
        }

        return IntentCatalog.Normalize(IntentCatalog.Chat);
    }

    private static bool ContainsAny(string input, params string[] tokens)
        => tokens.Any(token => input.Contains(token, StringComparison.OrdinalIgnoreCase));
}
