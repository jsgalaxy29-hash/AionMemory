using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Aion.Domain;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services;

public sealed class SyncEngine : ISyncEngine
{
    private readonly ILogger<SyncEngine> _logger;

    public SyncEngine(ILogger<SyncEngine> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<SyncState>> PlanAsync(ISyncBackend local, ISyncBackend remote, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        var localItems = await local.ListAsync(cancellationToken).ConfigureAwait(false);
        var remoteItems = await remote.ListAsync(cancellationToken).ConfigureAwait(false);

        var localMap = localItems.ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);
        var remoteMap = remoteItems.ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);
        var allPaths = new HashSet<string>(localMap.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var path in remoteMap.Keys)
        {
            allPaths.Add(path);
        }

        var plan = new List<SyncState>();
        foreach (var path in allPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            plan.Add(BuildState(path, localMap.GetValueOrDefault(path), remoteMap.GetValueOrDefault(path)));
        }

        return plan;
    }

    private SyncState BuildState(string path, SyncItem? local, SyncItem? remote)
    {
        if (local is null && remote is null)
        {
            throw new InvalidOperationException($"Sync item '{path}' must exist on at least one side.");
        }

        if (local is null)
        {
            return new SyncState(path, null, remote, SyncAction.Download);
        }

        if (remote is null)
        {
            return new SyncState(path, local, null, SyncAction.Upload);
        }

        if (AreEquivalent(local, remote))
        {
            return new SyncState(path, local, remote, SyncAction.None);
        }

        var preferredSide = ResolvePriority(local, remote, out var reason);
        var action = preferredSide == SyncPrioritySide.Local ? SyncAction.Upload : SyncAction.Download;
        var conflict = new SyncConflict(path, local, remote, preferredSide, SyncConflictRule.LastWriteWins, reason);
        _logger.LogInformation("Conflict detected on {Path}. Preferred side: {Side}. Rule: {Rule}.", path, preferredSide, SyncConflictRule.LastWriteWins);
        return new SyncState(path, local, remote, action, conflict);
    }

    private static bool AreEquivalent(SyncItem left, SyncItem right)
    {
        if (left.Version == right.Version && left.ModifiedAt == right.ModifiedAt)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(left.Hash)
               && string.Equals(left.Hash, right.Hash, StringComparison.OrdinalIgnoreCase)
               && left.Length == right.Length;
    }

    private static SyncPrioritySide ResolvePriority(SyncItem local, SyncItem remote, out string reason)
    {
        if (local.ModifiedAt > remote.ModifiedAt)
        {
            reason = "Local item updated later (LastWriteWins).";
            return SyncPrioritySide.Local;
        }

        if (remote.ModifiedAt > local.ModifiedAt)
        {
            reason = "Remote item updated later (LastWriteWins).";
            return SyncPrioritySide.Remote;
        }

        if (local.Version > remote.Version)
        {
            reason = "Local version is higher.";
            return SyncPrioritySide.Local;
        }

        if (remote.Version > local.Version)
        {
            reason = "Remote version is higher.";
            return SyncPrioritySide.Remote;
        }

        reason = "Versions and timestamps match; defaulting to local.";
        return SyncPrioritySide.Local;
    }
}

public sealed class FileSystemSyncBackend : ISyncBackend
{
    private readonly string _rootPath;
    private readonly bool _includeHash;

    public FileSystemSyncBackend(string rootPath, bool includeHash = false)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A root path is required for filesystem sync.", nameof(rootPath));
        }

        _rootPath = Path.GetFullPath(rootPath);
        _includeHash = includeHash;
        Directory.CreateDirectory(_rootPath);
    }

    public Task<IReadOnlyCollection<SyncItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<SyncItem>();
        foreach (var file in Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            items.Add(ToSyncItem(info));
        }

        return Task.FromResult<IReadOnlyCollection<SyncItem>>(items);
    }

    private SyncItem ToSyncItem(FileInfo info)
    {
        var modifiedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
        var hash = _includeHash ? ComputeHash(info) : null;
        var relative = Path.GetRelativePath(_rootPath, info.FullName).Replace(Path.DirectorySeparatorChar, '/');
        return new SyncItem(relative, modifiedAt, modifiedAt.UtcDateTime.Ticks, info.Length, hash);
    }

    private static string ComputeHash(FileInfo info)
    {
        using var stream = info.OpenRead();
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }
}
