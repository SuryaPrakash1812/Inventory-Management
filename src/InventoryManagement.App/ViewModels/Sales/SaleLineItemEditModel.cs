using CommunityToolkit.Mvvm.ComponentModel;

namespace InventoryManagement.App.ViewModels.Sales;

public sealed partial class SaleLineItemEditModel : ObservableObject
{
    public Guid ProductId { get; }

    public string Sku { get; }

    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private decimal _quantity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private decimal _unitPrice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private decimal _discountAmount;

    public decimal LineTotal => (Quantity * UnitPrice) - DiscountAmount;

    public SaleLineItemEditModel(Guid productId, string sku, string name, decimal quantity, decimal unitPrice, decimal discountAmount)
    {
        ProductId = productId;
        Sku = sku;
        Name = name;
        _quantity = quantity;
        _unitPrice = unitPrice;
        _discountAmount = discountAmount;
    }
}
