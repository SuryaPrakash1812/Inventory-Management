using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.Application.Categories;
using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.Products;

namespace InventoryManagement.App.ViewModels.Inventory;

/// <summary>
/// Deliberately reuses existing Application-layer methods rather than
/// introducing new ones: IProductService.GetProductsAsync for the overview
/// grid (it already returns CurrentStock/MinimumStock/PurchasePrice per
/// product), and IInventoryService.GetLowStockProductsAsync/
/// GetStockValuationAsync/GetStockHistoryAsync for the rest. No new
/// backend code was needed for this screen at all.
/// </summary>
public sealed partial class InventoryViewModel : ViewModelBase
{
    private readonly IInventoryService _inventoryService;
    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;

    private const int PageSize = 25;
    private int _loadRequestVersion;

    public ObservableCollection<InventoryOverviewRow> Products { get; } = new();

    public ObservableCollection<CategorySummary> Categories { get; } = new();

    public ObservableCollection<LowStockProduct> LowStockProducts { get; } = new();

    public ObservableCollection<StockHistoryEntry> HistoryEntries { get; } = new();

    public IReadOnlyList<string> StatusFilterOptions { get; } = new[] { "All", "In Stock", "Low Stock", "Out of Stock" };

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private CategorySummary? _selectedCategoryFilter;

    [ObservableProperty]
    private string _selectedStatusFilter = "All";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _pageNumber = 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalPages = 1;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private int _valuationProductCount;

    [ObservableProperty]
    private decimal _valuationTotalCostValue;

    [ObservableProperty]
    private decimal _valuationTotalRetailValue;

    [ObservableProperty]
    private int _lowStockCount;

    [ObservableProperty]
    private bool _isViewingHistory;

    [ObservableProperty]
    private string? _historyProductName;

    public bool IsShowingOverview => !IsViewingHistory;

    public bool CanGoToPreviousPage => PageNumber > 1;

    public bool CanGoToNextPage => PageNumber < TotalPages;

    public InventoryViewModel(IInventoryService inventoryService, IProductService productService, ICategoryService categoryService)
    {
        _inventoryService = inventoryService;
        _productService = productService;
        _categoryService = categoryService;
    }

    public async Task InitializeAsync()
    {
        await RefreshCategoriesAsync();
        await RefreshSummaryAsync();
        await LoadAsync();
    }

    private async Task RefreshCategoriesAsync()
    {
        var categories = await _categoryService.GetCategoriesAsync();

        Categories.Clear();
        foreach (var category in categories)
        {
            Categories.Add(category);
        }
    }

    private async Task RefreshSummaryAsync()
    {
        var valuation = await _inventoryService.GetStockValuationAsync();
        ValuationProductCount = valuation.ProductCount;
        ValuationTotalCostValue = valuation.TotalCostValue;
        ValuationTotalRetailValue = valuation.TotalRetailValue;

        var lowStock = await _inventoryService.GetLowStockProductsAsync();
        LowStockProducts.Clear();
        foreach (var product in lowStock)
        {
            LowStockProducts.Add(product);
        }

        LowStockCount = lowStock.Count;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        PageNumber = 1;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        SearchTerm = string.Empty;
        SelectedCategoryFilter = null;
        SelectedStatusFilter = "All";
        PageNumber = 1;
        await LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousPage))]
    private async Task PreviousPageAsync()
    {
        if (PageNumber > 1)
        {
            PageNumber--;
            await LoadAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextPage))]
    private async Task NextPageAsync()
    {
        if (PageNumber < TotalPages)
        {
            PageNumber++;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        var requestVersion = ++_loadRequestVersion;

        IsBusy = true;
        try
        {
            // The status filter is applied client-side after fetching, since
            // it is a computed display label (InventoryOverviewRow), not a
            // stored column IProductService's query can filter by server-side.
            var result = await _productService.GetProductsAsync(new ProductQueryParameters
            {
                SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm,
                CategoryId = SelectedCategoryFilter?.Id,
                IsActive = true,
                PageNumber = PageNumber,
                PageSize = PageSize,
            });

            if (requestVersion != _loadRequestVersion)
            {
                return;
            }

            var rows = result.Items.Select(p => new InventoryOverviewRow(p));

            rows = SelectedStatusFilter switch
            {
                "In Stock" => rows.Where(r => r.StockStatus == "In Stock"),
                "Low Stock" => rows.Where(r => r.StockStatus == "Low Stock"),
                "Out of Stock" => rows.Where(r => r.StockStatus == "Out of Stock"),
                _ => rows,
            };

            Products.Clear();
            foreach (var row in rows)
            {
                Products.Add(row);
            }

            TotalCount = result.TotalCount;
            TotalPages = Math.Max(1, result.TotalPages);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ViewHistoryAsync(InventoryOverviewRow row)
    {
        ErrorMessage = null;
        HistoryProductName = $"{row.Product.Sku} - {row.Product.Name}";

        var history = await _inventoryService.GetStockHistoryAsync(new StockHistoryQuery
        {
            ProductId = row.Product.Id,
            PageSize = 100,
        });

        HistoryEntries.Clear();
        foreach (var entry in history.Items)
        {
            HistoryEntries.Add(entry);
        }

        IsViewingHistory = true;
        OnPropertyChanged(nameof(IsShowingOverview));
    }

    [RelayCommand]
    private void CloseHistory()
    {
        IsViewingHistory = false;
        OnPropertyChanged(nameof(IsShowingOverview));
    }
}
