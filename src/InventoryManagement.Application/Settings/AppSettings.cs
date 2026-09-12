namespace InventoryManagement.Application.Settings;

/// <summary>
/// How the application should decide its light/dark appearance.
/// </summary>
public enum ThemeMode
{
    /// <summary>Follow the Windows setting.</summary>
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>
/// The full set of persisted user preferences. Kept as a plain, serializable
/// POCO with no dependency on WPF or any specific storage mechanism - the UI
/// layer maps <see cref="ThemeMode"/> onto whatever theming API it uses.
/// </summary>
public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;
}
