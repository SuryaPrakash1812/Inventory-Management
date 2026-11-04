using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using InventoryManagement.App.Messages;
using InventoryManagement.App.Services;
using InventoryManagement.Application.Categories;
using InventoryManagement.Application.Products;

namespace InventoryManagement.App.ViewModels.Products;

public sealed partial class ProductsViewModel : ViewModelBase
{
    /// <summary>The "All" entry at the top of the category filter (Id = Guid.Empty means no filter).</summary>
    private static readonly CategorySummary AllCategories = new(Guid.Empty, "All", null, null, null, 0);

    private readonly IProductService _productService;
    private readonly ICategoryService _categoryService;
    private bool _isInitialized;

    public ObservableCollection<ProductSummary> Products { get; } = new();

    public ObservableCollection<CategorySummary> Categories { get; } = new();

    /// <summary><see cref="Categories"/> with "All" first - for the list filter, not the edit form.</summary>
    public ObservableCollection<CategorySummary> CategoryFilterOptions { get; } = new();

    // --- Summary cards (all active products, regardless of filters) ---

    [ObservableProperty] private int _activeProductCount;
    [ObservableProperty] private int _inStockCount;
    [ObservableProperty] private int _lowStockCount;
    [ObservableProperty] private int _outOfStockCount;
    [ObservableProperty] private string _inStockPercent = "0%";
    [ObservableProperty] private string _lowStockPercent = "0%";
    [ObservableProperty] private string _outOfStockPercent = "0%";

    public IReadOnlyList<ProductSortColumn> SortColumns { get; } = Enum.GetValues<ProductSortColumn>();

    public IReadOnlyList<string> ActiveFilterOptions { get; } = new[] { "All", "Active only", "Inactive only" };

    /// <summary>Suggestions for the editable Unit box - any other unit can still be typed.</summary>
    public IReadOnlyList<string> UnitOptions { get; } = new[] { "pcs", "box", "kg", "g", "ltr", "ml", "m", "ft", "pack", "set", "pair", "dozen" };

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private CategorySummary? _selectedCategoryFilter;

    [ObservableProperty]
    private string _selectedActiveFilter = "All";

    [ObservableProperty]
    private ProductSortColumn _selectedSortColumn = ProductSortColumn.Name;

    [ObservableProperty]
    private bool _sortDescending;

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
    private string? _statusMessage;

    public bool CanGoToPreviousPage => PageNumber > 1;

    public bool CanGoToNextPage => PageNumber < TotalPages;

    [ObservableProperty]
    private int _pageSize = 10;

    // --- Edit panel state ---

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private Guid? _editingProductId;

    [ObservableProperty]
    private string _formSku = string.Empty;

    [ObservableProperty]
    private string _formBarcode = string.Empty;

    [ObservableProperty]
    private string _formName = string.Empty;

    [ObservableProperty]
    private string _formDescription = string.Empty;

    [ObservableProperty]
    private string _formBrand = string.Empty;

    [ObservableProperty]
    private CategorySummary? _formCategory;

    [ObservableProperty]
    private string _formUnit = "pcs";

    [ObservableProperty]
    private decimal _formPurchasePrice;

    [ObservableProperty]
    private decimal _formSellingPrice;

    [ObservableProperty]
    private decimal _formTaxPercentage;

    [ObservableProperty]
    private decimal _formMinimumStock;

    [ObservableProperty]
    private decimal _formInitialStock;

    [ObservableProperty]
    private decimal _formCurrentStock;

    [ObservableProperty]
    private bool _formIsActive = true;

    /// <summary>Stock is only enterable when creating a new product - see CreateProductRequest.InitialStock remarks.</summary>
    public bool IsNewProduct => EditingProductId is null;

    private readonly NavigationIntent _navigationIntent;

    public ProductsViewModel(IProductService productService, ICategoryService categoryService, NavigationIntent navigationIntent)
    {
        _productService = productService;
        _categoryService = categoryService;
        _navigationIntent = navigationIntent;
    }

    public async Task InitializeAsync()
    {
        // Arriving from the global search: show that product.
        if (_navigationIntent.TryTake(typeof(Views.Pages.ProductsPage), out var search, out _) && search is not null)
        {
            SearchTerm = search;
        }

        await RefreshCategoriesAsync();
        await LoadAsync();
        await RefreshStatsAsync();
        _isInitialized = true;
    }

