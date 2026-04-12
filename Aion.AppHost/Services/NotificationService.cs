using Aion.Domain;
using Microsoft.Extensions.Logging;

namespace Aion.AppHost.Services;

public interface INotificationPlatformService
{
    Task ScheduleAsync(NotificationRequest request, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid notificationId, CancellationToken cancellationToken = default);
}

public sealed class NotificationService : INotificationService
{
    private readonly INotificationPlatformService _platform;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(INotificationPlatformService platform, ILogger<NotificationService> logger)
    {
        _platform = platform;
        _logger = logger;
    }

    public async Task ScheduleAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Scheduling local notification {NotificationId} at {ScheduledAt}", request.Id, request.ScheduledAt);
        await _platform.ScheduleAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task CancelAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Cancelling local notification {NotificationId}", notificationId);
        await _platform.CancelAsync(notificationId, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class NullNotificationPlatformService : INotificationPlatformService
{
    public Task ScheduleAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task CancelAsync(Guid notificationId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
