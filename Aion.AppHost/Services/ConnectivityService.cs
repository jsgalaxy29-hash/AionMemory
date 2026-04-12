using Microsoft.Maui.Networking;

namespace Aion.AppHost.Services;

public interface IConnectivityService
{
    bool IsOnline { get; }
    event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;
}

public sealed class MauiConnectivityService : IConnectivityService, IDisposable
{
    public MauiConnectivityService()
    {
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;

    public void Dispose()
    {
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        ConnectivityChanged?.Invoke(this, e);
    }
}
