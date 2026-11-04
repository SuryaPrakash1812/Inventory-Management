using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using InventoryManagement.App.Messages;
using InventoryManagement.Application.Customers;
using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.Products;
using InventoryManagement.Application.Sales;
using InventoryManagement.App.Services;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.App.ViewModels.Sales;

public sealed partial class SalesViewModel : ViewModelBase
{
    /// <summary>The "All" entry at the top of the customer filter (Id = Guid.Empty means no filter).</summary>
    private static readonly CustomerSummary AllCustomers = new(Guid.Empty, "All", null, null, null, true);

    private readonly ISaleService _saleService;
    private readonly ICustomerService _customerService;
    private readonly IProductService _productService;
    private readonly IInventoryService _inventoryService;

    private int _loadRequestVersion;
    private bool _isInitialized;

    [ObservableProperty]
    private int _pageSize = 10;

    // --- Summary cards (all sales, regardless of filters) ---

    [ObservableProperty] private int _allSaleCount;
    [ObservableProperty] private int _draftSaleCount;
    [ObservableProperty] private int _invoicedSaleCount;
    [ObservableProperty] private decimal _invoicedSaleValue;

    public ObservableCollection<SaleSummary> Sales { get; } = new();

    public ObservableCollection<CustomerSummary> Customers { get; } = new();

    /// <summary><see cref="Customers"/> with "All" first - for the list filter, not the edit form.</summary>
    public ObservableCollection<CustomerSummary> CustomerFilterOptions { get; } = new();

    public IReadOnlyList<string> StatusFilterOptions { get; } = new[] { "All", "Draft", "Invoiced", "Cancelled" };

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private CustomerSummary? _selectedCustomerFilter;

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
    private string? _statusMessage;

    public bool CanGoToPreviousPage => PageNumber > 1;

    public bool CanGoToNextPage => PageNumber < TotalPages;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    private bool _isViewingDetail;

    [ObservableProperty]
    private Guid? _editingSaleId;

    [ObservableProperty]
    private SaleStatus _currentStatus = SaleStatus.Draft;

    [ObservableProperty]
    private string? _saleNumberDisplay;

    [ObservableProperty]
    private string? _invoiceNumberDisplay;

    [ObservableProperty]
    private CustomerSummary? _formCustomer;

    [ObservableProperty]
    private DateTime _formSaleDate = DateTime.Today;

    [ObservableProperty]
    private string _formNotes = string.Empty;

    [ObservableProperty]
    private string _productSearchText = string.Empty;

    public ObservableCollection<SaleLineItemEditModel> LineItems { get; } = new();

    public decimal FormSubtotal => LineItems.Sum(i => i.Quantity * i.UnitPrice);

    public decimal FormDiscountTotal => LineItems.Sum(i => i.DiscountAmount);

    public decimal FormTotal => FormSubtotal - FormDiscountTotal;

    public bool IsShowingList => !IsViewingDetail;

    public bool CanEditFields => EditingSaleId is null || CurrentStatus == SaleStatus.Draft;

    public bool CanInvoice => EditingSaleId is not null && CurrentStatus == SaleStatus.Draft;

    public bool CanCancel => EditingSaleId is not null && CurrentStatus != SaleStatus.Cancelled;

    public bool CanDelete => EditingSaleId is not null && CurrentStatus == SaleStatus.Draft;

    public bool IsNewSale => EditingSaleId is null;

    public bool IsReadOnlyLineItems => !CanEditFields;

    public ConfirmationDialogViewModel Dialog { get; } = new();

    private string _savedFormSnapshot = string.Empty;

    /// <summary>Compared with the saved state to tell whether leaving would lose edits.</summary>
    private string BuildFormSnapshot() => string.Join("|", new object?[]
    {
        FormCustomer?.Id, FormSaleDate.Date, FormNotes,
    }.Concat(LineItems.Select(i => (object)$"{i.ProductId}:{i.Quantity:0.####}:{i.UnitPrice:0.####}:{i.DiscountAmount:0.####}")));

    private bool HasUnsavedChanges => CanEditFields && BuildFormSnapshot() != _savedFormSnapshot;

