using System;
using System.Collections.Generic;

namespace Aion.Domain;

public enum SyncAction
{
    None,
    Upload,
    Download,
    Delete,
    Conflict
}

public enum SyncPrioritySide
{
    Local,
    Remote
}

public enum SyncConflictRule
{
    LastWriteWins
}

public sealed record SyncOperation(Guid OperationId, SyncAction Action, SyncItem Item);

public sealed record SyncItem
{
    public SyncItem(string path, DateTimeOffset modifiedAt, long version, long? length = null, string? hash = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The sync item path cannot be empty.", nameof(path));
        }

        Path = path.Replace("\\", "/", StringComparison.Ordinal);
        ModifiedAt = modifiedAt;
        Version = version;
        Length = length;
        Hash = hash;
    }

    public string Path { get; init; }

    public DateTimeOffset ModifiedAt { get; init; }

    public long Version { get; init; }

    public long? Length { get; init; }

    public string? Hash { get; init; }
}

public sealed record SyncConflict(
    string Path,
    SyncItem Local,
    SyncItem Remote,
    SyncPrioritySide PreferredSide,
    SyncConflictRule Rule,
    string? Reason = null);

public sealed record SyncState(
    string Path,
    SyncItem? Local,
    SyncItem? Remote,
    SyncAction Action,
    SyncConflict? Conflict = null)
{
    public bool IsConflict => Conflict is not null;

    public IReadOnlyDictionary<SyncPrioritySide, SyncItem> Items
    {
        get
        {
            var items = new Dictionary<SyncPrioritySide, SyncItem>();

            if (Local is not null)
            {
                items[SyncPrioritySide.Local] = Local;
            }

            if (Remote is not null)
            {
                items[SyncPrioritySide.Remote] = Remote;
            }

            return items;
        }
    }
}
