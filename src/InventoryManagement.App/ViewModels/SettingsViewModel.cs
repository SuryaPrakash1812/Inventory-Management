using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using InventoryManagement.App.Services;
using InventoryManagement.Application.Settings;

namespace InventoryManagement.App.ViewModels;

/// <summary>
/// Backs the Settings page. Currently exposes just the theme preference, but
/// is the natural home for the rest of "basic settings infrastructure"
/// (backup defaults, locale, etc.) as later stages add them.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;

    public ObservableCollection<ThemeMode> AvailableThemes { get; } =
        new(Enum.GetValues<ThemeMode>());

    [ObservableProperty]
    private ThemeMode _selectedTheme;

    /// <summary>Local confirmation text shown on this page - not the shared shell status bar, since a page never depends on the shell's specific ShellViewModel instance (see MainWindow.OnSelectionChanged remarks).</summary>
    [ObservableProperty]
    private string? _confirmationMessage;

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;

        _selectedTheme = settingsService.Current.Theme;
    }

    partial void OnSelectedThemeChanged(ThemeMode value)
    {
        _settingsService.Current.Theme = value;
        _ = _settingsService.SaveAsync();

        ThemeApplier.Apply(value);

        ConfirmationMessage = $"Theme set to {value}.";
    }
}
