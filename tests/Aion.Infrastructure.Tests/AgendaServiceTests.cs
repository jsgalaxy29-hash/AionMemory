using Aion.Domain;
using Aion.Infrastructure;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Infrastructure.Tests;

public class AgendaServiceTests
{
    [Fact]
    public async Task Creating_event_schedules_notification_and_links_record()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;
        await using var context = new AionDbContext(options);
        await context.Database.MigrateAsync();

        var notifications = new RecordingNotificationService();
        var service = new AionAgendaService(context, notifications, NullLogger<AionAgendaService>.Instance);

        var recordId = Guid.NewGuid();
        var reminderAt = DateTimeOffset.UtcNow.AddHours(1);
        var evt = new S_Event
        {
            Title = "Suivi client",
            Start = DateTimeOffset.UtcNow.AddHours(2),
            ReminderAt = reminderAt,
            Links = new List<J_Event_Link>
            {
                new()
                {
                    TargetType = "Record",
                    TargetId = recordId
                }
            }
        };

        var saved = await service.AddEventAsync(evt);

        var stored = await context.Events.Include(e => e.Links).FirstAsync();
        Assert.Equal(saved.Id, stored.Id);
        Assert.Single(stored.Links);
        Assert.Equal(recordId, stored.Links.First().TargetId);
        Assert.Single(notifications.Scheduled);
        Assert.Equal(saved.Id, notifications.Scheduled[0].Id);
        Assert.Equal(reminderAt, notifications.Scheduled[0].ScheduledAt);
    }
}

file sealed class RecordingNotificationService : INotificationService
{
    public List<NotificationRequest> Scheduled { get; } = new();
    public List<Guid> Cancelled { get; } = new();

    public Task ScheduleAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        Scheduled.Add(request);
        return Task.CompletedTask;
    }

    public Task CancelAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        Cancelled.Add(notificationId);
        return Task.CompletedTask;
    }
}
