using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.Products;
using InventoryManagement.Application.Purchases;
using InventoryManagement.Application.Sales;

namespace InventoryManagement.App.ViewModels.Reports;

/// <summary>
/// Every report reuses an existing Application-layer query method - no new
/// backend code. A report-type selector switches which single collection
/// is populated and shown; only one report's data is held at a time.
/// </summary>
public sealed partial class ReportsViewModel : ViewModelBase
{
    private readonly IProductService _productService;
    private readonly IInventoryService _inventoryService;
    private readonly IPurchaseService _purchaseService;
    private readonly ISaleService _saleService;

    public IReadOnlyList<string> ReportTypes { get; } =
        new[] { "Inventory", "Low Stock", "Purchases", "Sales", "Stock Movements" };

    [ObservableProperty]
    private string _selectedReportType = "Inventory";

    [ObservableProperty]
    private DateTime? _fromDate;

    [ObservableProperty]
    private DateTime? _toDate;

    public ObservableCollection<ProductSummary> InventoryReport { get; } = new();

    public ObservableCollection<LowStockProduct> LowStockReport { get; } = new();

    public ObservableCollection<PurchaseSummary> PurchaseReport { get; } = new();

    public ObservableCollection<SaleSummary> SaleReport { get; } = new();

    public ObservableCollection<StockHistoryEntry> StockMovementReport { get; } = new();

    public bool IsShowingInventory => SelectedReportType == "Inventory";

    public bool IsShowingLowStock => SelectedReportType == "Low Stock";

    public bool IsShowingPurchases => SelectedReportType == "Purchases";

    public bool IsShowingSales => SelectedReportType == "Sales";

    public bool IsShowingStockMovements => SelectedReportType == "Stock Movements";

    public bool ShowsDateRangeFilter => SelectedReportType is "Purchases" or "Sales" or "Stock Movements";

    public ReportsViewModel(
        IProductService productService, IInventoryService inventoryService, IPurchaseService purchaseService, ISaleService saleService)
    {
        _productService = productService;
        _inventoryService = inventoryService;
        _purchaseService = purchaseService;
        _saleService = saleService;
    }

    public async Task InitializeAsync() => await RunReportAsync();

    partial void OnSelectedReportTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsShowingInventory));
        OnPropertyChanged(nameof(IsShowingLowStock));
        OnPropertyChanged(nameof(IsShowingPurchases));
        OnPropertyChanged(nameof(IsShowingSales));
        OnPropertyChanged(nameof(IsShowingStockMovements));
        OnPropertyChanged(nameof(ShowsDateRangeFilter));

        _ = RunReportAsync();
    }

    [RelayCommand]
    private async Task RunReportAsync()
    {
        IsBusy = true;
        try
        {
            switch (SelectedReportType)
            {
                case "Inventory":
                    var products = await _productService.GetProductsAsync(new ProductQueryParameters { PageSize = 500, IsActive = true });
                    InventoryReport.Clear();
                    foreach (var product in products.Items)
                    {
                        InventoryReport.Add(product);
                    }

                    break;

                case "Low Stock":
                    var lowStock = await _inventoryService.GetLowStockProductsAsync();
                    LowStockReport.Clear();
                    foreach (var product in lowStock)
                    {
                        LowStockReport.Add(product);
                    }

                    break;

                case "Purchases":
                    var purchases = await _purchaseService.GetPurchasesAsync(new PurchaseQueryParameters
                    {
                        PageSize = 500,
                        FromDate = FromDate.HasValue ? new DateTimeOffset(FromDate.Value) : null,
                        ToDate = ToDate.HasValue ? new DateTimeOffset(ToDate.Value.AddDays(1).AddTicks(-1)) : null,
                    });
                    PurchaseReport.Clear();
                    foreach (var purchase in purchases.Items)
                    {
                        PurchaseReport.Add(purchase);
                    }

                    break;

                case "Sales":
                    var sales = await _saleService.GetSalesAsync(new SaleQueryParameters
                    {
                        PageSize = 500,
                        FromDate = FromDate.HasValue ? new DateTimeOffset(FromDate.Value) : null,
                        ToDate = ToDate.HasValue ? new DateTimeOffset(ToDate.Value.AddDays(1).AddTicks(-1)) : null,
                    });
                    SaleReport.Clear();
                    foreach (var sale in sales.Items)
                    {
                        SaleReport.Add(sale);
                    }

                    break;

                case "Stock Movements":
                    var movements = await _inventoryService.GetStockHistoryAsync(new StockHistoryQuery { PageSize = 500 });
                    StockMovementReport.Clear();
                    foreach (var movement in movements.Items)
                    {
                        if (FromDate.HasValue && movement.OccurredAtUtc < FromDate.Value)
                        {
                            continue;
                        }

                        if (ToDate.HasValue && movement.OccurredAtUtc > ToDate.Value.AddDays(1).AddTicks(-1))
                        {
                            continue;
                        }

                        StockMovementReport.Add(movement);
                    }

                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
