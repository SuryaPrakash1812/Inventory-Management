using InventoryManagement.Application.Settings;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace InventoryManagement.App.Services;

/// <summary>
/// Single place that knows how to turn our UI-agnostic <see cref="ThemeMode"/>
/// into an actual WPF-UI theme change. Used both at startup (to apply the
/// persisted preference before the first window shows) and from the Settings
/// page / theme-toggle button, so there is exactly one implementation to
/// get right.
/// </summary>
public static class ThemeApplier
{
    public static void Apply(ThemeMode mode)
    {
        var resolved = mode switch
        {
            ThemeMode.Light => ApplicationTheme.Light,
            ThemeMode.Dark => ApplicationTheme.Dark,
            ThemeMode.System => DetectSystemTheme(),
            _ => ApplicationTheme.Light,
        };

        ApplicationThemeManager.Apply(resolved, WindowBackdropType.Mica);
    }

    /// <summary>
    /// Reads the "apps use light theme" preference Windows stores in the
    /// registry. Best-effort: any failure (missing key, restricted registry
    /// access, etc.) falls back to Light rather than throwing.
    /// </summary>
    private static ApplicationTheme DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            if (key?.GetValue("AppsUseLightTheme") is int appsUseLightTheme)
            {
                return appsUseLightTheme == 0 ? ApplicationTheme.Dark : ApplicationTheme.Light;
            }
        }
        catch (Exception)
        {
            // Registry access can fail in locked-down environments - that's
            // not worth crashing or even logging loudly over, just fall back.
        }

        return ApplicationTheme.Light;
    }
}
