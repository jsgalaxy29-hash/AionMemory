using Microsoft.AspNetCore.Components.WebView.Maui;

namespace Aion.AppHost.Pages;

public partial class BlazorHostPage : ContentPage
{
    public static readonly BindableProperty StartPathProperty = BindableProperty.Create(
        nameof(StartPath), typeof(string), typeof(BlazorHostPage), default(string));

    public string? StartPath
    {
        get => (string?)GetValue(StartPathProperty);
        set => SetValue(StartPathProperty, value);
    }

    public BlazorHostPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (WebView is null)
        {
            return;
        }

        WebView.StartPath = string.IsNullOrWhiteSpace(StartPath) ? "/home" : StartPath;
    }
}