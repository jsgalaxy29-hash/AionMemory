using Aion.AppHost.Services;
using Aion.Domain;

namespace Aion.AppHost.Platforms.iOS;

public sealed class IosNotificationPlatformService : INotificationPlatformService
{
    public Task ScheduleAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task CancelAsync(Guid notificationId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
