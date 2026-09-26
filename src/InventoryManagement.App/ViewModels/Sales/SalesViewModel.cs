using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.Application.Customers;
using InventoryManagement.Application.Products;
using InventoryManagement.Application.Sales;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.App.ViewModels.Sales;

public sealed partial class SalesViewModel : ViewModelBase
{
    private readonly ISaleService _saleService;
    private readonly ICustomerService _customerService;
    private readonly IProductService _productService;

    private const int PageSize = 25;
    private int _loadRequestVersion;

    public ObservableCollection<SaleSummary> Sales { get; } = new();

    public ObservableCollection<CustomerSummary> Customers { get; } = new();

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

    public SalesViewModel(ISaleService saleService, ICustomerService customerService, IProductService productService)
    {
        _saleService = saleService;
        _customerService = customerService;
        _productService = productService;
    }

    public async Task InitializeAsync()
    {
        await RefreshCustomersAsync();
        await LoadAsync();
    }

    private async Task RefreshCustomersAsync()
    {
        var customers = await _customerService.GetCustomersAsync(new CustomerQueryParameters { PageSize = 500 });

        Customers.Clear();
        foreach (var customer in customers.Items)
        {
            Customers.Add(customer);
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
        SelectedCustomerFilter = null;
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
                CustomerId = SelectedCustomerFilter?.Id,
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
        IsViewingDetail = true;

        RaiseModeChanged();
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
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;

        if (FormCustomer is null)
        {
            ErrorMessage = "A customer must be selected.";
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
                .Select(i => new SaleItemRequest(i.ProductId, i.Quantity, i.UnitPrice, i.DiscountAmount))
                .ToList();

            var request = new SaveDraftSaleRequest(
                EditingSaleId, FormCustomer.Id, new DateTimeOffset(FormSaleDate),
                string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes, itemRequests);

            var result = await _saleService.SaveDraftAsync(request);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            EditingSaleId = result.Value.Id;
            SaleNumberDisplay = result.Value.SaleNumber;
            CurrentStatus = result.Value.Status;
            StatusMessage = "Draft saved.";
            RaiseModeChanged();

            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    [RelayCommand]
    private async Task InvoiceAsync()
    {
        if (IsBusy || EditingSaleId is not { } id)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            var result = await _saleService.InvoiceAsync(id);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            CurrentStatus = result.Value.Status;
            InvoiceNumberDisplay = result.Value.InvoiceNumber;
            StatusMessage = "Sale invoiced - stock issued.";
            RaiseModeChanged();
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    [RelayCommand]
    private async Task CancelSaleAsync()
    {
        if (IsBusy || EditingSaleId is not { } id)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            var result = await _saleService.CancelAsync(id);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            CurrentStatus = result.Value.Status;
            StatusMessage = "Sale cancelled.";
            RaiseModeChanged();
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
        if (IsBusy || EditingSaleId is not { } id)
        {
            return;
        }

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
