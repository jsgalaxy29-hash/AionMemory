namespace Aion.Domain;

public sealed record RecordSearchHit(Guid RecordId, double Score, string Snippet);

public sealed record class SearchOptions
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int DefaultSnippetTokens = 12;

    public int Skip { get; init; }

    public int Take { get; init; } = DefaultPageSize;

    public string? Language { get; init; }

    public int SnippetTokens { get; init; } = DefaultSnippetTokens;

    public string HighlightBefore { get; init; } = "<mark>";

    public string HighlightAfter { get; init; } = "</mark>";
}