    private readonly NavigationIntent _navigationIntent;

    public SalesViewModel(
        ISaleService saleService, ICustomerService customerService, IProductService productService,
        IInventoryService inventoryService, NavigationIntent navigationIntent)
    {
        _saleService = saleService;
        _customerService = customerService;
        _productService = productService;
        _inventoryService = inventoryService;
        _navigationIntent = navigationIntent;
    }

    public async Task InitializeAsync()
    {
        _navigationIntent.TryTake(typeof(Views.Pages.SalesPage), out var search, out var createNew);
        if (search is not null)
        {
            SearchTerm = search;
        }

        await RefreshCustomersAsync();
        await LoadAsync();
        _isInitialized = true;

        // Arriving from the Dashboard's "Create Sale" button: open a new draft straight away.
        if (createNew)
        {
            StartCreate();
        }
    }

    private async Task RefreshCustomersAsync()
    {
        var customers = await _customerService.GetCustomersAsync(new CustomerQueryParameters { PageSize = 500 });
        var selectedId = SelectedCustomerFilter?.Id ?? Guid.Empty;

        Customers.Clear();
        CustomerFilterOptions.Clear();
        CustomerFilterOptions.Add(AllCustomers);
        foreach (var customer in customers.Items)
        {
            Customers.Add(customer);
            CustomerFilterOptions.Add(customer);
        }

        SelectedCustomerFilter = CustomerFilterOptions.FirstOrDefault(c => c.Id == selectedId) ?? AllCustomers;
    }

    private async Task RefreshStatsAsync()
    {
        var all = (await _saleService.GetSalesAsync(new SaleQueryParameters { PageSize = int.MaxValue })).Items;
        AllSaleCount = all.Count;
        DraftSaleCount = all.Count(s => s.Status == SaleStatus.Draft);
        InvoicedSaleCount = all.Count(s => s.Status == SaleStatus.Invoiced);
        InvoicedSaleValue = all.Where(s => s.Status == SaleStatus.Invoiced).Sum(s => s.TotalAmount);
    }

    // Filters apply as soon as they change (the search box applies as you type).
    partial void OnSelectedCustomerFilterChanged(CustomerSummary? value) => ReloadFromFirstPage();

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
        SelectedCustomerFilter = AllCustomers;
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
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            SaleStatus? status = SelectedStatusFilter switch
            {
                "Draft" => SaleStatus.Draft,
                "Invoiced" => SaleStatus.Invoiced,
                "Cancelled" => SaleStatus.Cancelled,
                _ => null,
            };

            var result = await _saleService.GetSalesAsync(new SaleQueryParameters
            {
                SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm,
                CustomerId = SelectedCustomerFilter is { } customer && customer.Id != Guid.Empty ? customer.Id : null,
                Status = status,
                PageNumber = PageNumber,
                PageSize = PageSize,
            });

            if (requestVersion != _loadRequestVersion)
            {
                return;
            }

            Sales.Clear();
            foreach (var sale in result.Items)
            {
                Sales.Add(sale);
            }

            TotalCount = result.TotalCount;
            TotalPages = Math.Max(1, result.TotalPages);

            // Every save/invoice/cancel/delete ends with a reload, so the
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
        EditingSaleId = null;
        CurrentStatus = SaleStatus.Draft;
        SaleNumberDisplay = "(assigned on save)";
        InvoiceNumberDisplay = null;
        FormCustomer = Customers.FirstOrDefault();
        FormSaleDate = DateTime.Today;
        FormNotes = string.Empty;
        ProductSearchText = string.Empty;
        ClearLineItems();
        _savedFormSnapshot = BuildFormSnapshot();
        IsViewingDetail = true;

