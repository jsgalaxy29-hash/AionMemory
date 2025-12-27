using Aion.Domain;

namespace Aion.Infrastructure;

public sealed class OfflineRecordActionEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TableId { get; set; }

    public Guid RecordId { get; set; }

    public OfflineActionType Action { get; set; }

    public string PayloadJson { get; set; } = "{}";

    public DateTimeOffset EnqueuedAt { get; set; } = DateTimeOffset.UtcNow;

    public OfflineActionStatus Status { get; set; } = OfflineActionStatus.Pending;

    public DateTimeOffset? AppliedAt { get; set; }

    public string? FailureReason { get; set; }

    public OfflineRecordAction ToModel()
        => new(Id, TableId, RecordId, Action, PayloadJson, EnqueuedAt, Status, AppliedAt, FailureReason);
}
