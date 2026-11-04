using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using InventoryManagement.App.Messages;
using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.Products;
using InventoryManagement.Application.Purchases;
using InventoryManagement.Application.Suppliers;
using InventoryManagement.App.Services;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.App.ViewModels.Purchases;

public sealed partial class PurchasesViewModel : ViewModelBase
{
    /// <summary>The "All" entry at the top of the supplier filter (Id = Guid.Empty means no filter).</summary>
    private static readonly SupplierSummary AllSuppliers = new(Guid.Empty, "All", null, null, null, null, true);

    private readonly IPurchaseService _purchaseService;
    private readonly ISupplierService _supplierService;
    private readonly IProductService _productService;
    private bool _isInitialized;

    [ObservableProperty]
    private int _pageSize = 10;

    // --- Summary cards (all purchases, regardless of filters) ---

    [ObservableProperty] private int _allPurchaseCount;
    [ObservableProperty] private int _draftPurchaseCount;
    [ObservableProperty] private int _awaitingPaymentCount;
    [ObservableProperty] private decimal _awaitingPaymentAmount;
    [ObservableProperty] private decimal _confirmedPurchaseValue;

    // --- List view state ---

    public ObservableCollection<PurchaseSummary> Purchases { get; } = new();

    public ObservableCollection<SupplierSummary> Suppliers { get; } = new();

    /// <summary><see cref="Suppliers"/> with "All" first - for the list filter, not the edit form.</summary>
    public ObservableCollection<SupplierSummary> SupplierFilterOptions { get; } = new();

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
    /// Bound to every action button's IsEnabled (Save/Confirm/Cancel/Delete/
    /// Save Payment Status), so none of them can run while another is still
    /// in flight. Two overlapping writes sharing the same DbContext at once
    /// is a real source of corruption - EF Core's DbContext is not safe for
    /// concurrent use, even within one session - and was previously the
    /// actual cause of a DbUpdateConcurrencyException here (payment status
    /// used to auto-save via a fire-and-forget call, which a user could
    /// easily race against by clicking Save Draft right after; that
    /// auto-save is gone now in favor of SavePaymentStatusAsync being an
    /// explicit action, but this guard remains as defense against any other
    /// two actions overlapping).
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

    /// <summary>A brand-new purchase can be confirmed directly - it is saved first.</summary>
    public bool CanConfirm => CurrentStatus == PurchaseStatus.Draft;

    public bool CanCancel => EditingPurchaseId is not null && CurrentStatus != PurchaseStatus.Cancelled;

    public bool CanDelete => EditingPurchaseId is not null && CurrentStatus == PurchaseStatus.Draft;

    public bool IsNewPurchase => EditingPurchaseId is null;

    public bool IsReadOnlyLineItems => !CanEditFields;

    public bool CanChangePayment => EditingPurchaseId is not null && CurrentStatus == PurchaseStatus.Confirmed;

    public bool IsCancelled => CurrentStatus == PurchaseStatus.Cancelled;

    public bool IsConfirmed => CurrentStatus == PurchaseStatus.Confirmed;

    /// <summary>One sentence saying what state this purchase is in and what can happen next.</summary>
    public string StatusDescription => CurrentStatus switch
    {
        PurchaseStatus.Confirmed => "Confirmed - these items are in stock. Only the payment status can change now. Cancel the purchase to reverse the stock.",
        PurchaseStatus.Cancelled => _savedPaymentStatus == PurchasePaymentStatus.Unpaid
            ? "Cancelled - this purchase is closed and any stock it added was reversed."
            : $"Cancelled - this purchase is closed and any stock it added was reversed. It was marked {_savedPaymentStatus}, so recover that money from the supplier.",
        _ => "Draft - stock has not changed yet. Confirm the purchase when the items are received.",
    };

    public string ItemsSubtitle => CanEditFields
        ? "Scan or type a SKU, barcode or name, then press Enter to add it"
        : "The items on this purchase";

    /// <summary>Active suppliers, plus this purchase's own supplier if it has since been deactivated.</summary>
    public ObservableCollection<SupplierSummary> FormSupplierOptions { get; } = new();

    public ConfirmationDialogViewModel Dialog { get; } = new();

    private string _savedFormSnapshot = string.Empty;
    private PurchasePaymentStatus _savedPaymentStatus = PurchasePaymentStatus.Unpaid;

