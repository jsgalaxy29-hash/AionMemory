using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Microsoft.EntityFrameworkCore;

namespace Aion.Infrastructure.Services;

public sealed class AiCallLogService : IAiCallLogService
{
    private readonly AionDbContext _db;
    private readonly IWorkspaceContext _workspaceContext;

    public AiCallLogService(AionDbContext db, IWorkspaceContext workspaceContext)
    {
        _db = db;
        _workspaceContext = workspaceContext;
    }

    public async Task LogAsync(AiCallLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var context = OperationContext.Current;
        var log = new AiCallLog
        {
            WorkspaceId = _workspaceContext.WorkspaceId,
            Provider = entry.Provider,
            Model = entry.Model,
            Operation = entry.Operation,
            Tokens = entry.Tokens,
            Cost = entry.Cost,
            DurationMs = entry.DurationMs,
            Status = entry.Status,
            CorrelationId = context.CorrelationId,
            OccurredAt = DateTimeOffset.UtcNow
        };

        await _db.AiCallLogs.AddAsync(log, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AiCallDiagnostics> GetDiagnosticsAsync(AiCallDiagnosticsQuery query, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var from = query.From ?? now.AddDays(-7);
        var to = query.To ?? now;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var baseQuery = _db.AiCallLogs.AsNoTracking()
            .Where(log => log.OccurredAt >= from && log.OccurredAt <= to);

        var totalCalls = await baseQuery.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var totalTokens = await baseQuery.SumAsync(log => (long?)log.Tokens, cancellationToken).ConfigureAwait(false) ?? 0;
        var totalCost = await baseQuery.SumAsync(log => (double?)log.Cost, cancellationToken).ConfigureAwait(false) ?? 0d;
        var averageDuration = await baseQuery.AverageAsync(log => (double?)log.DurationMs, cancellationToken).ConfigureAwait(false) ?? 0d;
        var successCalls = await baseQuery.LongCountAsync(log => log.Status == AiCallStatus.Success, cancellationToken).ConfigureAwait(false);
        var errorCalls = await baseQuery.LongCountAsync(log => log.Status == AiCallStatus.Error, cancellationToken).ConfigureAwait(false);

        var providers = await baseQuery
            .GroupBy(log => new { log.Provider, log.Model })
            .Select(group => new AiCallProviderStats(
                group.Key.Provider,
                group.Key.Model,
                group.LongCount(),
                group.Sum(log => (long?)log.Tokens) ?? 0,
                group.Sum(log => (double?)log.Cost) ?? 0d,
                group.Average(log => (double?)log.DurationMs) ?? 0d))
            .OrderByDescending(stat => stat.Calls)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AiCallDiagnostics(
            from,
            to,
            new AiCallTotals(totalCalls, totalTokens, totalCost, averageDuration, successCalls, errorCalls),
            providers);
    }
}
