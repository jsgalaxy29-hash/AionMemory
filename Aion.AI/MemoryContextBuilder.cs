using Aion.Domain;
using Microsoft.Extensions.Logging;

namespace Aion.AI;

public sealed class MemoryContextBuilder : IMemoryContextBuilder
{
    private readonly ISearchService _searchService;
    private readonly ILifeService _lifeService;
    private readonly IMemoryIntelligenceService _memoryInsights;
    private readonly ILogger<MemoryContextBuilder> _logger;

    public MemoryContextBuilder(
        ISearchService searchService,
        ILifeService lifeService,
        IMemoryIntelligenceService memoryInsights,
        ILogger<MemoryContextBuilder> logger)
    {
        _searchService = searchService;
        _lifeService = lifeService;
        _memoryInsights = memoryInsights;
        _logger = logger;
    }

    public async Task<MemoryContextResult> BuildAsync(MemoryContextRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Empty();
        }

        var hits = await FetchRecordsAsync(request, cancellationToken).ConfigureAwait(false);
        var history = await FetchHistoryAsync(request, cancellationToken).ConfigureAwait(false);
        var insights = await FetchInsightsAsync(request, cancellationToken).ConfigureAwait(false);

        return new MemoryContextResult(hits, history, insights);
    }

    private async Task<IReadOnlyCollection<MemoryContextItem>> FetchRecordsAsync(MemoryContextRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var searchHits = await _searchService
                .SearchAsync(request.Query, cancellationToken)
                .ConfigureAwait(false);

            return searchHits
                .OrderByDescending(h => h.Score)
                .Take(Math.Max(1, request.RecordLimit))
                .Select(hit => new MemoryContextItem(
                    hit.TargetId,
                    hit.TargetType,
                    hit.Title,
                    hit.Snippet,
                    Timestamp: null,
                    TableId: null,
                    Scope: null,
                    Score: hit.Score))
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ContextBuilder: global search failed, returning empty record context");
            return Array.Empty<MemoryContextItem>();
        }
    }

    private async Task<IReadOnlyCollection<MemoryContextItem>> FetchHistoryAsync(MemoryContextRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var events = await _lifeService
                .GetTimelineAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var filtered = events
                .Where(evt => ContainsQuery(evt.Title, evt.Description, request.Query))
                .OrderByDescending(evt => evt.OccurredAt)
                .Take(Math.Max(0, request.HistoryLimit))
                .Select(evt => new MemoryContextItem(
                    evt.Id,
                    "history",
                    evt.Title,
                    BuildSnippet(evt.Description ?? evt.Title),
                    evt.OccurredAt,
                    TableId: null,
                    Scope: null,
                    Score: 0.5))
                .ToArray();

            return filtered;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ContextBuilder: history lookup failed");
            return Array.Empty<MemoryContextItem>();
        }
    }

    private async Task<IReadOnlyCollection<MemoryContextItem>> FetchInsightsAsync(MemoryContextRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var insights = await _memoryInsights
                .GetRecentAsync(Math.Max(1, request.InsightLimit * 3), cancellationToken)
                .ConfigureAwait(false);

            return insights
                .Where(insight => ContainsQuery(insight.Summary, insight.Scope, request.Query))
                .OrderByDescending(insight => insight.GeneratedAt)
                .Take(Math.Max(0, request.InsightLimit))
                .Select(insight => new MemoryContextItem(
                    insight.Id,
                    "insight",
                    insight.Scope ?? "Insight",
                    BuildSnippet(insight.Summary),
                    insight.GeneratedAt,
                    TableId: null,
                    Scope: insight.Scope,
                    Score: 0.75))
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ContextBuilder: insight lookup failed");
            return Array.Empty<MemoryContextItem>();
        }
    }

    private static bool ContainsQuery(string? left, string? right, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        return (!string.IsNullOrWhiteSpace(left) && left.Contains(query, comparison))
            || (!string.IsNullOrWhiteSpace(right) && right.Contains(query, comparison));
    }

    private static string BuildSnippet(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        const int limit = 220;
        var normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= limit ? normalized : normalized[..limit] + "…";
    }

    private static MemoryContextResult Empty()
        => new(Array.Empty<MemoryContextItem>(), Array.Empty<MemoryContextItem>(), Array.Empty<MemoryContextItem>());
}
