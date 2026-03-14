using Aion.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services;

public sealed class OfflineActionQueueService : IOfflineActionQueue
{
    private readonly AionDbContext _db;
    private readonly ILogger<OfflineActionQueueService> _logger;

    public OfflineActionQueueService(AionDbContext db, ILogger<OfflineActionQueueService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<OfflineRecordAction> EnqueueAsync(OfflineRecordAction action, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(action.PayloadJson))
        {
            throw new ArgumentException("Le payload JSON ne peut pas être vide.", nameof(action));
        }

        var entry = new OfflineRecordActionEntry
        {
            Id = action.Id == Guid.Empty ? Guid.NewGuid() : action.Id,
            TableId = action.TableId,
            RecordId = action.RecordId,
            Action = action.Action,
            PayloadJson = action.PayloadJson,
            EnqueuedAt = action.EnqueuedAt == default ? DateTimeOffset.UtcNow : action.EnqueuedAt,
            Status = OfflineActionStatus.Pending
        };

        _db.OfflineRecordActions.Add(entry);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Offline action {ActionId} queued for record {RecordId}.", entry.Id, entry.RecordId);
        return entry.ToModel();
    }

    public async Task<IReadOnlyCollection<OfflineRecordAction>> GetPendingAsync(CancellationToken cancellationToken = default)
        => await QueryPendingAsync(null, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyCollection<OfflineRecordAction>> GetPendingAsync(Guid tableId, CancellationToken cancellationToken = default)
        => await QueryPendingAsync(tableId, cancellationToken).ConfigureAwait(false);

    public async Task<bool> HasPendingAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default)
        => await _db.OfflineRecordActions
            .AsNoTracking()
            .AnyAsync(entry => entry.TableId == tableId
                               && entry.RecordId == recordId
                               && entry.Status == OfflineActionStatus.Pending,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task MarkAppliedAsync(Guid actionId, CancellationToken cancellationToken = default)
        => await UpdateStatusAsync(actionId, OfflineActionStatus.Applied, null, cancellationToken).ConfigureAwait(false);

    public async Task MarkFailedAsync(Guid actionId, string reason, CancellationToken cancellationToken = default)
        => await UpdateStatusAsync(actionId, OfflineActionStatus.Failed, reason, cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyCollection<OfflineRecordAction>> QueryPendingAsync(Guid? tableId, CancellationToken cancellationToken)
    {
        var query = _db.OfflineRecordActions
            .AsNoTracking()
            .Where(entry => entry.Status == OfflineActionStatus.Pending);

        if (tableId.HasValue)
        {
            query = query.Where(entry => entry.TableId == tableId.Value);
        }

        return await query
            .OrderBy(entry => entry.EnqueuedAt)
            .Select(entry => entry.ToModel())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task UpdateStatusAsync(Guid actionId, OfflineActionStatus status, string? failureReason, CancellationToken cancellationToken)
    {
        var entry = await _db.OfflineRecordActions
            .FirstOrDefaultAsync(item => item.Id == actionId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Offline action {actionId} not found.");

        entry.Status = status;
        entry.FailureReason = failureReason;
        entry.AppliedAt = status == OfflineActionStatus.Applied ? DateTimeOffset.UtcNow : entry.AppliedAt;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
