using Aion.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;

namespace Aion.AppHost.Services;

public sealed class OfflineActionReplayCoordinator : IDisposable
{
    private readonly IConnectivityService _connectivityService;
    private readonly IOfflineActionReplayService _replayService;
    private readonly ILogger<OfflineActionReplayCoordinator> _logger;

    public OfflineActionReplayCoordinator(
        IConnectivityService connectivityService,
        IOfflineActionReplayService replayService,
        ILogger<OfflineActionReplayCoordinator> logger)
    {
        _connectivityService = connectivityService;
        _replayService = replayService;
        _logger = logger;

        _connectivityService.ConnectivityChanged += OnConnectivityChanged;
        if (_connectivityService.IsOnline)
        {
            _ = ReplayAsync();
        }
    }

    public void Dispose()
    {
        _connectivityService.ConnectivityChanged -= OnConnectivityChanged;
    }

    private async void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
        {
            await ReplayAsync().ConfigureAwait(false);
        }
    }

    private async Task ReplayAsync()
    {
        try
        {
            await _replayService.ReplayPendingAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to replay offline actions after connectivity change.");
        }
    }
}
