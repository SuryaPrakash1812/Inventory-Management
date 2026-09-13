using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.Application.Common.Interfaces;
using InventoryManagement.Application.Suppliers;

namespace InventoryManagement.App.ViewModels.Suppliers;

public sealed partial class SuppliersViewModel : ViewModelBase
{
    private readonly ISupplierService _supplierService;

    private const int PageSize = 25;

    public ObservableCollection<SupplierSummary> Suppliers { get; } = new();

    public IReadOnlyList<string> ActiveFilterOptions { get; } = new[] { "All", "Active only", "Inactive only" };

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private string _selectedActiveFilter = "All";

    [ObservableProperty]
    private bool _sortDescending;

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

    public bool CanGoToPreviousPage => PageNumber > 1;

    public bool CanGoToNextPage => PageNumber < TotalPages;

    private int _loadRequestVersion;

    // --- Edit panel state ---

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private Guid? _editingSupplierId;

    [ObservableProperty]
    private string _formName = string.Empty;

    [ObservableProperty]
    private string _formContactPerson = string.Empty;

    [ObservableProperty]
    private string _formPhone = string.Empty;

    [ObservableProperty]
    private string _formEmail = string.Empty;

    [ObservableProperty]
    private string _formAddress = string.Empty;

    [ObservableProperty]
    private string _formTaxId = string.Empty;

    [ObservableProperty]
    private string _formNotes = string.Empty;

    [ObservableProperty]
    private bool _formIsActive = true;

    public bool IsNewSupplier => EditingSupplierId is null;

    // --- History view state ---

    [ObservableProperty]
    private bool _isViewingHistory;

    [ObservableProperty]
    private string _historySupplierName = string.Empty;

    public ObservableCollection<AuditLogEntry> HistoryEntries { get; } = new();

    public bool IsShowingList => !IsEditing && !IsViewingHistory;

    public SuppliersViewModel(ISupplierService supplierService)
    {
        _supplierService = supplierService;
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
        SelectedActiveFilter = "All";
        SortDescending = false;
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
        try
        {
            bool? isActive = SelectedActiveFilter switch
            {
                "Active only" => true,
                "Inactive only" => false,
                _ => null,
            };

            var result = await _supplierService.GetSuppliersAsync(new SupplierQueryParameters
            {
                SearchTerm = string.IsNullOrWhiteSpace(SearchTerm) ? null : SearchTerm,
                IsActive = isActive,
                SortDescending = SortDescending,
                PageNumber = PageNumber,
                PageSize = PageSize,
            });

            if (requestVersion != _loadRequestVersion)
            {
                return;
            }

            Suppliers.Clear();
            foreach (var supplier in result.Items)
            {
                Suppliers.Add(supplier);
            }

            TotalCount = result.TotalCount;
            TotalPages = Math.Max(1, result.TotalPages);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void StartCreate()
    {
        ErrorMessage = null;
        EditingSupplierId = null;
        FormName = string.Empty;
        FormContactPerson = string.Empty;
        FormPhone = string.Empty;
        FormEmail = string.Empty;
        FormAddress = string.Empty;
        FormTaxId = string.Empty;
        FormNotes = string.Empty;
        FormIsActive = true;
        IsEditing = true;

        OnPropertyChanged(nameof(IsNewSupplier));
        OnPropertyChanged(nameof(IsShowingList));
    }

    [RelayCommand]
    private async Task StartEditAsync(SupplierSummary supplier)
    {
        ErrorMessage = null;

        var detail = await _supplierService.GetSupplierByIdAsync(supplier.Id);
        if (detail is null)
        {
            ErrorMessage = "Supplier not found - it may have just been removed.";
            return;
        }

        EditingSupplierId = detail.Id;
        FormName = detail.Name;
        FormContactPerson = detail.ContactPerson ?? string.Empty;
        FormPhone = detail.Phone ?? string.Empty;
        FormEmail = detail.Email ?? string.Empty;
        FormAddress = detail.Address ?? string.Empty;
        FormTaxId = detail.TaxId ?? string.Empty;
        FormNotes = detail.Notes ?? string.Empty;
        FormIsActive = detail.IsActive;
        IsEditing = true;

        OnPropertyChanged(nameof(IsNewSupplier));
        OnPropertyChanged(nameof(IsShowingList));
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        ErrorMessage = null;
        OnPropertyChanged(nameof(IsShowingList));
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;

        IsBusy = true;
        try
        {
            var contactPerson = string.IsNullOrWhiteSpace(FormContactPerson) ? null : FormContactPerson;
            var phone = string.IsNullOrWhiteSpace(FormPhone) ? null : FormPhone;
            var email = string.IsNullOrWhiteSpace(FormEmail) ? null : FormEmail;
            var address = string.IsNullOrWhiteSpace(FormAddress) ? null : FormAddress;
            var taxId = string.IsNullOrWhiteSpace(FormTaxId) ? null : FormTaxId;
            var notes = string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes;

            if (EditingSupplierId is null)
            {
                var createResult = await _supplierService.CreateSupplierAsync(new CreateSupplierRequest(
                    FormName, contactPerson, phone, email, address, taxId, notes));

                if (createResult.IsFailure)
                {
                    ErrorMessage = createResult.Error;
                    return;
                }
            }
            else
            {
                var updateResult = await _supplierService.UpdateSupplierAsync(new UpdateSupplierRequest(
                    EditingSupplierId.Value, FormName, contactPerson, phone, email, address, taxId, notes, FormIsActive));

                if (updateResult.IsFailure)
                {
                    ErrorMessage = updateResult.Error;
                    return;
                }
            }

            IsEditing = false;
            OnPropertyChanged(nameof(IsShowingList));
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeactivateAsync(SupplierSummary supplier)
    {
        ErrorMessage = null;

        var result = await _supplierService.DeactivateSupplierAsync(supplier.Id);
        if (result.IsFailure)
        {
            ErrorMessage = result.Error;
            return;
        }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task ViewHistoryAsync(SupplierSummary supplier)
    {
        ErrorMessage = null;
        HistorySupplierName = supplier.Name;

        var history = await _supplierService.GetHistoryAsync(supplier.Id);

        HistoryEntries.Clear();
        foreach (var entry in history)
        {
            HistoryEntries.Add(entry);
        }

        IsViewingHistory = true;
        OnPropertyChanged(nameof(IsShowingList));
    }

    [RelayCommand]
    private void CloseHistory()
    {
        IsViewingHistory = false;
        OnPropertyChanged(nameof(IsShowingList));
    }
}
