using CommunityToolkit.Mvvm.ComponentModel;

namespace InventoryManagement.App.ViewModels.StockAdjustments;

public sealed partial class StockAdjustmentLineItemEditModel : ObservableObject
{
    public Guid ProductId { get; }

    public string Sku { get; }

    public string Name { get; }

    public decimal CurrentStock { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultingStock))]
    private decimal _quantityChange;

    [ObservableProperty]
    private string _notes = string.Empty;

    public decimal ResultingStock => CurrentStock + QuantityChange;

    public StockAdjustmentLineItemEditModel(Guid productId, string sku, string name, decimal currentStock, decimal quantityChange, string? notes)
    {
        ProductId = productId;
        Sku = sku;
        Name = name;
        CurrentStock = currentStock;
        _quantityChange = quantityChange;
        _notes = notes ?? string.Empty;
    }
}
