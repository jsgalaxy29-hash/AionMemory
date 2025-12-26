using Aion.Domain;
using Aion.Infrastructure;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Infrastructure.Tests;

public sealed class SyncOutboxReplayTests : IDisposable
{
    private readonly string _localRoot = Path.Combine(Path.GetTempPath(), $"aion-outbox-local-{Guid.NewGuid():N}");
    private readonly string _remoteRoot = Path.Combine(Path.GetTempPath(), $"aion-outbox-remote-{Guid.NewGuid():N}");

    public SyncOutboxReplayTests()
    {
        Directory.CreateDirectory(_localRoot);
        Directory.CreateDirectory(_remoteRoot);
    }

    [Fact]
    public async Task Replay_uploads_offline_queue_and_is_idempotent()
    {
        var localFile = Path.Combine(_localRoot, "memory.json");
        await File.WriteAllTextAsync(localFile, "{ \"content\": \"local\" }");

        var localBackend = new FileSystemSyncBackend(_localRoot);
        var remoteBackend = new FileSystemSyncBackend(_remoteRoot);
        var localItem = (await localBackend.ListAsync()).Single();

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;
        await using var context = new AionDbContext(options, new TestWorkspaceContext());
        await context.Database.MigrateAsync();
        var outbox = new SyncOutboxService(context);
        var outboxItem = await outbox.EnqueueAsync(localItem, SyncAction.Upload);

        var engine = new SyncEngine(new NullLogger<SyncEngine>());
        var results = await engine.ApplyAsync(localBackend, remoteBackend, new[] { outboxItem.ToOperation() });

        var state = Assert.Single(results);
        Assert.Equal(SyncAction.Upload, state.Action);

        await outbox.MarkAppliedAsync(outboxItem.Id);

        var remotePath = Path.Combine(_remoteRoot, "memory.json");
        Assert.True(File.Exists(remotePath));
        var remoteContent = await File.ReadAllTextAsync(remotePath);
        Assert.Equal("{ \"content\": \"local\" }", remoteContent);

        var replay = await engine.ApplyAsync(localBackend, remoteBackend, new[] { outboxItem.ToOperation() });
        var replayState = Assert.Single(replay);
        Assert.Equal(SyncAction.None, replayState.Action);
    }

    [Fact]
    public async Task Replay_detects_conflict_when_remote_is_newer()
    {
        var localFile = Path.Combine(_localRoot, "memory.json");
        var remoteFile = Path.Combine(_remoteRoot, "memory.json");

        await File.WriteAllTextAsync(localFile, "{ \"content\": \"local\" }");
        await File.WriteAllTextAsync(remoteFile, "{ \"content\": \"remote\" }");

        var newer = DateTimeOffset.UtcNow;
        File.SetLastWriteTimeUtc(localFile, newer.UtcDateTime.AddMinutes(-10));
        File.SetLastWriteTimeUtc(remoteFile, newer.UtcDateTime);

        var localBackend = new FileSystemSyncBackend(_localRoot);
        var remoteBackend = new FileSystemSyncBackend(_remoteRoot);
        var localItem = (await localBackend.ListAsync()).Single();

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;
        await using var context = new AionDbContext(options, new TestWorkspaceContext());
        await context.Database.MigrateAsync();
        var outbox = new SyncOutboxService(context);
        var outboxItem = await outbox.EnqueueAsync(localItem, SyncAction.Upload);

        var engine = new SyncEngine(new NullLogger<SyncEngine>());
        var results = await engine.ApplyAsync(localBackend, remoteBackend, new[] { outboxItem.ToOperation() });

        var state = Assert.Single(results);
        Assert.Equal(SyncAction.Conflict, state.Action);
        Assert.NotNull(state.Conflict);
        Assert.Equal(SyncPrioritySide.Remote, state.Conflict!.PreferredSide);

        var remoteContent = await File.ReadAllTextAsync(remoteFile);
        Assert.Equal("{ \"content\": \"remote\" }", remoteContent);

        await outbox.MarkConflictAsync(outboxItem.Id, state.Conflict!.Reason ?? "Conflict detected.");
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

    private sealed class TestWorkspaceContext : IWorkspaceContext
    {
        public Guid WorkspaceId { get; } = TenancyDefaults.DefaultWorkspaceId;
    }
}