    /// <summary>Compared with the saved state to tell whether leaving would lose edits.</summary>
    private string BuildFormSnapshot() => string.Join("|", new object?[]
    {
        FormSupplier?.Id, FormSupplierInvoiceNumber, FormPurchaseDate.Date, FormNotes,
    }.Concat(LineItems.Select(i => (object)$"{i.ProductId}:{i.Quantity}:{i.UnitCost}:{i.DiscountAmount}:{i.TaxPercentage}")));

    private bool HasUnsavedChanges =>
        (CanEditFields && BuildFormSnapshot() != _savedFormSnapshot)
        || (CanChangePayment && FormPaymentStatus != _savedPaymentStatus);

    private void RememberSavedState()
    {
        _savedFormSnapshot = BuildFormSnapshot();
        _savedPaymentStatus = FormPaymentStatus;
    }

    public PurchasesViewModel(
        IPurchaseService purchaseService, ISupplierService supplierService, IProductService productService, NavigationIntent navigationIntent)
    {
        _purchaseService = purchaseService;
        _supplierService = supplierService;
        _productService = productService;
        _navigationIntent = navigationIntent;
    }

    private readonly NavigationIntent _navigationIntent;

    public async Task InitializeAsync()
    {
        // Arriving from the global search: show that purchase.
        if (_navigationIntent.TryTake(typeof(Views.Pages.PurchasesPage), out var search, out _) && search is not null)
        {
            SearchTerm = search;
        }

        await RefreshSuppliersAsync();
        await LoadAsync();
        _isInitialized = true;
    }

    private async Task RefreshSuppliersAsync()
    {
        var suppliers = await _supplierService.GetSuppliersAsync(new SupplierQueryParameters { PageSize = 500 });
        var selectedId = SelectedSupplierFilter?.Id ?? Guid.Empty;

        Suppliers.Clear();
        SupplierFilterOptions.Clear();
        SupplierFilterOptions.Add(AllSuppliers);
        foreach (var supplier in suppliers.Items)
        {
            Suppliers.Add(supplier);
            SupplierFilterOptions.Add(supplier);
        }

        SelectedSupplierFilter = SupplierFilterOptions.FirstOrDefault(s => s.Id == selectedId) ?? AllSuppliers;
    }

    private async Task RefreshStatsAsync()
    {
        var all = (await _purchaseService.GetPurchasesAsync(new PurchaseQueryParameters { PageSize = int.MaxValue })).Items;
        AllPurchaseCount = all.Count;
        DraftPurchaseCount = all.Count(p => p.Status == PurchaseStatus.Draft);
        var unpaid = all.Where(p => p.Status == PurchaseStatus.Confirmed && p.PaymentStatus != PurchasePaymentStatus.Paid).ToList();
        AwaitingPaymentCount = unpaid.Count;
        AwaitingPaymentAmount = unpaid.Sum(p => p.TotalAmount);
        ConfirmedPurchaseValue = all.Where(p => p.Status == PurchaseStatus.Confirmed).Sum(p => p.TotalAmount);
    }

    // Filters apply as soon as they change (the search box applies as you type).
    partial void OnSelectedSupplierFilterChanged(SupplierSummary? value) => ReloadFromFirstPage();

    partial void OnSelectedStatusFilterChanged(string value) => ReloadFromFirstPage();

    partial void OnFromDateFilterChanged(DateTime? value) => ReloadFromFirstPage();

    partial void OnToDateFilterChanged(DateTime? value) => ReloadFromFirstPage();

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
        SelectedSupplierFilter = AllSuppliers;
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
                SupplierId = SelectedSupplierFilter is { } supplier && supplier.Id != Guid.Empty ? supplier.Id : null,
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

            // Every save/confirm/cancel/delete ends with a reload, so the
            // summary cards are refreshed here and never go stale.
            await RefreshStatsAsync();
            WeakReferenceMessenger.Default.Send(new StockDataChangedMessage());
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
        RefreshFormSupplierOptions(keepSupplierId: null);
        FormSupplier = FormSupplierOptions.FirstOrDefault();
        FormSupplierInvoiceNumber = string.Empty;
        FormPurchaseDate = DateTime.Today;
        FormNotes = string.Empty;
        FormPaymentStatus = PurchasePaymentStatus.Unpaid;
        ProductSearchText = string.Empty;
        ClearLineItems();
        RememberSavedState();
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

