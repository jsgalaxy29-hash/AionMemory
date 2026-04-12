using Aion.Domain;

namespace Aion.Infrastructure;

public enum SyncOutboxStatus
{
    Pending,
    Applied,
    Conflict,
    Failed
}

public sealed class SyncOutboxItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Path { get; set; } = string.Empty;

    public SyncAction Action { get; set; }

    public DateTimeOffset EnqueuedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastAttemptAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTimeOffset? AppliedAt { get; set; }

    public SyncOutboxStatus Status { get; set; } = SyncOutboxStatus.Pending;

    public string? FailureReason { get; set; }

    public DateTimeOffset ModifiedAt { get; set; }

    public long Version { get; set; }

    public long? Length { get; set; }

    public string? Hash { get; set; }

    public SyncOperation ToOperation()
        => new(Id, Action, new SyncItem(Path, ModifiedAt, Version, Length, Hash));
}
