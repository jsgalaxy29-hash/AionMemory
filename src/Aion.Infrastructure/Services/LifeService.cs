using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aion.AI;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Aion.Infrastructure.Observability;
using Aion.Infrastructure.Services.Automation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class LifeService : IAionLifeLogService, ILifeService
{
    private readonly AionDbContext _db;

    public LifeService(AionDbContext db)
    {
        _db = db;
    }

    public async Task<S_HistoryEvent> AddHistoryAsync(S_HistoryEvent evt, CancellationToken cancellationToken = default)
    {
        await _db.HistoryEvents.AddAsync(evt, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return evt;
    }

    public async Task<TimelinePage> GetTimelinePageAsync(TimelineQuery query, CancellationToken cancellationToken = default)
    {
        var take = query.NormalizedTake;
        var skip = query.NormalizedSkip;

        var eventsQuery = _db.HistoryEvents.Include(h => h.Links).AsQueryable();
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
        var query = _db.HistoryEvents.Include(h => h.Links).AsQueryable();
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
            .ToList();
    }
}

