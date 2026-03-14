using Aion.Domain;
using Microsoft.EntityFrameworkCore;

namespace Aion.Infrastructure.Services;

public sealed class TimelineService : IAionLifeLogService, ILifeService
{
    private readonly AionDbContext _db;

    public TimelineService(AionDbContext db)
    {
        _db = db;
    }

    public async Task<S_HistoryEvent> AddHistoryAsync(S_HistoryEvent evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (string.IsNullOrWhiteSpace(evt.Title))
        {
            throw new InvalidOperationException("History event title is required.");
        }

        if (evt.OccurredAt == default)
        {
            evt.OccurredAt = DateTimeOffset.UtcNow;
        }

        await _db.HistoryEvents.AddAsync(evt, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return evt;
    }

    public async Task<TimelinePage> GetTimelinePageAsync(TimelineQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var take = query.NormalizedTake;
        var skip = query.NormalizedSkip;

        var eventsQuery = _db.HistoryEvents
            .Include(h => h.Links)
            .AsNoTracking()
            .AsQueryable();

        if (query.ModuleId.HasValue)
        {
            eventsQuery = eventsQuery.Where(h => h.ModuleId == query.ModuleId.Value);
        }

        if (query.From.HasValue)
        {
            eventsQuery = eventsQuery.Where(h => h.OccurredAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            eventsQuery = eventsQuery.Where(h => h.OccurredAt <= query.To.Value);
        }

        var results = await eventsQuery
            .OrderByDescending(h => h.OccurredAt)
            .ThenByDescending(h => h.Id)
            .Skip(skip)
            .Take(take + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = results.Count > take;
        if (hasMore)
        {
            results.RemoveAt(results.Count - 1);
        }

        var nextSkip = skip + results.Count;
        return new TimelinePage(results, hasMore, nextSkip);
    }

    public async Task<IEnumerable<S_HistoryEvent>> GetTimelineAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        var query = _db.HistoryEvents
            .Include(h => h.Links)
            .AsNoTracking()
            .AsQueryable();
        if (from.HasValue)
        {
            query = query.Where(h => h.OccurredAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(h => h.OccurredAt <= to.Value);
        }

        var results = await query
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return results
            .OrderByDescending(h => h.OccurredAt)
            .ThenByDescending(h => h.Id)
            .ToList();
    }
}
