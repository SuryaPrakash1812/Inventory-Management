using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.Application.Products;
using InventoryManagement.Application.StockAdjustments;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.App.ViewModels.StockAdjustments;

public sealed partial class StockAdjustmentsViewModel : ViewModelBase
{
    private readonly IStockAdjustmentService _stockAdjustmentService;
    private readonly IProductService _productService;

    private const int PageSize = 25;
    private int _loadRequestVersion;

    // --- List view state ---

    public ObservableCollection<StockAdjustmentSummary> Adjustments { get; } = new();

    public IReadOnlyList<string> StatusFilterOptions { get; } = new[] { "All", "Draft", "Confirmed", "Cancelled" };

    public IReadOnlyList<StockAdjustmentReason> ReasonOptions { get; } = Enum.GetValues<StockAdjustmentReason>();

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private string _selectedStatusFilter = "All";

    [ObservableProperty]
    private StockAdjustmentReason? _selectedReasonFilter;

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

    // --- Detail/edit view state ---

    [ObservableProperty]
    private bool _isViewingDetail;

    [ObservableProperty]
    private Guid? _editingAdjustmentId;

    [ObservableProperty]
    private StockAdjustmentStatus _currentStatus = StockAdjustmentStatus.Draft;

    [ObservableProperty]
    private string? _adjustmentNumberDisplay;

    [ObservableProperty]
    private DateTime _formAdjustmentDate = DateTime.Today;

    [ObservableProperty]
    private StockAdjustmentReason _formReason = StockAdjustmentReason.Correction;

    [ObservableProperty]
    private string _formNotes = string.Empty;

    [ObservableProperty]
    private string _productSearchText = string.Empty;

    public ObservableCollection<StockAdjustmentLineItemEditModel> LineItems { get; } = new();

    public bool IsShowingList => !IsViewingDetail;

    public bool CanEditFields => EditingAdjustmentId is null || CurrentStatus == StockAdjustmentStatus.Draft;

    public bool CanConfirm => EditingAdjustmentId is not null && CurrentStatus == StockAdjustmentStatus.Draft;

    public bool CanCancel => EditingAdjustmentId is not null && CurrentStatus != StockAdjustmentStatus.Cancelled;

    public bool CanDelete => EditingAdjustmentId is not null && CurrentStatus == StockAdjustmentStatus.Draft;

    public bool IsNewAdjustment => EditingAdjustmentId is null;

    public bool IsReadOnlyLineItems => !CanEditFields;

    public StockAdjustmentsViewModel(IStockAdjustmentService stockAdjustmentService, IProductService productService)
    {
        _stockAdjustmentService = stockAdjustmentService;
        _productService = productService;
    }

    public async Task InitializeAsync() => await LoadAsync();

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
        SelectedStatusFilter = "All";
        SelectedReasonFilter = null;
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
            StockAdjustmentStatus? status = SelectedStatusFilter switch
            {
                "Draft" => StockAdjustmentStatus.Draft,
                "Confirmed" => StockAdjustmentStatus.Confirmed,
                "Cancelled" => StockAdjustmentStatus.Cancelled,
                _ => null,
            };

            var result = await _stockAdjustmentService.GetStockAdjustmentsAsync(new StockAdjustmentQueryParameters
            {
                SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm,
                Status = status,
                Reason = SelectedReasonFilter,
                PageNumber = PageNumber,
                PageSize = PageSize,
            });

            if (requestVersion != _loadRequestVersion)
            {
                return;
            }

            Adjustments.Clear();
            foreach (var adjustment in result.Items)
            {
                Adjustments.Add(adjustment);
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
        EditingAdjustmentId = null;
        CurrentStatus = StockAdjustmentStatus.Draft;
        AdjustmentNumberDisplay = "(assigned on save)";
        FormAdjustmentDate = DateTime.Today;
        FormReason = StockAdjustmentReason.Correction;
        FormNotes = string.Empty;
        ProductSearchText = string.Empty;
        LineItems.Clear();
        IsViewingDetail = true;

        RaiseModeChanged();
    }

    [RelayCommand]
    private async Task StartViewAsync(StockAdjustmentSummary adjustment)
    {
        ErrorMessage = null;
        StatusMessage = null;

        var detail = await _stockAdjustmentService.GetStockAdjustmentByIdAsync(adjustment.Id);
        if (detail is null)
        {
            ErrorMessage = "Stock adjustment not found - it may have just been removed.";
            return;
        }

        EditingAdjustmentId = null;
        CurrentStatus = detail.Status;
        AdjustmentNumberDisplay = detail.AdjustmentNumber;
        FormAdjustmentDate = detail.AdjustmentDate.Date;
        FormReason = detail.Reason;
        FormNotes = detail.Notes ?? string.Empty;
        ProductSearchText = string.Empty;

        LineItems.Clear();
        foreach (var item in detail.Items)
        {
            LineItems.Add(new StockAdjustmentLineItemEditModel(
                item.ProductId, item.ProductSku, item.ProductName, item.QuantityBefore, item.QuantityChange, item.Notes));
        }

        EditingAdjustmentId = detail.Id;
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

        if (LineItems.Any(i => i.ProductId == product.Id))
        {
            ErrorMessage = $"'{product.Name}' is already on this adjustment.";
            return;
        }

        LineItems.Add(new StockAdjustmentLineItemEditModel(
            product.Id, product.Sku, product.Name, currentStock: product.CurrentStock, quantityChange: 0, notes: null));

        ProductSearchText = string.Empty;
    }

    [RelayCommand]
    private void RemoveLineItem(StockAdjustmentLineItemEditModel item) => LineItems.Remove(item);

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;

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
                .Select(i => new StockAdjustmentItemRequest(i.ProductId, i.QuantityChange, string.IsNullOrWhiteSpace(i.Notes) ? null : i.Notes))
                .ToList();

            var request = new SaveDraftStockAdjustmentRequest(
                EditingAdjustmentId, new DateTimeOffset(FormAdjustmentDate), FormReason,
                string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes, itemRequests);

            var result = await _stockAdjustmentService.SaveDraftAsync(request);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            EditingAdjustmentId = result.Value.Id;
            AdjustmentNumberDisplay = result.Value.AdjustmentNumber;
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
    private async Task ConfirmAsync()
    {
        if (IsBusy || EditingAdjustmentId is not { } id)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            var result = await _stockAdjustmentService.ConfirmAsync(id);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            CurrentStatus = result.Value.Status;
            StatusMessage = "Stock adjustment confirmed - stock updated.";
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
    private async Task CancelAdjustmentAsync()
    {
        if (IsBusy || EditingAdjustmentId is not { } id)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            var result = await _stockAdjustmentService.CancelAsync(id);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            CurrentStatus = result.Value.Status;
            StatusMessage = "Stock adjustment cancelled.";
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
        if (IsBusy || EditingAdjustmentId is not { } id)
        {
            return;
        }

        IsBusy = true;
        OnPropertyChanged(nameof(IsNotBusy));
        try
        {
            var result = await _stockAdjustmentService.DeleteDraftAsync(id);
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

    private void RaiseModeChanged()
    {
        OnPropertyChanged(nameof(IsShowingList));
        OnPropertyChanged(nameof(CanEditFields));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(IsNewAdjustment));
        OnPropertyChanged(nameof(IsReadOnlyLineItems));
    }
}
