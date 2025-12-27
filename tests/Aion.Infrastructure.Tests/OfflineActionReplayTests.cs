using Aion.Domain;
using Aion.Infrastructure;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Infrastructure.Tests;

public sealed class OfflineActionReplayTests
{
    [Fact]
    public async Task Offline_action_is_replayed_and_marked_applied()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;
        await using var context = new AionDbContext(options, new TestWorkspaceContext());
        await context.Database.MigrateAsync();

        var queue = new OfflineActionQueueService(context, new NullLogger<OfflineActionQueueService>());
        var outbox = new SyncOutboxService(context, new NullLifeService());
        var replay = new OfflineActionReplayService(queue, outbox, new NullLogger<OfflineActionReplayService>());

        var tableId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var action = new OfflineRecordAction(
            Guid.NewGuid(),
            tableId,
            recordId,
            OfflineActionType.Create,
            "{ \"name\": \"Test\" }",
            DateTimeOffset.UtcNow,
            OfflineActionStatus.Pending,
            null,
            null);

        var queued = await queue.EnqueueAsync(action);
        var pending = await queue.GetPendingAsync(tableId);
        Assert.Single(pending);

        await replay.ReplayPendingAsync();

        var pendingAfter = await queue.GetPendingAsync(tableId);
        Assert.Empty(pendingAfter);

        var outboxPending = await outbox.GetPendingAsync();
        var outboxItem = Assert.Single(outboxPending);
        Assert.Equal(SyncAction.Upload, outboxItem.Action);
        Assert.Equal($"offline-actions/{tableId:N}/{recordId:N}/{queued.Id:N}.json", outboxItem.Path);
    }

    private sealed class TestWorkspaceContext : IWorkspaceContext
    {
        public Guid WorkspaceId { get; } = TenancyDefaults.DefaultWorkspaceId;
    }

    private sealed class NullLifeService : ILifeService
    {
        public Task<S_HistoryEvent> AddHistoryAsync(S_HistoryEvent evt, CancellationToken cancellationToken = default)
            => Task.FromResult(evt);

        public Task<TimelinePage> GetTimelinePageAsync(TimelineQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new TimelinePage(Array.Empty<S_HistoryEvent>(), false, 0));

        public Task<IEnumerable<S_HistoryEvent>> GetTimelineAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<S_HistoryEvent>>(Array.Empty<S_HistoryEvent>());
    }
}