    private async Task RefreshCategoriesAsync()
    {
        var categories = await _categoryService.GetCategoriesAsync();
        var selectedId = SelectedCategoryFilter?.Id ?? Guid.Empty;

        Categories.Clear();
        CategoryFilterOptions.Clear();
        CategoryFilterOptions.Add(AllCategories);
        foreach (var category in categories)
        {
            Categories.Add(category);
            CategoryFilterOptions.Add(category);
        }

        SelectedCategoryFilter = CategoryFilterOptions.FirstOrDefault(c => c.Id == selectedId) ?? AllCategories;
    }

    /// <summary>Counts for the summary cards, over every active product - the same stock rules as the Dashboard.</summary>
    private async Task RefreshStatsAsync()
    {
        var all = await _productService.GetProductsAsync(new ProductQueryParameters { IsActive = true, PageSize = int.MaxValue });
        var products = all.Items;

        ActiveProductCount = products.Count;
        OutOfStockCount = products.Count(p => p.CurrentStock <= 0);
        LowStockCount = products.Count(p => p.CurrentStock > 0 && p.CurrentStock <= p.MinimumStock);
        InStockCount = products.Count - OutOfStockCount - LowStockCount;

        string Percent(int count) => products.Count == 0 ? "0%" : $"{100.0 * count / products.Count:0}%";
        InStockPercent = Percent(InStockCount);
        LowStockPercent = Percent(LowStockCount);
        OutOfStockPercent = Percent(OutOfStockCount);

        WeakReferenceMessenger.Default.Send(new StockDataChangedMessage());
    }

    // Filters apply as soon as they change (the search box applies as you type).
    partial void OnSelectedCategoryFilterChanged(CategorySummary? value) => ReloadFromFirstPage();

    partial void OnSelectedActiveFilterChanged(string value) => ReloadFromFirstPage();

    partial void OnSelectedSortColumnChanged(ProductSortColumn value) => ReloadFromFirstPage();

    partial void OnSortDescendingChanged(bool value) => ReloadFromFirstPage();

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
        SelectedActiveFilter = "All";
        SelectedSortColumn = ProductSortColumn.Name;
        SortDescending = false;
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

    private int _loadRequestVersion;

    private Guid? CategoryFilterId =>
        SelectedCategoryFilter is { } category && category.Id != Guid.Empty ? category.Id : null;

