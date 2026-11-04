using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.Application.Common;
using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.Products;
using InventoryManagement.Application.Purchases;
using InventoryManagement.Application.Sales;
using InventoryManagement.Application.Settings;
using Microsoft.Win32;

namespace InventoryManagement.App.ViewModels.Reports;

/// <summary>
/// Every report reuses an existing Application-layer query method - no new
/// backend code. A report-type selector switches which single collection
/// is populated and shown; only one report's data is held at a time.
///
/// Reports always load ALL matching rows (PageSize = int.MaxValue, as the
/// Products export already does) - a report that silently stops at an
/// arbitrary row count gives wrong totals without saying so.
/// </summary>
public sealed partial class ReportsViewModel : ViewModelBase
{
    private const int AllRows = int.MaxValue;

    private readonly IProductService _productService;
    private readonly IInventoryService _inventoryService;
    private readonly IPurchaseService _purchaseService;
    private readonly ISaleService _saleService;
    private readonly ISettingsService _settingsService;

    public IReadOnlyList<string> ReportTypes { get; } =
        new[] { "Inventory", "Low Stock", "Purchases", "Sales", "Stock Movements" };

    [ObservableProperty]
    private string _selectedReportType = "Inventory";

    [ObservableProperty]
    private DateTime? _fromDate;

    [ObservableProperty]
    private DateTime? _toDate;

    [ObservableProperty]
    private string? _summaryText;

    [ObservableProperty]
    private string? _statusMessage;

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
        IProductService productService,
        IInventoryService inventoryService,
        IPurchaseService purchaseService,
        ISaleService saleService,
        ISettingsService settingsService)
    {
        _productService = productService;
        _inventoryService = inventoryService;
        _purchaseService = purchaseService;
        _saleService = saleService;
        _settingsService = settingsService;
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

    private DateTimeOffset? FromBound => FromDate.HasValue ? new DateTimeOffset(FromDate.Value.Date) : null;

    /// <summary>End of the selected "To" day, so the whole day is included.</summary>
    private DateTimeOffset? ToBound => ToDate.HasValue ? new DateTimeOffset(ToDate.Value.Date.AddDays(1).AddTicks(-1)) : null;

    [RelayCommand]
    private async Task RunReportAsync()
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            switch (SelectedReportType)
            {
                case "Inventory":
                    var products = await _productService.GetProductsAsync(new ProductQueryParameters { PageSize = AllRows, IsActive = true });
                    Replace(InventoryReport, products.Items);
                    SummaryText = $"{InventoryReport.Count} products - stock value at cost {InventoryReport.Sum(p => p.CurrentStock * p.PurchasePrice):N2}, "
                        + $"at selling price {InventoryReport.Sum(p => p.CurrentStock * p.SellingPrice):N2}";
                    break;

                case "Low Stock":
                    Replace(LowStockReport, await _inventoryService.GetLowStockProductsAsync());
                    SummaryText = $"{LowStockReport.Count} products at or below their minimum stock";
                    break;

                case "Purchases":
                    var purchases = await _purchaseService.GetPurchasesAsync(new PurchaseQueryParameters
                    {
                        PageSize = AllRows,
                        FromDate = FromBound,
                        ToDate = ToBound,
                    });
                    Replace(PurchaseReport, purchases.Items);
                    SummaryText = $"{PurchaseReport.Count} purchases - total {PurchaseReport.Sum(p => p.TotalAmount):N2}";
                    break;

                case "Sales":
                    var sales = await _saleService.GetSalesAsync(new SaleQueryParameters
                    {
                        PageSize = AllRows,
                        FromDate = FromBound,
                        ToDate = ToBound,
                    });
                    Replace(SaleReport, sales.Items);
                    SummaryText = $"{SaleReport.Count} sales - total {SaleReport.Sum(s => s.TotalAmount):N2}";
                    break;

                case "Stock Movements":
                    var movements = await _inventoryService.GetStockHistoryAsync(new StockHistoryQuery
                    {
                        PageSize = AllRows,
                        FromDate = FromBound,
                        ToDate = ToBound,
                    });
                    Replace(StockMovementReport, movements.Items);
                    SummaryText = $"{StockMovementReport.Count} stock movements";
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = $"Export {SelectedReportType} Report",
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"{SelectedReportType.Replace(" ", string.Empty)}Report-{DateTime.Now:yyyyMMdd}.csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            // UTF-8 with BOM so Excel detects the encoding (names with
            // non-ASCII characters would otherwise be garbled).
            await File.WriteAllTextAsync(dialog.FileName, BuildCsv(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusMessage = $"Exported to {dialog.FileName}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    private string BuildCsv()
    {
        var csv = new CsvWriter();
        var company = _settingsService.Current.Company;

        if (!string.IsNullOrWhiteSpace(company.Name))
        {
            csv.AddRow(company.Name);
            if (!string.IsNullOrWhiteSpace(company.Address)) csv.AddRow(company.Address);
            if (!string.IsNullOrWhiteSpace(company.TaxId)) csv.AddRow("Tax ID", company.TaxId);
        }

        csv.AddRow($"{SelectedReportType} Report", $"Generated {DateTime.Now:yyyy-MM-dd HH:mm}");
        if (ShowsDateRangeFilter && (FromDate.HasValue || ToDate.HasValue))
        {
            csv.AddRow("Period", FromDate, ToDate);
        }

        csv.AddRow(SummaryText);
        csv.AddBlankRow();

        switch (SelectedReportType)
        {
            case "Inventory":
                csv.AddRow("SKU", "Name", "Category", "Unit", "Current Stock", "Cost Price", "Selling Price", "Stock Value (Cost)");
                foreach (var p in InventoryReport)
                {
                    csv.AddRow(p.Sku, p.Name, p.CategoryName, p.Unit, p.CurrentStock, p.PurchasePrice, p.SellingPrice, p.CurrentStock * p.PurchasePrice);
                }

                break;

            case "Low Stock":
                csv.AddRow("SKU", "Name", "Current Stock", "Minimum Stock");
                foreach (var p in LowStockReport)
                {
                    csv.AddRow(p.Sku, p.Name, p.CurrentStock, p.MinimumStock);
                }

                break;

            case "Purchases":
                csv.AddRow("Purchase #", "Supplier Invoice #", "Supplier", "Date", "Status", "Payment Status", "Total");
                foreach (var p in PurchaseReport)
                {
                    csv.AddRow(p.PurchaseNumber, p.SupplierInvoiceNumber, p.SupplierName, p.PurchaseDate, p.Status, p.PaymentStatus, p.TotalAmount);
                }

                break;

            case "Sales":
                csv.AddRow("Sale #", "Invoice #", "Customer", "Date", "Status", "Total");
                foreach (var s in SaleReport)
                {
                    csv.AddRow(s.SaleNumber, s.InvoiceNumber, s.CustomerName, s.SaleDate, s.Status, s.TotalAmount);
                }

                break;

            case "Stock Movements":
                csv.AddRow("Date", "SKU", "Product", "Movement Type", "Quantity", "Balance After", "Reference", "Notes");
                foreach (var m in StockMovementReport)
                {
                    csv.AddRow(m.OccurredAtUtc, m.ProductSku, m.ProductName, m.MovementType, m.QuantityChange, m.QuantityBalanceAfter, m.ReferenceType, m.Notes);
                }

                break;
        }

        return csv.ToString();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
