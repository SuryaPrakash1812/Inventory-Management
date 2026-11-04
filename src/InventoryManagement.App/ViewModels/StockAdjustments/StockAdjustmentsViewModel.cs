using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using InventoryManagement.App.Messages;
using InventoryManagement.Application.Inventory;
using InventoryManagement.Application.Products;
using InventoryManagement.Application.StockAdjustments;
using InventoryManagement.Domain.Enums;

namespace InventoryManagement.App.ViewModels.StockAdjustments;

public sealed partial class StockAdjustmentsViewModel : ViewModelBase
{
    private readonly IStockAdjustmentService _stockAdjustmentService;
    private readonly IProductService _productService;
    private readonly IInventoryService _inventoryService;

    private int _loadRequestVersion;
    private bool _isInitialized;

    [ObservableProperty]
    private int _pageSize = 10;

    // --- Summary cards (all adjustments, regardless of filters) ---

    [ObservableProperty] private int _allAdjustmentCount;
    [ObservableProperty] private int _draftAdjustmentCount;
    [ObservableProperty] private int _confirmedAdjustmentCount;
    [ObservableProperty] private int _cancelledAdjustmentCount;

    // --- List view state ---

    public ObservableCollection<StockAdjustmentSummary> Adjustments { get; } = new();

    public IReadOnlyList<string> StatusFilterOptions { get; } = new[] { "All", "Draft", "Confirmed", "Cancelled" };

    /// <summary>Reasons for the edit form.</summary>
    public IReadOnlyList<StockAdjustmentReason> ReasonOptions { get; } = Enum.GetValues<StockAdjustmentReason>();

    /// <summary>Reasons for the list filter, "All" first.</summary>
    public IReadOnlyList<string> ReasonFilterOptions { get; } =
        new[] { "All" }.Concat(Enum.GetNames<StockAdjustmentReason>()).ToList();

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private string _selectedStatusFilter = "All";

    [ObservableProperty]
    private string _selectedReasonFilter = "All";

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

    public ConfirmationDialogViewModel Dialog { get; } = new();

    private string _savedFormSnapshot = string.Empty;

    /// <summary>Compared with the saved state to tell whether leaving would lose edits.</summary>
    private string BuildFormSnapshot() => string.Join("|", new object?[]
    {
        FormAdjustmentDate.Date, FormReason, FormNotes,
    }.Concat(LineItems.Select(i => (object)$"{i.ProductId}:{i.QuantityChange:0.####}:{i.Notes}")));

    private bool HasUnsavedChanges => CanEditFields && BuildFormSnapshot() != _savedFormSnapshot;

    public StockAdjustmentsViewModel(
        IStockAdjustmentService stockAdjustmentService, IProductService productService, IInventoryService inventoryService)
    {
        _stockAdjustmentService = stockAdjustmentService;
        _productService = productService;
        _inventoryService = inventoryService;
    }

    public async Task InitializeAsync()
    {
        await LoadAsync();
        _isInitialized = true;
    }

    private async Task RefreshStatsAsync()
    {
        var all = (await _stockAdjustmentService.GetStockAdjustmentsAsync(new StockAdjustmentQueryParameters { PageSize = int.MaxValue })).Items;
        AllAdjustmentCount = all.Count;
        DraftAdjustmentCount = all.Count(a => a.Status == StockAdjustmentStatus.Draft);
        ConfirmedAdjustmentCount = all.Count(a => a.Status == StockAdjustmentStatus.Confirmed);
        CancelledAdjustmentCount = all.Count(a => a.Status == StockAdjustmentStatus.Cancelled);
    }

    // Filters apply as soon as they change (the search box applies as you type).
    partial void OnSelectedStatusFilterChanged(string value) => ReloadFromFirstPage();

    partial void OnSelectedReasonFilterChanged(string value) => ReloadFromFirstPage();

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
        SelectedStatusFilter = "All";
        SelectedReasonFilter = "All";
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
                Reason = Enum.TryParse<StockAdjustmentReason>(SelectedReasonFilter, out var reason) ? reason : null,
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
        EditingAdjustmentId = null;
        CurrentStatus = StockAdjustmentStatus.Draft;
        AdjustmentNumberDisplay = "(assigned on save)";
        FormAdjustmentDate = DateTime.Today;
        FormReason = StockAdjustmentReason.Correction;
        FormNotes = string.Empty;
        ProductSearchText = string.Empty;
        LineItems.Clear();
        _savedFormSnapshot = BuildFormSnapshot();
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

