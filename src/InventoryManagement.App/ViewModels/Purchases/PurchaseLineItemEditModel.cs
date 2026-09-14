using CommunityToolkit.Mvvm.ComponentModel;

namespace InventoryManagement.App.ViewModels.Purchases;

public sealed partial class PurchaseLineItemEditModel : ObservableObject
{
    public Guid ProductId { get; }

    public string Sku { get; }

    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private decimal _quantity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private decimal _unitCost;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private decimal _discountAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private decimal _taxPercentage;

    public decimal LineTotal
    {
        get
        {
            var taxable = (Quantity * UnitCost) - DiscountAmount;
            var tax = taxable * TaxPercentage / 100m;
            return taxable + tax;
        }
    }

    public PurchaseLineItemEditModel(
        Guid productId, string sku, string name, decimal quantity, decimal unitCost,
        decimal discountAmount, decimal taxPercentage)
    {
        ProductId = productId;
        Sku = sku;
        Name = name;
        _quantity = quantity;
        _unitCost = unitCost;
        _discountAmount = discountAmount;
        _taxPercentage = taxPercentage;
    }
}
