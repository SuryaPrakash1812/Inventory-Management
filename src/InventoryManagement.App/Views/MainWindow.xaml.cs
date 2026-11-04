using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Navigation;
using CommunityToolkit.Mvvm.Messaging;
using InventoryManagement.App.Messages;
using InventoryManagement.App.Services;
using InventoryManagement.App.ViewModels;
using InventoryManagement.App.Views.Pages;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace InventoryManagement.App.Views;

/// <summary>
/// The application shell: sidebar, top bar (search, notifications, user
/// menu), page area and status bar. Code-behind owns only what is
/// inherently view-specific - hosting pages in the Frame, keeping the two
/// sidebar lists' selection in step, and keyboard handling - everything
/// else lives in <see cref="ShellViewModel"/>.
///
/// Pages are resolved through WPF-UI's DI page provider (a fresh instance
/// per navigation, as before) and shown in a plain Frame whose back stack
/// is cleared after every navigation, so previous pages are not kept alive.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly INavigationViewPageProvider _pageProvider;
    private readonly NavigationIntent _navigationIntent;
    private bool _syncingSelection;

    public ShellViewModel ViewModel { get; }

    public MainWindow(ShellViewModel viewModel, INavigationViewPageProvider pageProvider, NavigationIntent navigationIntent)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        _pageProvider = pageProvider;
        _navigationIntent = navigationIntent;

        InitializeComponent();

        ApplicationThemeManager.Apply(this);

        ContentFrame.Navigated += OnFrameNavigated;
        PreviewKeyDown += OnWindowPreviewKeyDown;

        WeakReferenceMessenger.Default.Register<NavigateToPageMessage>(this, (_, message) =>
            NavigateTo(message.PageType, message.SearchText, message.CreateNew));
        WeakReferenceMessenger.Default.Register<StockDataChangedMessage>(this, (_, _) => _ = ViewModel.RefreshAlertsAsync());
        Closed += (_, _) => WeakReferenceMessenger.Default.UnregisterAll(this);

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        if (ViewModel.MenuItems.Concat(ViewModel.FooterMenuItems).FirstOrDefault() is { } first)
        {
            NavigateTo(first.PageType);
        }
    }

    private void NavigateTo(Type pageType, string? searchText = null, bool createNew = false)
    {
        var item = ViewModel.FindNavItem(pageType);
        if (item is null)
        {
            // Not in this user's menu, so they lack the permission for it.
            return;
        }

        _navigationIntent.Set(pageType, searchText, createNew);

        if (_pageProvider.GetPage(pageType) is not { } page)
        {
            return;
        }

        ContentFrame.Navigate(page);

        ViewModel.CurrentPageTitle = item.Title;
        ViewModel.SelectedNavItem = item;
        SyncSidebarSelection(item);

        _ = ViewModel.RefreshAlertsAsync();
    }

    private void SyncSidebarSelection(NavItem item)
    {
        _syncingSelection = true;
        try
        {
            MainNavList.SelectedItem = ViewModel.MenuItems.Contains(item) ? item : null;
            FooterNavList.SelectedItem = ViewModel.FooterMenuItems.Contains(item) ? item : null;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || sender is not ListBox { SelectedItem: NavItem item } || item == ViewModel.SelectedNavItem)
        {
            return;
        }

        NavigateTo(item.PageType);
    }

    /// <summary>
    /// Clicking the page you are already on opens it afresh (back at its list,
    /// with reloaded data). Selecting a different item is handled by
    /// OnNavSelectionChanged; on mouse-down the selection has not changed yet.
    /// </summary>
    private void OnNavItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject)?.FindAncestor<ListBoxItem>() is { DataContext: NavItem item } && item == ViewModel.SelectedNavItem)
        {
            NavigateTo(item.PageType);
        }
    }

    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        while (ContentFrame.CanGoBack)
        {
            ContentFrame.RemoveBackEntry();
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnSearchBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var results = ViewModel.SearchResults;
        var index = ViewModel.SelectedSearchResult is null ? -1 : results.IndexOf(ViewModel.SelectedSearchResult);

        switch (e.Key)
        {
            case Key.Down when results.Count > 0:
                ViewModel.SelectedSearchResult = results[Math.Min(results.Count - 1, index + 1)];
                ViewModel.IsSearchOpen = true;
                e.Handled = true;
                break;
            case Key.Up when results.Count > 0:
                ViewModel.SelectedSearchResult = results[Math.Max(0, index - 1)];
                e.Handled = true;
                break;
            case Key.Enter:
                ViewModel.OpenSearchResultCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                ViewModel.SearchText = string.Empty;
                ViewModel.CloseSearchCommand.Execute(null);
                Keyboard.ClearFocus();
                e.Handled = true;
                break;
        }
    }

    private void OnSearchResultClicked(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject)?.FindAncestor<ListBoxItem>() is { DataContext: SearchResult result })
        {
            ViewModel.OpenSearchResultCommand.Execute(result);
        }
    }

    private void OnUserMenuClick(object sender, RoutedEventArgs e)
    {
        if (UserMenuButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = UserMenuButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }
}

internal static class VisualTreeExtensions
{
    public static T? FindAncestor<T>(this DependencyObject start)
        where T : DependencyObject
    {
        for (var current = start; current is not null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
