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
    /// <summary>The "All" entry at the top of the category filter (Id = Guid.Empty means no filter).</summary>
    private static readonly CategorySummary AllCategories = new(Guid.Empty, "All", null, null, null, 0);

    private readonly IInventoryService _inventoryService;
    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;

    private int _loadRequestVersion;
    private bool _isInitialized;

    [ObservableProperty]
    private int _pageSize = 10;

    [ObservableProperty]
    private int _outOfStockCount;

    public ObservableCollection<InventoryOverviewRow> Products { get; } = new();

    /// <summary>Category filter choices, "All" first.</summary>
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
        _isInitialized = true;
    }

    private async Task RefreshCategoriesAsync()
    {
        var categories = await _categoryService.GetCategoriesAsync();

        Categories.Clear();
        Categories.Add(AllCategories);
        foreach (var category in categories)
        {
            Categories.Add(category);
        }

        SelectedCategoryFilter = AllCategories;
    }

    // Filters apply as soon as they change (the search box applies as you type).
    partial void OnSelectedCategoryFilterChanged(CategorySummary? value) => ReloadFromFirstPage();

    partial void OnSelectedStatusFilterChanged(string value) => ReloadFromFirstPage();

    partial void OnPageSizeChanged(int value) => ReloadFromFirstPage();

    private void ReloadFromFirstPage()
    {
        if (!_isInitialized)
        {
            return;
        }

        PageNumber = 1;
        _ = LoadAsync();
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

        OutOfStockCount = lowStock.Count(p => p.CurrentStock <= 0);
        LowStockCount = lowStock.Count - OutOfStockCount;
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
        SelectedCategoryFilter = AllCategories;
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
            // The status is a computed label (InventoryOverviewRow), not a
            // stored column IProductService can filter by, so every matching
            // product is fetched, filtered by status, and only THEN paged.
            // (Filtering after paging - as this used to - showed only the
            // matching rows of one page, with a total that ignored the filter.)
            var result = await _productService.GetProductsAsync(new ProductQueryParameters
            {
                SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm,
                CategoryId = SelectedCategoryFilter is { } category && category.Id != Guid.Empty ? category.Id : null,
                IsActive = true,
                PageNumber = 1,
                PageSize = int.MaxValue,
            });

            if (requestVersion != _loadRequestVersion)
            {
                return;
            }

            var rows = result.Items.Select(p => new InventoryOverviewRow(p));
            if (SelectedStatusFilter != "All")
            {
                rows = rows.Where(r => r.StockStatus == SelectedStatusFilter);
            }

            var filtered = rows.ToList();
            TotalCount = filtered.Count;
            TotalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
            PageNumber = Math.Min(PageNumber, TotalPages);

            Products.Clear();
            foreach (var row in filtered.Skip((PageNumber - 1) * PageSize).Take(PageSize))
            {
                Products.Add(row);
            }
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
    private async Task CloseHistoryAsync()
    {
        IsViewingHistory = false;
        OnPropertyChanged(nameof(IsShowingOverview));

        // The overview is always reloaded when returning to it.
        await RefreshSummaryAsync();
        await LoadAsync();
    }
}