        ShowDetail(detail);
    }

    /// <summary>Fills the form from the saved adjustment - what was on screen is replaced.</summary>
    private void ShowDetail(StockAdjustmentDetail detail)
    {
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
        _savedFormSnapshot = BuildFormSnapshot();
        IsViewingDetail = true;

        RaiseModeChanged();
    }

    /// <summary>Back / Close. Asks first if leaving would lose edits.</summary>
    [RelayCommand]
    private async Task CancelEditAsync()
    {
        if (IsViewingDetail && HasUnsavedChanges)
        {
            Dialog.Open(
                "Discard your changes?",
                "You have changes on this adjustment that have not been saved. Leaving now will lose them.",
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

        if (LineItems.Count == 0)
        {
            ErrorMessage = "Add at least one item before saving.";
            return false;
        }

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
            return false;
        }

        EditingAdjustmentId = result.Value.Id;
        AdjustmentNumberDisplay = result.Value.AdjustmentNumber;
        CurrentStatus = result.Value.Status;
        _savedFormSnapshot = BuildFormSnapshot();
        RaiseModeChanged();
        return true;
    }

    /// <summary>
    /// Shows what confirming will do to stock, from what is on screen. If a
    /// product would go below zero, the dialog says so and offers no way to
    /// proceed - the same rule the service enforces, seen up front instead of
    /// after a click.
    /// </summary>
    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (IsBusy || EditingAdjustmentId is null)
        {
            return;
        }

        ErrorMessage = null;
        if (LineItems.Count == 0)
        {
            ErrorMessage = "Add at least one item before confirming.";
            return;
        }

        var lines = await _inventoryService.PreviewStockChangesAsync(
            LineItems.Select(i => new StockChange(i.ProductId, i.QuantityChange)).ToList());
        var canConfirm = lines.All(l => l.IsPossible);

        Dialog.Open(
            $"Confirm {AdjustmentNumberDisplay}?",
            canConfirm
                ? "These changes will be applied to stock, as shown below. The adjustment can no longer be edited afterwards - it can only be cancelled, which reverses them."
                : "This adjustment can't be confirmed: the products shown in red would go below zero. Change the quantities first.",
            "Confirm Adjustment",
            canConfirm ? "Not Yet" : "Close",
            isDanger: false,
            ConfirmFormAsync,
            canConfirm: canConfirm,
            stockLines: lines);
    }

    /// <summary>Saves first, so what gets confirmed is what is on screen - confirming the saved copy would silently ignore unsaved edits.</summary>
    private async Task ConfirmFormAsync()
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

            var result = await _stockAdjustmentService.ConfirmAsync(EditingAdjustmentId!.Value);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            ShowDetail(result.Value);
            StatusMessage = "Stock adjustment confirmed - stock updated.";
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    /// <summary>
    /// Shows what cancelling will do to stock before anything changes, and
    /// offers no way to proceed if it can't be reversed. Only the saved
    /// adjustment is cancelled, so unsaved edits are dropped.
    /// </summary>
    [RelayCommand]
    private async Task CancelAdjustmentAsync()
    {
        if (IsBusy || EditingAdjustmentId is not { } id)
        {
            return;
        }

        ErrorMessage = null;

        var adjustment = await _stockAdjustmentService.GetStockAdjustmentByIdAsync(id);
        if (adjustment is null)
        {
            ErrorMessage = "Stock adjustment not found - it may have just been removed.";
            return;
        }

        var wasConfirmed = adjustment.Status == StockAdjustmentStatus.Confirmed;
        var lines = wasConfirmed
            ? await _inventoryService.PreviewStockChangesAsync(adjustment.Items.Select(i => new StockChange(i.ProductId, -i.QuantityChange)).ToList())
            : Array.Empty<StockEffectLine>();
        var canCancel = lines.All(l => l.IsPossible);

        string message;
        if (!wasConfirmed)
        {
            message = "This draft has not changed any stock, so stock will not change. The adjustment is kept on record as Cancelled.";
        }
        else if (canCancel)
        {
            message = "The changes this adjustment made will be reversed, as shown below. This cannot be undone.";
        }
        else
        {
            message = "This adjustment can't be cancelled yet: some of the stock it added has already been used. "
                + "Add the stock back or cancel what used it, then try again.";
        }

        Dialog.Open(
            $"Cancel {adjustment.AdjustmentNumber}?",
            message,
            "Cancel Adjustment",
            canCancel ? "Keep Adjustment" : "Close",
            isDanger: true,
            () => CancelConfirmedAsync(id),
            canConfirm: canCancel,
            asksReason: canCancel,
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
            var result = await _stockAdjustmentService.CancelAsync(id, reason);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            ShowDetail(result.Value);
            StatusMessage = "Stock adjustment cancelled.";
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
        if (IsBusy || EditingAdjustmentId is not { } id)
        {
            return;
        }

        Dialog.Open(
            $"Delete {AdjustmentNumberDisplay}?",
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
            var result = await _stockAdjustmentService.DeleteDraftAsync(id);
            if (result.IsFailure)
            {
                ErrorMessage = result.Error;
                return;
            }

            StatusMessage = "Draft adjustment deleted.";
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
