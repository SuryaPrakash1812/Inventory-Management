using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using InventoryManagement.App.Messages;
using InventoryManagement.App.Views.Pages;
using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.Products;
using InventoryManagement.Application.Purchases;
using InventoryManagement.Application.Suppliers;
using Wpf.Ui.Controls;

namespace InventoryManagement.App.ViewModels;

/// <summary>One sidebar entry.</summary>
public sealed record NavItem(string Title, SymbolRegular Icon, Type PageType);

/// <summary>One row of the global search results popup.</summary>
public sealed record SearchResult(string Group, string Title, string Subtitle, SymbolRegular Icon, Type PageType, string SearchText);

/// <summary>One low-stock alert in the notifications popup.</summary>
public sealed record StockAlert(string Name, string Detail, bool IsOutOfStock);

/// <summary>
/// ViewModel for the application shell (<see cref="Views.MainWindow"/>): the
/// sidebar, the top bar (global search, notifications, user menu) and the
/// status bar.
///
/// Navigation items are filtered by <see cref="IAuthorizationService"/> at
/// construction time (which happens only after a successful sign-in - see
/// App.xaml.cs), so a user simply never sees a menu entry for something they
/// don't have permission to open. This is the "do not hard-code authorization
/// logic throughout the UI" requirement: every future page just declares the
/// one permission name it needs here, instead of every page re-implementing
/// its own visibility checks. Global search only searches areas the user can
/// open, for the same reason.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private const int ResultsPerGroup = 4;

    private readonly IProductService _productService;
    private readonly ISupplierService _supplierService;
    private readonly IPurchaseService _purchaseService;
    private readonly IInventoryService _inventoryService;
    private readonly IAuthorizationService _authorizationService;
    private int _searchVersion;

    public string WindowTitle => "Inventory Management";

    public string AppVersion => $"v{typeof(ShellViewModel).Assembly.GetName().Version?.ToString(3)}";

    [ObservableProperty]
    private string _currentPageTitle = "Dashboard";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private NavItem? _selectedNavItem;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSearchOpen;

    [ObservableProperty]
    private SearchResult? _selectedSearchResult;

    [ObservableProperty]
    private bool _isNotificationsOpen;

    public string UserDisplayName { get; }

    public string UserRoleName { get; }

    public ObservableCollection<NavItem> MenuItems { get; } = new();

    public ObservableCollection<NavItem> FooterMenuItems { get; } = new();

    public ObservableCollection<SearchResult> SearchResults { get; } = new();

    public ObservableCollection<StockAlert> StockAlerts { get; } = new();

    public bool HasStockAlerts => StockAlerts.Count > 0;

    public bool HasNoSearchResults => IsSearchOpen && SearchResults.Count == 0 && SearchText.Trim().Length >= 2;

    public ShellViewModel(
        IAuthorizationService authorizationService,
        ICurrentUserSession session,
        IProductService productService,
        ISupplierService supplierService,
        IPurchaseService purchaseService,
        IInventoryService inventoryService)
    {
        _authorizationService = authorizationService;
        _productService = productService;
        _supplierService = supplierService;
        _purchaseService = purchaseService;
        _inventoryService = inventoryService;

        UserDisplayName = session.CurrentUser?.FullName ?? "Unknown User";
        UserRoleName = session.CurrentUser?.RoleName ?? string.Empty;

        void AddIfAuthorized(ObservableCollection<NavItem> collection, string title, SymbolRegular icon, Type pageType, string permission)
        {
            if (authorizationService.HasPermission(permission))
            {
                collection.Add(new NavItem(title, icon, pageType));
            }
        }

        AddIfAuthorized(MenuItems, "Dashboard", SymbolRegular.Home24, typeof(DashboardPage), "Dashboard.View");
        AddIfAuthorized(MenuItems, "Products", SymbolRegular.Box24, typeof(ProductsPage), "Products.View");
        AddIfAuthorized(MenuItems, "Categories", SymbolRegular.Tag24, typeof(CategoriesPage), "Products.View");
        AddIfAuthorized(MenuItems, "Suppliers", SymbolRegular.Building24, typeof(SuppliersPage), "Purchases.View");
        AddIfAuthorized(MenuItems, "Customers", SymbolRegular.PeopleTeam24, typeof(CustomersPage), "Sales.View");
        AddIfAuthorized(MenuItems, "Purchases", SymbolRegular.Cart24, typeof(PurchasesPage), "Purchases.View");
        AddIfAuthorized(MenuItems, "Sales", SymbolRegular.ReceiptMoney24, typeof(SalesPage), "Sales.View");
        AddIfAuthorized(MenuItems, "Inventory", SymbolRegular.BoxMultiple24, typeof(InventoryPage), "Inventory.View");
        AddIfAuthorized(MenuItems, "Stock Adjustments", SymbolRegular.ArrowSwap24, typeof(StockAdjustmentsPage), "Inventory.Adjust");
        AddIfAuthorized(MenuItems, "Reports", SymbolRegular.DataBarVertical24, typeof(ReportsPage), "Reports.View");
        AddIfAuthorized(MenuItems, "Users", SymbolRegular.Person24, typeof(UsersPage), "Users.View");
        AddIfAuthorized(MenuItems, "Backup", SymbolRegular.CloudArrowUp24, typeof(BackupPage), "Backup.Create");

        AddIfAuthorized(FooterMenuItems, "Settings", SymbolRegular.Settings24, typeof(SettingsPage), "Settings.Manage");
    }

    public bool CanOpenSettings => FooterMenuItems.Count > 0;

    public NavItem? FindNavItem(Type pageType) =>
        MenuItems.Concat(FooterMenuItems).FirstOrDefault(item => item.PageType == pageType);

    partial void OnSearchTextChanged(string value) => _ = RunSearchAsync(value);

    partial void OnIsSearchOpenChanged(bool value) => OnPropertyChanged(nameof(HasNoSearchResults));

    /// <summary>
    /// Debounced: waits briefly after the last keystroke, and a newer search
    /// supersedes an older one still in flight, so fast typing never shows
    /// results for a term the user has already typed past.
    /// </summary>
    private async Task RunSearchAsync(string text)
    {
        var version = ++_searchVersion;
        var term = text.Trim();

        if (term.Length < 2)
        {
            SearchResults.Clear();
            IsSearchOpen = false;
            OnPropertyChanged(nameof(HasNoSearchResults));
            return;
        }

        await Task.Delay(250);
        if (version != _searchVersion)
        {
            return;
        }

        var results = new List<SearchResult>();

        if (_authorizationService.HasPermission("Products.View"))
        {
            var products = await _productService.GetProductsAsync(new ProductQueryParameters { SearchTerm = term, PageSize = ResultsPerGroup });
            results.AddRange(products.Items.Select(p => new SearchResult(
                "Products", p.Name, $"{p.Sku} · {p.CurrentStock:0.##} {p.Unit} in stock", SymbolRegular.Box24, typeof(ProductsPage), p.Sku)));
        }

        if (_authorizationService.HasPermission("Purchases.View"))
        {
            var suppliers = await _supplierService.GetSuppliersAsync(new SupplierQueryParameters { SearchTerm = term, PageSize = ResultsPerGroup });
            results.AddRange(suppliers.Items.Select(s => new SearchResult(
                "Suppliers", s.Name, s.ContactPerson ?? s.Phone ?? s.Email ?? "Supplier", SymbolRegular.Building24, typeof(SuppliersPage), s.Name)));

            var purchases = await _purchaseService.GetPurchasesAsync(new PurchaseQueryParameters { SearchTerm = term, PageSize = ResultsPerGroup });
            results.AddRange(purchases.Items.Select(p => new SearchResult(
                "Purchases", p.PurchaseNumber, $"{p.SupplierName} · {p.Status} · {p.TotalAmount:N2}", SymbolRegular.Cart24, typeof(PurchasesPage), p.PurchaseNumber)));
        }

        if (version != _searchVersion)
        {
            return;
        }

        SearchResults.Clear();
        foreach (var result in results)
        {
            SearchResults.Add(result);
        }

        SelectedSearchResult = SearchResults.FirstOrDefault();
        IsSearchOpen = true;
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    [RelayCommand]
    private void OpenSearchResult(SearchResult? result)
    {
        result ??= SelectedSearchResult;
        if (result is null)
        {
            return;
        }

        IsSearchOpen = false;
        SearchText = string.Empty;
        WeakReferenceMessenger.Default.Send(new NavigateToPageMessage(result.PageType, result.SearchText));
    }

    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchOpen = false;
    }

    /// <summary>Products that need restocking - the bell's red dot and popup. Refreshed on every page change.</summary>
    public async Task RefreshAlertsAsync()
    {
        if (!_authorizationService.HasPermission("Inventory.View") && !_authorizationService.HasPermission("Products.View"))
        {
            return;
        }

        var lowStock = await _inventoryService.GetLowStockProductsAsync();

        StockAlerts.Clear();
        foreach (var product in lowStock.OrderBy(p => p.CurrentStock > 0).ThenBy(p => p.Name))
        {
            StockAlerts.Add(new StockAlert(
                product.Name,
                product.CurrentStock <= 0
                    ? $"{product.Sku} · out of stock"
                    : $"{product.Sku} · {product.CurrentStock:0.##} left (minimum {product.MinimumStock:0.##})",
                product.CurrentStock <= 0));
        }

        OnPropertyChanged(nameof(HasStockAlerts));
    }

    [RelayCommand]
    private void ToggleNotifications() => IsNotificationsOpen = !IsNotificationsOpen;

    [RelayCommand]
    private void CloseNotifications() => IsNotificationsOpen = false;

    [RelayCommand]
    private void ViewInventory()
    {
        IsNotificationsOpen = false;
        WeakReferenceMessenger.Default.Send(new NavigateToPageMessage(typeof(InventoryPage)));
    }

    [RelayCommand]
    private void OpenSettings() =>
        WeakReferenceMessenger.Default.Send(new NavigateToPageMessage(typeof(SettingsPage)));

    [RelayCommand]
    private void Logout()
    {
        WeakReferenceMessenger.Default.Send(new LogoutRequestedMessage());
    }
}
