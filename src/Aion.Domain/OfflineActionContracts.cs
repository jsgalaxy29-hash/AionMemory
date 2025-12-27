namespace Aion.Domain;

public enum OfflineActionType
{
    Create,
    Update,
    Delete
}

public enum OfflineActionStatus
{
    Pending,
    Applied,
    Failed
}

public sealed record OfflineRecordAction(
    Guid Id,
    Guid TableId,
    Guid RecordId,
    OfflineActionType Action,
    string PayloadJson,
    DateTimeOffset EnqueuedAt,
    OfflineActionStatus Status,
    DateTimeOffset? AppliedAt,
    string? FailureReason);

public interface IOfflineActionQueue
{
    Task<OfflineRecordAction> EnqueueAsync(OfflineRecordAction action, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<OfflineRecordAction>> GetPendingAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<OfflineRecordAction>> GetPendingAsync(Guid tableId, CancellationToken cancellationToken = default);
    Task<bool> HasPendingAsync(Guid tableId, Guid recordId, CancellationToken cancellationToken = default);
    Task MarkAppliedAsync(Guid actionId, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(Guid actionId, string reason, CancellationToken cancellationToken = default);
}

public interface IOfflineActionReplayService
{
    Task ReplayPendingAsync(CancellationToken cancellationToken = default);
}
