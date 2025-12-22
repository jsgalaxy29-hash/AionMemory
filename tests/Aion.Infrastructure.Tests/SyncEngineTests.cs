using Aion.Domain;
using Aion.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Infrastructure.Tests;

public sealed class SyncEngineTests : IDisposable
{
    private readonly string _localRoot = Path.Combine(Path.GetTempPath(), $"aion-sync-local-{Guid.NewGuid():N}");
    private readonly string _remoteRoot = Path.Combine(Path.GetTempPath(), $"aion-sync-remote-{Guid.NewGuid():N}");

    public SyncEngineTests()
    {
        Directory.CreateDirectory(_localRoot);
        Directory.CreateDirectory(_remoteRoot);
    }

    [Fact]
    public async Task Plan_uploads_missing_remote_items()
    {
        var localFile = Path.Combine(_localRoot, "memory.json");
        await File.WriteAllTextAsync(localFile, "{ \"content\": \"local\" }");

        var engine = new SyncEngine(new NullLogger<SyncEngine>());
        var local = new FileSystemSyncBackend(_localRoot);
        var remote = new FileSystemSyncBackend(_remoteRoot);

        var plan = await engine.PlanAsync(local, remote);
        var state = Assert.Single(plan);
        Assert.Equal(SyncAction.Upload, state.Action);
        Assert.Equal("memory.json", state.Path);
        Assert.False(state.IsConflict);
    }

    [Fact]
    public async Task Plan_detects_conflicts_and_prefers_latest_write()
    {
        var localFile = Path.Combine(_localRoot, "memory.json");
        var remoteFile = Path.Combine(_remoteRoot, "memory.json");

        await File.WriteAllTextAsync(localFile, "{ \"content\": \"local\" }");
        await File.WriteAllTextAsync(remoteFile, "{ \"content\": \"remote\" }");

        var newer = DateTimeOffset.UtcNow;
        File.SetLastWriteTimeUtc(localFile, newer.UtcDateTime);
        File.SetLastWriteTimeUtc(remoteFile, newer.UtcDateTime.AddMinutes(-5));

        var engine = new SyncEngine(new NullLogger<SyncEngine>());
        var local = new FileSystemSyncBackend(_localRoot);
        var remote = new FileSystemSyncBackend(_remoteRoot);

        var plan = await engine.PlanAsync(local, remote);
        var state = Assert.Single(plan);

        Assert.Equal(SyncAction.Upload, state.Action);
        var conflict = Assert.NotNull(state.Conflict);
        Assert.Equal(SyncConflictRule.LastWriteWins, conflict.Rule);
        Assert.Equal(SyncPrioritySide.Local, conflict.PreferredSide);
    }

    public void Dispose()
    {
        TryDelete(_localRoot);
        TryDelete(_remoteRoot);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup for CI; ignore locked files.
        }
    }
}
