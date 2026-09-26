using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.Products;
using InventoryManagement.Application.Purchases;
using InventoryManagement.Application.Sales;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.App.ViewModels.Dashboard;

/// <summary>
/// Entirely composed from existing Application-layer methods - no new
/// backend code was written for this screen. Every metric below is either
/// a direct call to an existing service method, or a TotalCount read from
/// an existing paginated query with PageSize=1 (a cheap way to get an
/// exact count without a dedicated aggregate method that doesn't exist
/// yet). "Recent" lists (purchases/sales/stock movements) are simply the
/// first page of each existing query, which already sorts newest-first by
/// default.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly IProductService _productService;
    private readonly IInventoryService _inventoryService;
    private readonly IPurchaseService _purchaseService;
    private readonly ISaleService _saleService;

    [ObservableProperty]
    private int _totalProductCount;

    [ObservableProperty]
    private int _activeProductCount;

    [ObservableProperty]
    private int _lowStockCount;

    [ObservableProperty]
    private int _outOfStockCount;

    [ObservableProperty]
    private decimal _inventoryValueAtCost;

    [ObservableProperty]
    private int _draftPurchaseCount;

    [ObservableProperty]
    private int _confirmedPurchaseCount;

    [ObservableProperty]
    private int _draftSaleCount;

    [ObservableProperty]
    private int _invoicedSaleCount;

    public ObservableCollection<PurchaseSummary> RecentPurchases { get; } = new();

    public ObservableCollection<SaleSummary> RecentSales { get; } = new();

    public ObservableCollection<StockHistoryEntry> RecentStockMovements { get; } = new();

    public DashboardViewModel(
        IProductService productService, IInventoryService inventoryService, IPurchaseService purchaseService, ISaleService saleService)
    {
        _productService = productService;
        _inventoryService = inventoryService;
        _purchaseService = purchaseService;
        _saleService = saleService;
    }

    public async Task InitializeAsync() => await RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var allProducts = await _productService.GetProductsAsync(new ProductQueryParameters { PageSize = 1 });
            TotalProductCount = allProducts.TotalCount;

            var activeProducts = await _productService.GetProductsAsync(new ProductQueryParameters { PageSize = 1, IsActive = true });
            ActiveProductCount = activeProducts.TotalCount;

            var lowStock = await _inventoryService.GetLowStockProductsAsync();
            LowStockCount = lowStock.Count(p => p.CurrentStock > 0);
            OutOfStockCount = lowStock.Count(p => p.CurrentStock <= 0);

            var valuation = await _inventoryService.GetStockValuationAsync();
            InventoryValueAtCost = valuation.TotalCostValue;

            var draftPurchases = await _purchaseService.GetPurchasesAsync(
                new PurchaseQueryParameters { PageSize = 1, Status = PurchaseStatus.Draft });
            DraftPurchaseCount = draftPurchases.TotalCount;

            var confirmedPurchases = await _purchaseService.GetPurchasesAsync(
                new PurchaseQueryParameters { PageSize = 1, Status = PurchaseStatus.Confirmed });
            ConfirmedPurchaseCount = confirmedPurchases.TotalCount;

            var draftSales = await _saleService.GetSalesAsync(new SaleQueryParameters { PageSize = 1, Status = SaleStatus.Draft });
            DraftSaleCount = draftSales.TotalCount;

            var invoicedSales = await _saleService.GetSalesAsync(new SaleQueryParameters { PageSize = 1, Status = SaleStatus.Invoiced });
            InvoicedSaleCount = invoicedSales.TotalCount;

            var recentPurchases = await _purchaseService.GetPurchasesAsync(new PurchaseQueryParameters { PageSize = 5 });
            RecentPurchases.Clear();
            foreach (var purchase in recentPurchases.Items)
            {
                RecentPurchases.Add(purchase);
            }

            var recentSales = await _saleService.GetSalesAsync(new SaleQueryParameters { PageSize = 5 });
            RecentSales.Clear();
            foreach (var sale in recentSales.Items)
            {
                RecentSales.Add(sale);
            }

            var recentMovements = await _inventoryService.GetStockHistoryAsync(new StockHistoryQuery { PageSize = 5 });
            RecentStockMovements.Clear();
            foreach (var movement in recentMovements.Items)
            {
                RecentStockMovements.Add(movement);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