    private async Task LoadAsync()
    {
        var requestVersion = ++_loadRequestVersion;

        IsBusy = true;
        try
        {
            bool? isActive = SelectedActiveFilter switch
            {
                "Active only" => true,
                "Inactive only" => false,
                _ => null,
            };

            var result = await _productService.GetProductsAsync(new ProductQueryParameters
            {
                SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm,
                CategoryId = CategoryFilterId,
                IsActive = isActive,
                SortColumn = SelectedSortColumn,
                SortDescending = SortDescending,
                PageNumber = PageNumber,
                PageSize = PageSize,
            });

            // If another LoadAsync started (and possibly already finished)
            // while this one was awaiting the database, this result is
            // stale - a newer request's results (or an even-newer one still
            // in flight) should win, not whichever call happens to finish
            // last. This is what "filters not working right one after
            // another" actually was: two overlapping fire-and-forget
            // searches racing, with the slower one able to overwrite the
            // faster/newer one's correct results.
            if (requestVersion != _loadRequestVersion)
            {
                return;
            }

            Products.Clear();
            foreach (var product in result.Items)
            {
                Products.Add(product);
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
    private void StartCreate()
    {
        ErrorMessage = null;
        StatusMessage = null;
        EditingProductId = null;
        FormSku = string.Empty;
        FormBarcode = string.Empty;
        FormName = string.Empty;
        FormDescription = string.Empty;
        FormBrand = string.Empty;
        FormCategory = Categories.FirstOrDefault();
        FormUnit = "pcs";
        FormPurchasePrice = 0;
        FormSellingPrice = 0;
        FormTaxPercentage = 0;
        FormMinimumStock = 0;
        FormInitialStock = 0;
        FormCurrentStock = 0;
        FormIsActive = true;
        IsEditing = true;

        OnPropertyChanged(nameof(IsNewProduct));
    }

    [RelayCommand]
    private async Task StartEditAsync(ProductSummary product)
    {
        ErrorMessage = null;
        StatusMessage = null;

        var detail = await _productService.GetProductByIdAsync(product.Id);
        if (detail is null)
        {
            ErrorMessage = "Product not found - it may have just been removed.";
            return;
        }

        EditingProductId = detail.Id;
        FormSku = detail.Sku;
        FormBarcode = detail.Barcode ?? string.Empty;
        FormName = detail.Name;
        FormDescription = detail.Description ?? string.Empty;
        FormBrand = detail.Brand ?? string.Empty;
        FormCategory = Categories.FirstOrDefault(c => c.Id == detail.CategoryId);
        FormUnit = detail.Unit;
        FormPurchasePrice = detail.PurchasePrice;
        FormSellingPrice = detail.SellingPrice;
        FormTaxPercentage = detail.TaxPercentage;
        FormMinimumStock = detail.MinimumStock;
        FormCurrentStock = detail.CurrentStock;
        FormIsActive = detail.IsActive;
        IsEditing = true;

        OnPropertyChanged(nameof(IsNewProduct));
    }

    [RelayCommand]
    private async Task CancelEditAsync()
    {
        IsEditing = false;
        ErrorMessage = null;

        // The list is always reloaded when returning to it.
        await RefreshCategoriesAsync();
        await LoadAsync();
        await RefreshStatsAsync();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        if (FormCategory is null)
        {
            ErrorMessage = "A category must be selected.";
            return;
        }

        IsBusy = true;
        try
        {
            if (EditingProductId is null)
            {
                var createResult = await _productService.CreateProductAsync(new CreateProductRequest(
                    FormSku, FormBarcode, FormName, FormDescription, FormBrand, FormCategory.Id, FormUnit,
                    FormPurchasePrice, FormSellingPrice, FormTaxPercentage, FormMinimumStock, FormInitialStock));

                if (createResult.IsFailure)
                {
                    ErrorMessage = createResult.Error;
                    return;
                }
            }
            else
            {
                var updateResult = await _productService.UpdateProductAsync(new UpdateProductRequest(
                    EditingProductId.Value, FormBarcode, FormName, FormDescription, FormBrand, FormCategory.Id,
                    FormUnit, FormPurchasePrice, FormSellingPrice, FormTaxPercentage, FormMinimumStock, FormIsActive));

                if (updateResult.IsFailure)
                {
                    ErrorMessage = updateResult.Error;
                    return;
                }
            }

            IsEditing = false;
            await LoadAsync();
            await RefreshStatsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeactivateAsync(ProductSummary product)
    {
        ErrorMessage = null;

        var result = await _productService.DeactivateProductAsync(product.Id);
        if (result.IsFailure)
        {
            ErrorMessage = result.Error;
            return;
        }

        await LoadAsync();
        await RefreshStatsAsync();
    }

    /// <summary>Exports every product matching the current filters (not just the current page) to the given file path.</summary>
    public async Task ExportToFileAsync(string filePath)
    {
        bool? isActive = SelectedActiveFilter switch
        {
            "Active only" => true,
            "Inactive only" => false,
            _ => null,
        };

        var csv = await _productService.ExportToCsvAsync(new ProductQueryParameters
        {
            SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm,
            CategoryId = CategoryFilterId,
            IsActive = isActive,
            PageNumber = 1,
            PageSize = int.MaxValue,
        });

        await File.WriteAllTextAsync(filePath, csv);
        StatusMessage = $"Exported to {filePath}.";
    }

    public async Task ImportFromFileAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var result = await _productService.ImportFromCsvAsync(content);

        StatusMessage = $"Import complete: {result.CreatedCount} created, {result.UpdatedCount} updated, {result.Errors.Count} error(s).";
        if (result.Errors.Count > 0)
        {
            ErrorMessage = string.Join(
                Environment.NewLine, result.Errors.Take(10).Select(e => $"Row {e.RowNumber}: {e.Message}"));
        }
        else
        {
            ErrorMessage = null;
        }

        await RefreshCategoriesAsync();
        await LoadAsync();
        await RefreshStatsAsync();
    }
}
