using System.Security.Cryptography;
using System.Text;
using Aion.Domain;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services;

public sealed class OfflineActionReplayService : IOfflineActionReplayService
{
    private readonly IOfflineActionQueue _queue;
    private readonly SyncOutboxService _outbox;
    private readonly ILogger<OfflineActionReplayService> _logger;

    public OfflineActionReplayService(
        IOfflineActionQueue queue,
        SyncOutboxService outbox,
        ILogger<OfflineActionReplayService> logger)
    {
        _queue = queue;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task ReplayPendingAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _queue.GetPendingAsync(cancellationToken).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return;
        }

        foreach (var action in pending)
        {
            try
            {
                var syncAction = action.Action == OfflineActionType.Delete ? SyncAction.Delete : SyncAction.Upload;
                var payloadBytes = Encoding.UTF8.GetBytes(action.PayloadJson);
                var hash = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();
                var syncItem = new SyncItem(
                    BuildPath(action),
                    action.EnqueuedAt,
                    action.EnqueuedAt.UtcDateTime.Ticks,
                    payloadBytes.LongLength,
                    hash);

                await _outbox.EnqueueAsync(syncItem, syncAction, cancellationToken).ConfigureAwait(false);
                await _queue.MarkAppliedAsync(action.Id, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Offline action {ActionId} replayed to outbox.", action.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to replay offline action {ActionId}.", action.Id);
                await _queue.MarkFailedAsync(action.Id, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string BuildPath(OfflineRecordAction action)
        => $"offline-actions/{action.TableId:N}/{action.RecordId:N}/{action.Id:N}.json";
}
