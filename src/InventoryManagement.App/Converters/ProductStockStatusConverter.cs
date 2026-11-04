using System.Globalization;
using System.Windows.Data;
using InventoryManagement.Application.Products;

namespace InventoryManagement.App.Converters;

/// <summary>
/// A product row's stock status label - the same rules as the Dashboard and
/// the Products summary cards: Inactive, Out of Stock (nothing on hand),
/// Low Stock (at or below minimum), otherwise In Stock. The label then feeds
/// StatusPillBrushConverter for its colours.
/// </summary>
public sealed class ProductStockStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ProductSummary { IsActive: false } => "Inactive",
        ProductSummary { CurrentStock: <= 0 } => "Out of Stock",
        ProductSummary p when p.CurrentStock <= p.MinimumStock => "Low Stock",
        ProductSummary => "In Stock",
        _ => string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
