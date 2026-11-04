using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InventoryManagement.Application.Inventory;

namespace InventoryManagement.App.ViewModels;

/// <summary>State behind Controls.ConfirmationDialog: one overlay for every Confirm, Cancel, Delete and Discard on a document screen.</summary>
public sealed partial class ConfirmationDialogViewModel : ObservableObject
{
    private Func<Task>? _action;

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string? _warning;
    [ObservableProperty] private string _confirmText = string.Empty;
    [ObservableProperty] private string _dismissText = string.Empty;
    [ObservableProperty] private bool _isDanger;
    [ObservableProperty] private bool _canConfirm;
    [ObservableProperty] private bool _asksReason;
    [ObservableProperty] private string _reason = string.Empty;

    /// <summary>What happens to stock, per product.</summary>
    public ObservableCollection<StockEffectLine> StockLines { get; } = new();

    public bool HasStockLines => StockLines.Count > 0;

    public void Open(
        string title, string message, string confirmText, string dismissText, bool isDanger, Func<Task> action,
        string? warning = null, bool canConfirm = true, bool asksReason = false, IEnumerable<StockEffectLine>? stockLines = null)
    {
        Title = title;
        Message = message;
        Warning = warning;
        ConfirmText = confirmText;
        DismissText = dismissText;
        IsDanger = isDanger;
        CanConfirm = canConfirm;
        AsksReason = asksReason;
        Reason = string.Empty;

        StockLines.Clear();
        foreach (var line in stockLines ?? Enumerable.Empty<StockEffectLine>())
        {
            StockLines.Add(line);
        }

        OnPropertyChanged(nameof(HasStockLines));
        _action = action;
        IsOpen = true;
    }

    [RelayCommand]
    private void Dismiss()
    {
        IsOpen = false;
        _action = null;
    }

    [RelayCommand]
    private async Task AcceptAsync()
    {
        var action = _action;
        IsOpen = false;
        _action = null;

        if (action is not null)
        {
            await action();
        }
    }
}
