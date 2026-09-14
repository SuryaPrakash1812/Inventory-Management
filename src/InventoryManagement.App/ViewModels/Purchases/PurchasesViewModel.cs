using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.Application.Products;
using InventoryManagement.Application.Purchases;
using InventoryManagement.Application.Suppliers;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.App.ViewModels.Purchases;

public sealed partial class PurchasesViewModel : ViewModelBase
{
    private readonly IPurchaseService _purchaseService;
    private readonly ISupplierService _supplierService;
    private readonly IProductService _productService;

    private const int PageSize = 25;

    // --- List view state ---

    public ObservableCollection<PurchaseSummary> Purchases { get; } = new();

    public ObservableCollection<SupplierSummary> Suppliers { get; } = new();

    public IReadOnlyList<string> StatusFilterOptions { get; } = new[] { "All", "Draft", "Confirmed", "Cancelled" };

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private SupplierSummary? _selectedSupplierFilter;

    [ObservableProperty]
    private string _selectedStatusFilter = "All";

    [ObservableProperty]
    private DateTime? _fromDateFilter;

    [ObservableProperty]
    private DateTime? _toDateFilter;

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

    /// <summary>
    /// Bound to every action button's IsEnabled (Save/Confirm/Cancel/Delete).
    /// Critically, this also covers the payment-status auto-save: without
    /// it, nothing stopped a user from clicking Save Draft WHILE that
    /// fire-and-forget save was still in flight, and two overlapping
    /// operations sharing the same DbContext at once is a real source of
    /// corruption (EF Core's DbContext is not safe for concurrent use,
    /// even within one session) - this is what actually caused the
    /// DbUpdateConcurrencyException, not stale tracking as first suspected.
    /// </summary>
    public bool IsNotBusy => !IsBusy;

    private int _loadRequestVersion;

    // --- Detail/edit view state ---

    [ObservableProperty]
    private bool _isViewingDetail;

    [ObservableProperty]
    private Guid? _editingPurchaseId;

    [ObservableProperty]
    private PurchaseStatus _currentStatus = PurchaseStatus.Draft;

    [ObservableProperty]
    private string? _purchaseNumberDisplay;

    [ObservableProperty]
    private SupplierSummary? _formSupplier;

    [ObservableProperty]
    private string _formSupplierInvoiceNumber = string.Empty;

    [ObservableProperty]
    private DateTime _formPurchaseDate = DateTime.Today;

    [ObservableProperty]
    private string _formNotes = string.Empty;

    [ObservableProperty]
    private PurchasePaymentStatus _formPaymentStatus = PurchasePaymentStatus.Unpaid;

    public IReadOnlyList<PurchasePaymentStatus> PaymentStatusOptions { get; } =
        Enum.GetValues<PurchasePaymentStatus>();

    public ObservableCollection<PurchaseLineItemEditModel> LineItems { get; } = new();

    [ObservableProperty]
    private string _productSearchText = string.Empty;

    public decimal FormSubtotal => LineItems.Sum(i => i.Quantity * i.UnitCost);

    public decimal FormDiscountTotal => LineItems.Sum(i => i.DiscountAmount);

    public decimal FormTaxTotal => LineItems.Sum(i => (i.Quantity * i.UnitCost - i.DiscountAmount) * i.TaxPercentage / 100m);

    public decimal FormTotal => FormSubtotal - FormDiscountTotal + FormTaxTotal;

    public bool IsShowingList => !IsViewingDetail;

    public bool CanEditFields => EditingPurchaseId is null || CurrentStatus == PurchaseStatus.Draft;

    public bool CanConfirm => EditingPurchaseId is not null && CurrentStatus == PurchaseStatus.Draft;

    public bool CanCancel => EditingPurchaseId is not null && CurrentStatus != PurchaseStatus.Cancelled;

    public bool CanDelete => EditingPurchaseId is not null && CurrentStatus == PurchaseStatus.Draft;

    public bool IsNewPurchase => EditingPurchaseId is null;

    public bool IsReadOnlyLineItems => !CanEditFields;

    public PurchasesViewModel(
        IPurchaseService purchaseService, ISupplierService supplierService, IProductService productService)
    {
        _purchaseService = purchaseService;
        _supplierService = supplierService;
        _productService = productService;
    }

    public async Task InitializeAsync()
    {
        await RefreshSuppliersAsync();
        await LoadAsync();
    }

