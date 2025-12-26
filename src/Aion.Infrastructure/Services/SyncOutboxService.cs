using Aion.Domain;
using Microsoft.EntityFrameworkCore;

namespace Aion.Infrastructure.Services;

public sealed class SyncOutboxService
{
    private readonly AionDbContext _db;
    private readonly ILifeService _timeline;

    public SyncOutboxService(AionDbContext db, ILifeService timeline)
    {
        _db = db;
        _timeline = timeline;
    }

    public async Task<SyncOutboxItem> EnqueueAsync(SyncItem item, SyncAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var entry = new SyncOutboxItem
        {
            Path = item.Path,
            Action = action,
            ModifiedAt = item.ModifiedAt,
            Version = item.Version,
            Length = item.Length,
            Hash = item.Hash
        };

        _db.SyncOutbox.Add(entry);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _timeline.AddHistoryAsync(new S_HistoryEvent
        {
            Title = "Synchronisation en attente",
            Description = entry.Path,
            OccurredAt = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
        return entry;
    }

    public async Task<IReadOnlyCollection<SyncOutboxItem>> GetPendingAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        if (take <= 0)
        {
            return Array.Empty<SyncOutboxItem>();
        }

        return await _db.SyncOutbox
            .Where(item => item.Status == SyncOutboxStatus.Pending)
            .OrderBy(item => item.EnqueuedAt)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task MarkAppliedAsync(Guid id, CancellationToken cancellationToken = default)
        => await UpdateStatusAsync(id, SyncOutboxStatus.Applied, null, cancellationToken).ConfigureAwait(false);

    public async Task MarkConflictAsync(Guid id, string reason, CancellationToken cancellationToken = default)
        => await UpdateStatusAsync(id, SyncOutboxStatus.Conflict, reason, cancellationToken).ConfigureAwait(false);

    public async Task MarkFailedAsync(Guid id, string reason, CancellationToken cancellationToken = default)
        => await UpdateStatusAsync(id, SyncOutboxStatus.Failed, reason, cancellationToken).ConfigureAwait(false);

    private async Task UpdateStatusAsync(Guid id, SyncOutboxStatus status, string? reason, CancellationToken cancellationToken)
    {
        var entry = await _db.SyncOutbox.FirstOrDefaultAsync(item => item.Id == id, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Outbox item {id} not found.");

        entry.Status = status;
        entry.FailureReason = string.IsNullOrWhiteSpace(reason) ? null : reason;
        entry.LastAttemptAt = DateTimeOffset.UtcNow;
        entry.AttemptCount += 1;

        if (status == SyncOutboxStatus.Applied)
        {
            entry.AppliedAt = entry.LastAttemptAt;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var title = status switch
        {
            SyncOutboxStatus.Applied => "Synchronisation appliquée",
            SyncOutboxStatus.Conflict => "Synchronisation en conflit",
            SyncOutboxStatus.Failed => "Synchronisation échouée",
            _ => "Synchronisation mise à jour"
        };

        var description = string.IsNullOrWhiteSpace(reason)
            ? entry.Path
            : $"{entry.Path} — {reason}";

        await _timeline.AddHistoryAsync(new S_HistoryEvent
        {
            Title = title,
            Description = description,
            OccurredAt = entry.LastAttemptAt ?? DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
    }
}
