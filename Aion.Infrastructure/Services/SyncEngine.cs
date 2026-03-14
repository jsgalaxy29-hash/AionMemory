using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Aion.Domain;
using Aion.Infrastructure.Observability;
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
            plan.Add(BuildState(path, localMap.GetValueOrDefault(path), remoteMap.GetValueOrDefault(path), "plan"));
        }

        return plan;
    }

    public async Task<IReadOnlyCollection<SyncState>> ApplyAsync(
        ISyncBackend local,
        ISyncBackend remote,
        IReadOnlyCollection<SyncOperation> operations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(operations);

        var results = new List<SyncState>();

        foreach (var operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = operation.Item.Path;
            var remoteItem = await remote.GetAsync(path, cancellationToken).ConfigureAwait(false);

            if (await remote.HasAppliedAsync(operation.OperationId, cancellationToken).ConfigureAwait(false))
            {
                InfrastructureMetrics.RecordSyncReplay();
                results.Add(new SyncState(path, operation.Item, remoteItem, SyncAction.None));
                continue;
            }

            if (operation.Action == SyncAction.Delete)
            {
                var deleteState = ResolveDeleteState(operation.Item, remoteItem);
                if (deleteState.Action == SyncAction.Conflict)
                {
                    results.Add(deleteState);
                    continue;
                }

                await remote.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
                await remote.MarkAppliedAsync(operation.OperationId, cancellationToken).ConfigureAwait(false);
                results.Add(deleteState);
                continue;
            }

            var state = BuildState(path, operation.Item, remoteItem, "apply");
            if (state.Conflict is not null && state.Conflict.PreferredSide == SyncPrioritySide.Remote)
            {
                results.Add(new SyncState(path, operation.Item, remoteItem, SyncAction.Conflict, state.Conflict));
                continue;
            }

            if (state.Action != SyncAction.None)
            {
                await using var stream = await local.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
                await remote.WriteAsync(operation.Item, stream, cancellationToken).ConfigureAwait(false);
            }

            await remote.MarkAppliedAsync(operation.OperationId, cancellationToken).ConfigureAwait(false);
            results.Add(state);
        }

        return results;
    }

    private SyncState BuildState(string path, SyncItem? local, SyncItem? remote, string stage)
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
        InfrastructureMetrics.RecordSyncConflict(stage);
        return new SyncState(path, local, remote, action, conflict);
    }

    private SyncState ResolveDeleteState(SyncItem local, SyncItem? remote)
    {
        if (remote is null)
        {
            return new SyncState(local.Path, local, null, SyncAction.Delete);
        }

        if (AreEquivalent(local, remote))
        {
            return new SyncState(local.Path, local, remote, SyncAction.Delete);
        }

        var preferredSide = ResolvePriority(local, remote, out var reason);
        if (preferredSide == SyncPrioritySide.Remote)
        {
            var conflict = new SyncConflict(local.Path, local, remote, preferredSide, SyncConflictRule.LastWriteWins, reason);
            _logger.LogInformation("Conflict detected on {Path} during delete. Preferred side: {Side}. Rule: {Rule}.", local.Path, preferredSide, SyncConflictRule.LastWriteWins);
            InfrastructureMetrics.RecordSyncConflict("delete");
            return new SyncState(local.Path, local, remote, SyncAction.Conflict, conflict);
        }

        return new SyncState(local.Path, local, remote, SyncAction.Delete);
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
    private readonly string _appliedOperationsPath;
    private readonly SemaphoreSlim _appliedGate = new(1, 1);

    public FileSystemSyncBackend(string rootPath, bool includeHash = false)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("A root path is required for filesystem sync.", nameof(rootPath));
        }

        _rootPath = Path.GetFullPath(rootPath);
        _includeHash = includeHash;
        _appliedOperationsPath = Path.Combine(_rootPath, ".sync-applied.json");
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

    public Task<SyncItem?> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<SyncItem?>(null);
        }

        var info = new FileInfo(fullPath);
        return Task.FromResult<SyncItem?>(ToSyncItem(info));
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Sync item '{path}' not found.", fullPath);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public async Task WriteAsync(SyncItem item, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = GetFullPath(item.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        File.SetLastWriteTimeUtc(fullPath, item.ModifiedAt.UtcDateTime);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public async Task<bool> HasAppliedAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        await _appliedGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var applied = await ReadAppliedAsync(cancellationToken).ConfigureAwait(false);
            return applied.Contains(operationId);
        }
        finally
        {
            _appliedGate.Release();
        }
    }

    public async Task MarkAppliedAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        await _appliedGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var applied = await ReadAppliedAsync(cancellationToken).ConfigureAwait(false);
            if (applied.Add(operationId))
            {
                await WriteAppliedAsync(applied, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _appliedGate.Release();
        }
    }

    private SyncItem ToSyncItem(FileInfo info)
    {
        var modifiedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
        var hash = _includeHash ? ComputeHash(info) : null;
        var relative = Path.GetRelativePath(_rootPath, info.FullName).Replace(Path.DirectorySeparatorChar, '/');
        return new SyncItem(relative, modifiedAt, modifiedAt.UtcDateTime.Ticks, info.Length, hash);
    }

    private string GetFullPath(string path)
    {
        var normalized = path.Replace("\\", "/", StringComparison.Ordinal);
        var fullPath = Path.GetFullPath(Path.Combine(_rootPath, normalized));
        if (!fullPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Sync path '{path}' escapes root '{_rootPath}'.");
        }

        return fullPath;
    }

    private async Task<HashSet<Guid>> ReadAppliedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_appliedOperationsPath))
        {
            return new HashSet<Guid>();
        }

        await using var stream = new FileStream(_appliedOperationsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var items = await JsonSerializer.DeserializeAsync<HashSet<Guid>>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return items ?? new HashSet<Guid>();
    }

    private async Task WriteAppliedAsync(HashSet<Guid> applied, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(_appliedOperationsPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, applied, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string ComputeHash(FileInfo info)
    {
        using var stream = info.OpenRead();
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }
}
