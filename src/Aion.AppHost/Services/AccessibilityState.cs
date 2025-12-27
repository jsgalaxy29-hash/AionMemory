using System.Globalization;
using Microsoft.Maui.Storage;

namespace Aion.AppHost.Services;

public enum AccessibilityTheme
{
    System,
    Light,
    Dark,
    HighContrast
}

public sealed class AccessibilityState
{
    private const string ThemeKey = "aion.ui.theme";
    private const string FontScaleKey = "aion.ui.fontScale";
    private const string SimplifiedNavKey = "aion.ui.nav.simplified";

    public AccessibilityTheme Theme { get; private set; }
    public double FontScale { get; private set; }
    public bool SimplifiedNavigation { get; private set; }

    public event Action? OnChange;

    public AccessibilityState()
    {
        Theme = ReadTheme();
        FontScale = ReadFontScale();
        SimplifiedNavigation = Preferences.Default.Get(SimplifiedNavKey, false);
    }

    public void SetTheme(AccessibilityTheme theme)
    {
        if (Theme == theme)
        {
            return;
        }

        Theme = theme;
        Preferences.Default.Set(ThemeKey, theme.ToString());
        Notify();
    }

    public void SetFontScale(double scale)
    {
        var normalized = NormalizeScale(scale);
        if (Math.Abs(FontScale - normalized) < 0.001)
        {
            return;
        }

        FontScale = normalized;
        Preferences.Default.Set(FontScaleKey, normalized.ToString(CultureInfo.InvariantCulture));
        Notify();
    }

    public void SetSimplifiedNavigation(bool enabled)
    {
        if (SimplifiedNavigation == enabled)
        {
            return;
        }

        SimplifiedNavigation = enabled;
        Preferences.Default.Set(SimplifiedNavKey, enabled);
        Notify();
    }

    private static AccessibilityTheme ReadTheme()
    {
        var stored = Preferences.Default.Get(ThemeKey, AccessibilityTheme.System.ToString());
        return Enum.TryParse(stored, ignoreCase: true, out AccessibilityTheme theme)
            ? theme
            : AccessibilityTheme.System;
    }

    private static double ReadFontScale()
    {
        var stored = Preferences.Default.Get(FontScaleKey, string.Empty);
        if (double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
        {
            return NormalizeScale(scale);
        }

        return 1.0;
    }

    private static double NormalizeScale(double scale)
        => scale switch
        {
            < 0.85 => 0.85,
            > 1.6 => 1.6,
            _ => Math.Round(scale, 2)
        };

    private void Notify() => OnChange?.Invoke();
}