    private async Task RefreshSuppliersAsync()
    {
        var suppliers = await _supplierService.GetSuppliersAsync(new SupplierQueryParameters { PageSize = 500 });

        Suppliers.Clear();
        foreach (var supplier in suppliers.Items)
        {
            Suppliers.Add(supplier);
        }
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
        SelectedSupplierFilter = null;
        SelectedStatusFilter = "All";
        FromDateFilter = null;
        ToDateFilter = null;
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
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            PurchaseStatus? status = SelectedStatusFilter switch
            {
                "Draft" => PurchaseStatus.Draft,
                "Confirmed" => PurchaseStatus.Confirmed,
                "Cancelled" => PurchaseStatus.Cancelled,
                _ => null,
            };

            var result = await _purchaseService.GetPurchasesAsync(new PurchaseQueryParameters
            {
                SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm,
                SupplierId = SelectedSupplierFilter?.Id,
                Status = status,
                FromDate = FromDateFilter.HasValue ? new DateTimeOffset(FromDateFilter.Value) : null,
                ToDate = ToDateFilter.HasValue ? new DateTimeOffset(ToDateFilter.Value.AddDays(1).AddTicks(-1)) : null,
                PageNumber = PageNumber,
                PageSize = PageSize,
            });

            if (requestVersion != _loadRequestVersion)
            {
                return;
            }

            Purchases.Clear();
            foreach (var purchase in result.Items)
            {
                Purchases.Add(purchase);
            }

            TotalCount = result.TotalCount;
            TotalPages = Math.Max(1, result.TotalPages);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    [RelayCommand]
    private void StartCreate()
    {
        ErrorMessage = null;
        StatusMessage = null;
        EditingPurchaseId = null;
        CurrentStatus = PurchaseStatus.Draft;
        PurchaseNumberDisplay = "(assigned on save)";
        FormSupplier = Suppliers.FirstOrDefault();
        FormSupplierInvoiceNumber = string.Empty;
        FormPurchaseDate = DateTime.Today;
        FormNotes = string.Empty;
        FormPaymentStatus = PurchasePaymentStatus.Unpaid;
        ProductSearchText = string.Empty;
        ClearLineItems();
        IsViewingDetail = true;

        RaiseModeChanged();
    }

    [RelayCommand]
    private async Task StartViewAsync(PurchaseSummary purchase)
    {
        ErrorMessage = null;
        StatusMessage = null;

        var detail = await _purchaseService.GetPurchaseByIdAsync(purchase.Id);
        if (detail is null)
        {
            ErrorMessage = "Purchase not found - it may have just been removed.";
            return;
        }

        // EditingPurchaseId is deliberately set LAST, after every other field
        // (including FormPaymentStatus). OnFormPaymentStatusChanged only
        // acts when EditingPurchaseId is already set, specifically so that
        // populating these fields while loading a record never fires a
        // spurious "save payment status" call - only a genuine, later,
        // user-driven change to the dropdown should do that.
        EditingPurchaseId = null;
        CurrentStatus = detail.Status;
        PurchaseNumberDisplay = detail.PurchaseNumber;
        FormSupplier = Suppliers.FirstOrDefault(s => s.Id == detail.SupplierId);
        FormSupplierInvoiceNumber = detail.SupplierInvoiceNumber ?? string.Empty;
        FormPurchaseDate = detail.PurchaseDate.Date;
        FormNotes = detail.Notes ?? string.Empty;
        FormPaymentStatus = detail.PaymentStatus;
        ProductSearchText = string.Empty;

        ClearLineItems();
        foreach (var item in detail.Items)
        {
            AddLineItemToCollection(new PurchaseLineItemEditModel(
                item.ProductId, item.ProductSku, item.ProductName, item.Quantity, item.UnitCost,
                item.DiscountAmount, item.TaxPercentage));
        }

        EditingPurchaseId = detail.Id;
        IsViewingDetail = true;

        RaiseModeChanged();
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsViewingDetail = false;
        ErrorMessage = null;
        OnPropertyChanged(nameof(IsShowingList));
    }

    /// <summary>
    /// Fast item entry: type a SKU, barcode, or name and press Enter. An
    /// exact SKU/barcode match wins; otherwise the first search result is
    /// used. Scanning a barcode already on the purchase just increments its
    /// quantity, matching how a physical barcode scanner is normally used
    /// at a receiving desk (repeated scans = more units, not more lines).
    /// </summary>
    [RelayCommand]
    private async Task AddLineItemAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(ProductSearchText))
        {
            return;
        }

        var searchText = ProductSearchText.Trim();

        var results = await _productService.GetProductsAsync(new ProductQueryParameters
        {
            SearchTerm = searchText, PageSize = 5, IsActive = true,
        });

        if (results.Items.Count == 0)
        {
            ErrorMessage = $"No product found matching '{searchText}'.";
            return;
        }

        var exactMatch = results.Items.FirstOrDefault(p =>
            string.Equals(p.Sku, searchText, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Barcode, searchText, StringComparison.OrdinalIgnoreCase));
        var product = exactMatch ?? results.Items[0];

        var existingLine = LineItems.FirstOrDefault(i => i.ProductId == product.Id);
        if (existingLine is not null)
        {
            existingLine.Quantity += 1;
        }
        else
        {
            AddLineItemToCollection(new PurchaseLineItemEditModel(
                product.Id, product.Sku, product.Name, quantity: 1, unitCost: product.PurchasePrice,
                discountAmount: 0, taxPercentage: product.TaxPercentage));
        }