        RaiseModeChanged();
        RaiseTotalsChanged();
    }

    [RelayCommand]
    private async Task StartViewAsync(SaleSummary sale)
    {
        ErrorMessage = null;
        StatusMessage = null;

        var detail = await _saleService.GetSaleByIdAsync(sale.Id);
        if (detail is null)
        {
            ErrorMessage = "Sale not found - it may have just been removed.";
            return;
        }

        ShowDetail(detail);
    }

    /// <summary>Fills the form from the saved sale - what was on screen is replaced.</summary>
    private void ShowDetail(SaleDetail detail)
    {
        EditingSaleId = null;
        CurrentStatus = detail.Status;
        SaleNumberDisplay = detail.SaleNumber;
        InvoiceNumberDisplay = detail.InvoiceNumber;
        FormCustomer = Customers.FirstOrDefault(c => c.Id == detail.CustomerId);
        FormSaleDate = detail.SaleDate.Date;
        FormNotes = detail.Notes ?? string.Empty;
        ProductSearchText = string.Empty;

        ClearLineItems();
        foreach (var item in detail.Items)
        {
            AddLineItemToCollection(new SaleLineItemEditModel(
                item.ProductId, item.ProductSku, item.ProductName, item.Quantity, item.UnitPrice, item.DiscountAmount));
        }

        EditingSaleId = detail.Id;
        _savedFormSnapshot = BuildFormSnapshot();
        IsViewingDetail = true;

        RaiseModeChanged();
        RaiseTotalsChanged();
    }

    /// <summary>Back / Close. Asks first if leaving would lose edits.</summary>
    [RelayCommand]
    private async Task CancelEditAsync()
    {
        if (IsViewingDetail && HasUnsavedChanges)
        {
            Dialog.Open(
                "Discard your changes?",
                "You have changes on this sale that have not been saved. Leaving now will lose them.",
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

        // The list is always reloaded when returning to it.
        await LoadAsync();
    }

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
            AddLineItemToCollection(new SaleLineItemEditModel(
                product.Id, product.Sku, product.Name, quantity: 1, unitPrice: product.SellingPrice, discountAmount: 0));
        }

        ProductSearchText = string.Empty;
        RaiseTotalsChanged();
    }

    [RelayCommand]
    private void RemoveLineItem(SaleLineItemEditModel item)
    {
        item.PropertyChanged -= OnLineItemPropertyChanged;
        LineItems.Remove(item);
        RaiseTotalsChanged();
    }

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        // The screen check matters for Ctrl+S, which also fires from the list.
        if (IsBusy || Dialog.IsOpen || !IsViewingDetail || !CanEditFields)
        {
            return;
        }

        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            if (await SaveFormAsync())
            {
                StatusMessage = "Draft saved.";
                await LoadAsync();
            }
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    /// <summary>Validates and saves the form as a draft. Returns false (with ErrorMessage set) if it could not be saved.</summary>
    private async Task<bool> SaveFormAsync()
    {
        ErrorMessage = null;

        if (FormCustomer is null)
        {
            ErrorMessage = "A customer must be selected.";
            return false;
        }

        if (LineItems.Count == 0)
        {
            ErrorMessage = "Add at least one item before saving.";
            return false;
        }

        var itemRequests = LineItems
            .Select(i => new SaleItemRequest(i.ProductId, i.Quantity, i.UnitPrice, i.DiscountAmount))
            .ToList();

        var request = new SaveDraftSaleRequest(
            EditingSaleId, FormCustomer.Id, new DateTimeOffset(FormSaleDate),
            string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes, itemRequests);

        var result = await _saleService.SaveDraftAsync(request);
        if (result.IsFailure)
        {
            ErrorMessage = result.Error;
            return false;
        }

        EditingSaleId = result.Value.Id;
        SaleNumberDisplay = result.Value.SaleNumber;
        CurrentStatus = result.Value.Status;
        _savedFormSnapshot = BuildFormSnapshot();
        RaiseModeChanged();
        return true;
    }

    /// <summary>
    /// Shows what invoicing will do to stock, from what is on screen. If there
    /// isn't enough, the dialog says so and offers no way to proceed - the same
    /// rule the service enforces, seen up front instead of after a click.
    /// </summary>
    [RelayCommand]
    private async Task InvoiceAsync()
    {
        if (IsBusy || EditingSaleId is null)
        {
            return;
        }

        ErrorMessage = null;
        if (FormCustomer is null || LineItems.Count == 0)
        {
            ErrorMessage = FormCustomer is null ? "A customer must be selected." : "Add at least one item before invoicing.";
            return;
        }

        var lines = await _inventoryService.PreviewStockChangesAsync(
            LineItems.Select(i => new StockChange(i.ProductId, -i.Quantity)).ToList());
        var canInvoice = lines.All(l => l.IsPossible);

        Dialog.Open(
            $"Create an invoice for {SaleNumberDisplay}?",
            canInvoice
                ? "These items will be issued from stock, as shown below. The sale can no longer be edited afterwards - it can only be cancelled, which returns the stock."
                : "This sale can't be invoiced yet: there isn't enough stock for the products shown in red. Reduce the quantity or restock first.",
            "Create Invoice",
            canInvoice ? "Not Yet" : "Close",
            isDanger: false,
            InvoiceFormAsync,
            canConfirm: canInvoice,
            stockLines: lines);
    }

    /// <summary>Saves first, so what gets invoiced is what is on screen - invoicing the saved copy would silently ignore unsaved edits.</summary>
    private async Task InvoiceFormAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            if (HasUnsavedChanges && !await SaveFormAsync())
            {
                return;
            }

            var result = await _saleService.InvoiceAsync(EditingSaleId!.Value);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            ShowDetail(result.Value);
            StatusMessage = "Sale invoiced - stock issued.";
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    /// <summary>Shows what cancelling will do to stock before anything changes. Only the saved sale is cancelled, so unsaved edits are dropped.</summary>
    [RelayCommand]
    private async Task CancelSaleAsync()
    {
        if (IsBusy || EditingSaleId is not { } id)
        {
            return;
        }

        ErrorMessage = null;

        var sale = await _saleService.GetSaleByIdAsync(id);
        if (sale is null)
        {
            ErrorMessage = "Sale not found - it may have just been removed.";
            return;
        }

        var wasInvoiced = sale.Status == SaleStatus.Invoiced;
        var lines = wasInvoiced
            ? await _inventoryService.PreviewStockChangesAsync(sale.Items.Select(i => new StockChange(i.ProductId, i.Quantity)).ToList())
            : Array.Empty<StockEffectLine>();

        Dialog.Open(
            $"Cancel {sale.SaleNumber}?",
            wasInvoiced
                ? "The items on this sale will be returned to stock, as shown below. This cannot be undone."
                : "This draft has not issued any stock, so stock will not change. The sale is kept on record as Cancelled.",
            "Cancel Sale",
            "Keep Sale",
            isDanger: true,
            () => CancelConfirmedAsync(id),
            warning: wasInvoiced
                ? $"Cancelling will not refund the {sale.TotalAmount:N2} total - return any money received to the customer separately."
                : null,
            asksReason: true,
            stockLines: lines);
    }

    private async Task CancelConfirmedAsync(Guid id)
    {
        var reason = Dialog.Reason;

        ErrorMessage = null;
        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            var result = await _saleService.CancelAsync(id, reason);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            ShowDetail(result.Value);
            StatusMessage = "Sale cancelled.";
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
        if (IsBusy || EditingSaleId is not { } id)
        {
            return;
        }

        Dialog.Open(
            $"Delete {SaleNumberDisplay}?",
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
            var result = await _saleService.DeleteDraftAsync(id);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            StatusMessage = "Draft sale deleted.";
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

    private void ClearLineItems()
    {
        foreach (var item in LineItems)
        {
            item.PropertyChanged -= OnLineItemPropertyChanged;
        }

        LineItems.Clear();
    }

    private void AddLineItemToCollection(SaleLineItemEditModel item)
    {
        item.PropertyChanged += OnLineItemPropertyChanged;
        LineItems.Add(item);
    }

    private void OnLineItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RaiseTotalsChanged();

    private void RaiseTotalsChanged()
    {
        OnPropertyChanged(nameof(FormSubtotal));
        OnPropertyChanged(nameof(FormDiscountTotal));
        OnPropertyChanged(nameof(FormTotal));
    }

    private void RaiseModeChanged()
    {
        OnPropertyChanged(nameof(IsShowingList));
        OnPropertyChanged(nameof(CanEditFields));
        OnPropertyChanged(nameof(CanInvoice));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(IsNewSale));
        OnPropertyChanged(nameof(IsReadOnlyLineItems));
    }
}
