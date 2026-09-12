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
    private readonly ShellViewModel _shell;

    public ObservableCollection<ThemeMode> AvailableThemes { get; } =
        new(Enum.GetValues<ThemeMode>());

    [ObservableProperty]
    private ThemeMode _selectedTheme;

    public SettingsViewModel(ISettingsService settingsService, ShellViewModel shell)
    {
        _settingsService = settingsService;
        _shell = shell;

        _selectedTheme = settingsService.Current.Theme;

        shell.CurrentPageTitle = "Settings";
    }

    partial void OnSelectedThemeChanged(ThemeMode value)
    {
        _settingsService.Current.Theme = value;
        _ = _settingsService.SaveAsync();

        ThemeApplier.Apply(value);

        _shell.IsDarkTheme = value == ThemeMode.Dark;
        _shell.StatusMessage = $"Theme set to {value}";
    }
}
