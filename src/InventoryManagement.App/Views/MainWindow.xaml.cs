using System.Windows;
using InventoryManagement.App.ViewModels;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace InventoryManagement.App.Views;

/// <summary>
/// The application shell: title bar, sidebar navigation, header (current page
/// title + theme toggle + user), content frame, and status bar. Code-behind
/// only wires the DataContext and the WPF-UI navigation plumbing - everything
/// else lives in <see cref="ShellViewModel"/>.
/// </summary>
public partial class MainWindow : FluentWindow
{
    public ShellViewModel ViewModel { get; }

    public MainWindow(ShellViewModel viewModel, INavigationViewPageProvider pageProvider)
    {
        ViewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        ApplicationThemeManager.Apply(this);

        // ApplicationThemeManager.Apply(theme, backdrop) (called from
        // ThemeApplier when the user toggles the theme) updates the app-wide
        // resource dictionaries, but Mica is a native per-window DWM effect -
        // it does not automatically refresh on windows that are already
        // open. Without this, a theme toggle updates colors/brushes but
        // leaves this window's actual backdrop composition stuck on the old
        // theme, which is exactly what produced the "everything unreadable"
        // symptom: mismatched old-backdrop/new-foreground colors.
        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
        Closed += (_, _) => ApplicationThemeManager.Changed -= OnApplicationThemeChanged;

        RootNavigation.SetPageProviderService(pageProvider);
        RootNavigation.SelectionChanged += OnSelectionChanged;

        // NavigationView's internal content presenter isn't ready until its
        // control template has been applied, which hasn't happened yet at
        // constructor time - navigating here throws a NullReferenceException
        // deep inside UpdateContent. Deferring to Loaded guarantees the
        // template is applied first.
        Loaded += OnLoaded;
    }

    private void OnApplicationThemeChanged(ApplicationTheme currentApplicationTheme, System.Windows.Media.Color systemAccent) =>
        ApplicationThemeManager.Apply(this);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        var firstItem = ViewModel.MenuItems.Concat(ViewModel.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.TargetPageType is not null);

        if (firstItem?.TargetPageType is { } targetPageType)
        {
            RootNavigation.Navigate(targetPageType);
        }
    }

    /// <summary>
    /// Updates the header title whenever the sidebar selection changes. This
    /// is deliberately the one and only place CurrentPageTitle gets set -
    /// pages themselves used to set it via an injected ShellViewModel, but
    /// since pages are resolved by WPF-UI's own page provider (not
    /// necessarily through the same DI scope as this window), that could
    /// silently update a *different* ShellViewModel instance than the one
    /// this window is bound to. Reading the selection directly from the
    /// NavigationView this window already owns has no such ambiguity.
    /// </summary>
    private void OnSelectionChanged(NavigationView sender, RoutedEventArgs args)
    {
        if (sender.SelectedItem is NavigationViewItem { Content: string title })
        {
            ViewModel.CurrentPageTitle = title;
        }
    }
}