        ProductSearchText = string.Empty;
        RaiseTotalsChanged();
    }

    [RelayCommand]
    private void RemoveLineItem(PurchaseLineItemEditModel item)
    {
        item.PropertyChanged -= OnLineItemPropertyChanged;
        LineItems.Remove(item);
        RaiseTotalsChanged();
    }

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        // Guards against Ctrl+S (or any other invocation path) firing while
        // another operation - notably the fire-and-forget payment-status
        // auto-save - is still running. IsEnabled on the button covers
        // mouse clicks, but a KeyBinding invokes the command directly and
        // would bypass that, so the guard belongs here in the method body
        // too, not just in the UI.
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;

        if (FormSupplier is null)
        {
            ErrorMessage = "A supplier must be selected.";
            return;
        }

        if (LineItems.Count == 0)
        {
            ErrorMessage = "Add at least one item before saving.";
            return;
        }

        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            var itemRequests = LineItems
                .Select(i => new PurchaseItemRequest(i.ProductId, i.Quantity, i.UnitCost, i.DiscountAmount, i.TaxPercentage))
                .ToList();

            var request = new SaveDraftPurchaseRequest(
                EditingPurchaseId,
                FormSupplier.Id,
                string.IsNullOrWhiteSpace(FormSupplierInvoiceNumber) ? null : FormSupplierInvoiceNumber,
                new DateTimeOffset(FormPurchaseDate),
                string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes,
                itemRequests);

            var result = await _purchaseService.SaveDraftAsync(request);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            StatusMessage = $"Purchase {result.Value.PurchaseNumber} saved.";
            IsViewingDetail = false;
            OnPropertyChanged(nameof(IsShowingList));
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (EditingPurchaseId is not { } id || IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            var result = await _purchaseService.ConfirmPurchaseAsync(id);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            StatusMessage = $"Purchase {result.Value.PurchaseNumber} confirmed - stock updated.";
            IsViewingDetail = false;
            OnPropertyChanged(nameof(IsShowingList));
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    [RelayCommand]
    private async Task CancelPurchaseAsync()
    {
        if (EditingPurchaseId is not { } id || IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            var result = await _purchaseService.CancelPurchaseAsync(id);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            StatusMessage = $"Purchase {result.Value.PurchaseNumber} cancelled.";
            IsViewingDetail = false;
            OnPropertyChanged(nameof(IsShowingList));
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    [RelayCommand]
    private async Task DeleteDraftAsync()
    {
        if (EditingPurchaseId is not { } id || IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            var result = await _purchaseService.DeleteDraftAsync(id);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            StatusMessage = "Draft purchase deleted.";
            IsViewingDetail = false;
            OnPropertyChanged(nameof(IsShowingList));
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    partial void OnFormPaymentStatusChanged(PurchasePaymentStatus value)
    {
        if (EditingPurchaseId is not { } id)
        {
            return;
        }

        _ = PersistPaymentStatusAsync(id, value);
    }

    private async Task PersistPaymentStatusAsync(Guid id, PurchasePaymentStatus value)
    {
        if (IsBusy)
        {
            ErrorMessage = "Another action is still in progress - please try changing the payment status again in a moment.";
            return;
        }

        // Still fire-and-forget from OnFormPaymentStatusChanged's point of
        // view (that hook can't be async), but IsBusy now makes every other
        // action button unavailable for the duration, so nothing can start
        // a second operation against the shared DbContext while this one is
        // still running - see IsNotBusy remarks for why that matters.
        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            var result = await _purchaseService.SetPaymentStatusAsync(id, value);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
            }
            else
            {
                StatusMessage = $"Payment status set to {value}.";
            }
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    private void ClearLineItems()
    {
        foreach (var item in LineItems)
        {
            item.PropertyChanged -= OnLineItemPropertyChanged;
        }

        LineItems.Clear();
    }

    private void AddLineItemToCollection(PurchaseLineItemEditModel item)
    {
        item.PropertyChanged += OnLineItemPropertyChanged;
        LineItems.Add(item);
    }

    private void OnLineItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        RaiseTotalsChanged();

    private void RaiseModeChanged()
    {
        OnPropertyChanged(nameof(IsShowingList));
        OnPropertyChanged(nameof(CanEditFields));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(IsNewPurchase));
        OnPropertyChanged(nameof(IsReadOnlyLineItems));
        RaiseTotalsChanged();
    }

    private void RaiseTotalsChanged()
    {
        OnPropertyChanged(nameof(FormSubtotal));
        OnPropertyChanged(nameof(FormDiscountTotal));
        OnPropertyChanged(nameof(FormTaxTotal));
        OnPropertyChanged(nameof(FormTotal));
    }
}
