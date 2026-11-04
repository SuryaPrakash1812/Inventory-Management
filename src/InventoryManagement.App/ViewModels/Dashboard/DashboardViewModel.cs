using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using InventoryManagement.App.Controls;
using InventoryManagement.App.Messages;
using InventoryManagement.App.Views.Pages;
using InventoryManagement.Application.Auth;
using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.Products;
using InventoryManagement.Application.Purchases;
using InventoryManagement.Application.Sales;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.App.ViewModels.Dashboard;

public sealed record DateRangeOption(string Label, int Days);

/// <summary>
/// Composed entirely from existing Application-layer queries. Product and
/// purchase lists are loaded once per refresh (PageSize = all) and every
/// card, the trend chart and the stock breakdown are computed from those
/// in memory - one consistent snapshot, so the numbers on different cards
/// can never disagree with each other. A desktop install's data volume
/// makes this cheap.
///
/// Stock status uses the same rule as the Inventory low-stock list:
/// out of stock = nothing on hand; low stock = on hand but at or below the
/// product's minimum; otherwise in stock.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private const int AllRows = int.MaxValue;

    private static readonly Brush InStockBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)));
    private static readonly Brush LowStockBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFA, 0xC0, 0x15)));
    private static readonly Brush OutOfStockBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)));
    private static readonly Brush[] CategoryBrushes =
    {
        Freeze(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB))),
        Freeze(new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E))),
        Freeze(new SolidColorBrush(Color.FromRgb(0xFA, 0xC0, 0x15))),
        Freeze(new SolidColorBrush(Color.FromRgb(0xA8, 0x55, 0xF7))),
        Freeze(new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16))),
        Freeze(new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8))),
    };

    private readonly IProductService _productService;
    private readonly IInventoryService _inventoryService;
    private readonly IPurchaseService _purchaseService;
    private readonly ISaleService _saleService;

    private IReadOnlyList<ProductSummary> _products = Array.Empty<ProductSummary>();
    private IReadOnlyList<PurchaseSummary> _purchases = Array.Empty<PurchaseSummary>();

    public IReadOnlyList<DateRangeOption> DateRanges { get; } = new[]
    {
        new DateRangeOption("Last 7 days", 7),
        new DateRangeOption("Last 30 days", 30),
        new DateRangeOption("Last 90 days", 90),
    };

    public IReadOnlyList<string> InsightModes { get; } = new[] { "By Stock Status", "By Category" };

    public string WelcomeText { get; }

    [ObservableProperty] private DateRangeOption _selectedRange;
    [ObservableProperty] private string _selectedInsightMode = "By Stock Status";
    [ObservableProperty] private string _rangeText = string.Empty;

    [ObservableProperty] private int _activeProductCount;
    [ObservableProperty] private int _totalProductCount;
    [ObservableProperty] private decimal _inventoryValueAtCost;
    [ObservableProperty] private int _lowStockCount;
    [ObservableProperty] private int _outOfStockCount;
    [ObservableProperty] private int _openPurchaseCount;
    [ObservableProperty] private int _draftPurchaseCount;
    [ObservableProperty] private int _awaitingPaymentCount;

    [ObservableProperty] private IReadOnlyList<ChartPoint> _purchaseTrend = Array.Empty<ChartPoint>();
    [ObservableProperty] private decimal _purchaseTrendTotal;
    [ObservableProperty] private IReadOnlyList<ChartSegment> _insightSegments = Array.Empty<ChartSegment>();
    [ObservableProperty] private int _insightTotal;
    [ObservableProperty] private string _insightCenterLabel = "Products";

    public int NeedsAttentionCount => LowStockCount + OutOfStockCount;

    public ObservableCollection<PurchaseSummary> RecentPurchases { get; } = new();

    public ObservableCollection<SaleSummary> RecentSales { get; } = new();

    public ObservableCollection<StockHistoryEntry> RecentStockMovements { get; } = new();

    public bool HasRecentSales => RecentSales.Count > 0;

    public bool HasRecentPurchases => RecentPurchases.Count > 0;

    public bool HasRecentMovements => RecentStockMovements.Count > 0;

    public DashboardViewModel(
        IProductService productService,
        IInventoryService inventoryService,
        IPurchaseService purchaseService,
        ISaleService saleService,
        ICurrentUserSession session)
    {
        _productService = productService;
        _inventoryService = inventoryService;
        _purchaseService = purchaseService;
        _saleService = saleService;

        var firstName = session.CurrentUser?.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        WelcomeText = string.IsNullOrEmpty(firstName)
            ? "Here's what's happening with your inventory today."
            : $"Welcome back, {firstName}! Here's what's happening with your inventory today.";

        _selectedRange = DateRanges[0];
        UpdateRangeText();
    }

    public async Task InitializeAsync() => await RefreshAsync();

    partial void OnSelectedRangeChanged(DateRangeOption value)
    {
        UpdateRangeText();
        BuildPurchaseTrend();
    }

    partial void OnSelectedInsightModeChanged(string value) => BuildInsights();

    partial void OnLowStockCountChanged(int value) => OnPropertyChanged(nameof(NeedsAttentionCount));

    partial void OnOutOfStockCountChanged(int value) => OnPropertyChanged(nameof(NeedsAttentionCount));

    [RelayCommand]
    private void SelectRange(DateRangeOption option) => SelectedRange = option;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var products = await _productService.GetProductsAsync(new ProductQueryParameters { PageSize = AllRows });
            _products = products.Items;
            TotalProductCount = products.TotalCount;

            var active = _products.Where(p => p.IsActive).ToList();
            ActiveProductCount = active.Count;
            OutOfStockCount = active.Count(p => p.CurrentStock <= 0);
            LowStockCount = active.Count(p => p.CurrentStock > 0 && p.CurrentStock <= p.MinimumStock);

            InventoryValueAtCost = (await _inventoryService.GetStockValuationAsync()).TotalCostValue;

            var purchases = await _purchaseService.GetPurchasesAsync(new PurchaseQueryParameters { PageSize = AllRows });
            _purchases = purchases.Items;
            DraftPurchaseCount = _purchases.Count(p => p.Status == PurchaseStatus.Draft);
            AwaitingPaymentCount = _purchases.Count(p =>
                p.Status == PurchaseStatus.Confirmed && p.PaymentStatus != PurchasePaymentStatus.Paid);
            OpenPurchaseCount = DraftPurchaseCount + AwaitingPaymentCount;

            Replace(RecentPurchases, _purchases.Take(5));
            Replace(RecentSales, (await _saleService.GetSalesAsync(new SaleQueryParameters { PageSize = 5 })).Items);
            Replace(RecentStockMovements, (await _inventoryService.GetStockHistoryAsync(new StockHistoryQuery { PageSize = 5 })).Items);

            OnPropertyChanged(nameof(HasRecentSales));
            OnPropertyChanged(nameof(HasRecentPurchases));
            OnPropertyChanged(nameof(HasRecentMovements));

            BuildPurchaseTrend();
            BuildInsights();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Navigate(string target)
    {
        var (pageType, createNew) = target switch
        {
            "Products" => (typeof(ProductsPage), false),
            "Inventory" => (typeof(InventoryPage), false),
            "Purchases" => (typeof(PurchasesPage), false),
            "Sales" => (typeof(SalesPage), false),
            "CreateSale" => (typeof(SalesPage), true),
            "Reports" => (typeof(ReportsPage), false),
            _ => (typeof(DashboardPage), false),
        };

        WeakReferenceMessenger.Default.Send(new NavigateToPageMessage(pageType, CreateNew: createNew));
    }

    private void UpdateRangeText()
    {
        var (start, end) = CurrentRange();
        RangeText = $"{start:MMM d, yyyy} – {end:MMM d, yyyy}";
    }

    private (DateTime Start, DateTime End) CurrentRange()
    {
        var end = DateTime.Today;
        return (end.AddDays(1 - SelectedRange.Days), end);
    }

    /// <summary>Total value of non-cancelled purchases per day of the selected range (days with none plot as zero).</summary>
    private void BuildPurchaseTrend()
    {
        var (start, end) = CurrentRange();

        var byDay = _purchases
            .Where(p => p.Status != PurchaseStatus.Cancelled)
            .GroupBy(p => p.PurchaseDate.ToLocalTime().Date)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.TotalAmount));

        var points = new List<ChartPoint>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            var value = byDay.GetValueOrDefault(day);
            points.Add(new ChartPoint(
                day.ToString("MMM d", CultureInfo.CurrentCulture),
                value,
                $"{day:ddd, MMM d}: {value:N2}"));
        }

        PurchaseTrend = points;
        PurchaseTrendTotal = points.Sum(p => p.Value);
    }

    private void BuildInsights()
    {
        var active = _products.Where(p => p.IsActive).ToList();
        InsightTotal = active.Count;
        InsightCenterLabel = active.Count == 1 ? "Product" : "Products";

        List<(string Label, int Count, Brush Brush)> groups;
        if (SelectedInsightMode == "By Category")
        {
            var byCategory = active
                .GroupBy(p => string.IsNullOrWhiteSpace(p.CategoryName) ? "Uncategorized" : p.CategoryName)
                .Select(g => (Label: g.Key, Count: g.Count()))
                .OrderByDescending(g => g.Count)
                .ToList();

            // Five largest categories, everything else folded into "Other" so the legend stays readable.
            var top = byCategory.Take(5).ToList();
            var otherCount = byCategory.Skip(5).Sum(g => g.Count);
            if (otherCount > 0)
            {
                top.Add(("Other", otherCount));
            }

            groups = top.Select((g, i) => (g.Label, g.Count, CategoryBrushes[i % CategoryBrushes.Length])).ToList();
        }
        else
        {
            groups = new List<(string, int, Brush)>
            {
                ("In Stock", active.Count(p => p.CurrentStock > 0 && p.CurrentStock > p.MinimumStock), InStockBrush),
                ("Low Stock", active.Count(p => p.CurrentStock > 0 && p.CurrentStock <= p.MinimumStock), LowStockBrush),
                ("Out of Stock", active.Count(p => p.CurrentStock <= 0), OutOfStockBrush),
            };
        }

        InsightSegments = groups
            .Select(g => new ChartSegment(g.Label, g.Count, g.Brush, active.Count == 0 ? 0 : 100.0 * g.Count / active.Count))
            .ToList();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
