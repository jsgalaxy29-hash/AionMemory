using Aion.Domain;

namespace Aion.Infrastructure.Services;

public sealed class NullNotificationService : INotificationService
{
    public Task ScheduleAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task CancelAsync(Guid notificationId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