        // EditingPurchaseId is set LAST, after every other field, simply to
        // keep this method's field-population order consistent with
        // StartCreate's. Payment status is no longer auto-saved on change
        // (see SavePaymentStatusAsync), so there's no longer a risk of a
        // spurious save from populating these fields while loading.
        EditingPurchaseId = null;
        CurrentStatus = detail.Status;
        PurchaseNumberDisplay = detail.PurchaseNumber;
        RefreshFormSupplierOptions(keepSupplierId: detail.SupplierId);
        FormSupplier = FormSupplierOptions.FirstOrDefault(s => s.Id == detail.SupplierId);
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
        RememberSavedState();
        IsViewingDetail = true;

        RaiseModeChanged();
    }

    private void RefreshFormSupplierOptions(Guid? keepSupplierId)
    {
        FormSupplierOptions.Clear();
        foreach (var supplier in Suppliers.Where(s => s.IsActive || s.Id == keepSupplierId))
        {
            FormSupplierOptions.Add(supplier);
        }
    }

    /// <summary>Back / Close. Asks first if leaving would lose edits.</summary>
    [RelayCommand]
    private async Task CancelEditAsync()
    {
        if (IsViewingDetail && HasUnsavedChanges)
        {
            Dialog.Open(
                "Discard your changes?",
                "You have changes on this purchase that have not been saved. Leaving now will lose them.",
                "Discard Changes",
                "Keep Editing",
                isDanger: true,
                CloseDetailAsync);
            return;
        }

        await CloseDetailAsync();
    }

    private async Task CloseDetailAsync()
    {
        IsViewingDetail = false;
        ErrorMessage = null;
        OnPropertyChanged(nameof(IsShowingList));

        // Reloaded on every return: changes can be saved on the detail screen
        // without leaving it (e.g. Update Payment Status).
        await LoadAsync();
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
        // too, not just in the UI. The same goes for the screen check: Ctrl+S
        // from the list used to re-save whichever purchase was last open,
        // creating a duplicate if that had been a new one.
        if (IsBusy || Dialog.IsOpen || !IsViewingDetail || !CanEditFields)
        {
            return;
        }

        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            var saved = await SaveFormAsync();
            if (saved is null)
            {
                return;
            }

            StatusMessage = $"Purchase {saved.PurchaseNumber} saved as a draft.";
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

    /// <summary>Validates and saves the form as a draft. Returns null (with ErrorMessage set) if it could not be saved.</summary>
    private async Task<PurchaseDetail?> SaveFormAsync()
    {
        ErrorMessage = null;

        if (FormSupplier is null)
        {
            ErrorMessage = "A supplier must be selected.";
            return null;
        }

        if (LineItems.Count == 0)
        {
            ErrorMessage = "Add at least one item before saving.";
            return null;
        }

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
            return null;
        }

        // From here on this is an existing draft, so if the next step (e.g.
        // confirming) fails, trying again edits it instead of creating another.
        EditingPurchaseId = result.Value.Id;
        PurchaseNumberDisplay = result.Value.PurchaseNumber;
        RememberSavedState();
        RaiseModeChanged();
        return result.Value;
    }

    [RelayCommand]
    private void Confirm()
    {
        if (IsBusy || !CanConfirm)
        {
            return;
        }

        ErrorMessage = null;
        if (FormSupplier is null || LineItems.Count == 0)
        {
            ErrorMessage = FormSupplier is null ? "A supplier must be selected." : "Add at least one item before confirming.";
            return;
        }

        var units = LineItems.Sum(i => i.Quantity);
        Dialog.Open(
            "Confirm this purchase?",
            $"{units:0.##} unit(s) across {LineItems.Count} item(s) will be added to stock. "
                + "The purchase can no longer be edited afterwards - it can only be cancelled, which reverses the stock.",
            "Confirm Purchase",
            "Not Yet",
            isDanger: false,
            ConfirmFormAsync);
    }

    /// <summary>
    /// Saves first, so what gets confirmed is what is on screen. Confirming
    /// straight from the database used to silently ignore unsaved edits, adding
    /// the old quantities to stock.
    /// </summary>
    private async Task ConfirmFormAsync()
    {
        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            if (EditingPurchaseId is null || HasUnsavedChanges)
            {
                if (await SaveFormAsync() is null)
                {
                    return;
                }
            }

            var result = await _purchaseService.ConfirmPurchaseAsync(EditingPurchaseId!.Value);
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

    /// <summary>Cancel from the open purchase.</summary>
    [RelayCommand]
    private async Task CancelPurchaseAsync()
    {
        if (EditingPurchaseId is { } id)
        {
            await PromptCancelAsync(id);
        }
    }

    /// <summary>Cancel from a row of the list.</summary>
    [RelayCommand]
    private async Task CancelListedPurchaseAsync(PurchaseSummary purchase) => await PromptCancelAsync(purchase.Id);

    /// <summary>
    /// Shows what cancelling will do before anything changes. If stock can't
    /// be reversed the dialog explains why and offers no way to proceed - the
    /// same rule the service enforces, seen up front instead of after a click.
    /// </summary>
    private async Task PromptCancelAsync(Guid id)
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;

        var previewResult = await _purchaseService.PreviewCancellationAsync(id);
        if (previewResult.IsFailure)
        {
            ErrorMessage = previewResult.Error;
            return;
        }

        var preview = previewResult.Value;

        string message;
        if (preview.Status == PurchaseStatus.Draft)
        {
            message = "This draft has not added any stock, so stock will not change. The purchase is kept on record as Cancelled.";
        }
        else if (preview.CanCancel)
        {
            message = "The stock this purchase added will be removed, as shown below. This cannot be undone.";
        }
        else
        {
            message = "This purchase can't be cancelled yet: some of its stock has already been used. "
                + "Cancel the sales that used it, or add the stock back with a stock adjustment, then try again.";
        }

        string? warning = null;
        if (preview.CanCancel && preview.PaymentStatus != PurchasePaymentStatus.Unpaid)
        {
            warning = $"This purchase is marked {preview.PaymentStatus}. Cancelling will not refund the {preview.TotalAmount:N2} total - "
                + "recover any money paid from the supplier separately.";
        }

        Dialog.Open(
            $"Cancel {preview.PurchaseNumber}?",
            message,
            "Cancel Purchase",
            preview.CanCancel ? "Keep Purchase" : "Close",
            isDanger: true,
            () => CancelConfirmedAsync(id),
            warning,
            canConfirm: preview.CanCancel,
            asksReason: preview.CanCancel,
            stockLines: preview.Lines.Select(l => new StockEffectLine(l.ProductId, l.ProductSku, l.ProductName, l.CurrentStock, -l.QuantityToReverse)));
    }

    private async Task CancelConfirmedAsync(Guid id)
    {
        var reason = Dialog.Reason;

        ErrorMessage = null;
        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            var result = await _purchaseService.CancelPurchaseAsync(id, reason);
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
    private void DeleteDraft()
    {
        if (EditingPurchaseId is not { } id || IsBusy)
        {
            return;
        }

        Dialog.Open(
            $"Delete {PurchaseNumberDisplay}?",
            "This draft will be removed from the list. Stock is not affected.",
            "Delete Draft",
            "Keep Draft",
            isDanger: true,
            () => DeleteConfirmedAsync(id));
    }

    private async Task DeleteConfirmedAsync(Guid id)
    {
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

    /// <summary>
    /// Deliberately NOT auto-saved on dropdown change. That used to be a
    /// fire-and-forget call from a property-changed hook (which can't be
    /// async), and nothing stopped a user from clicking another action
    /// button - or pressing Ctrl+S - while that save was still in flight.
    /// Two overlapping writes sharing the same DbContext at once produced a
    /// DbUpdateConcurrencyException that looked like a data conflict but
    /// wasn't. Requiring an explicit click removes the race at its root
    /// instead of just guarding around it.
    /// </summary>
    [RelayCommand]
    private async Task SavePaymentStatusAsync()
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
            var result = await _purchaseService.SetPaymentStatusAsync(id, FormPaymentStatus);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
            }
            else
            {
                _savedPaymentStatus = FormPaymentStatus;
                StatusMessage = $"Payment status set to {FormPaymentStatus}.";
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
        OnPropertyChanged(nameof(CanChangePayment));
        OnPropertyChanged(nameof(IsCancelled));
        OnPropertyChanged(nameof(IsConfirmed));
        OnPropertyChanged(nameof(StatusDescription));
        OnPropertyChanged(nameof(ItemsSubtitle));
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
