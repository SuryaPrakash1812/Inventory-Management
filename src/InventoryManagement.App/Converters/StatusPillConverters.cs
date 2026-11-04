using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Media;

namespace InventoryManagement.App.Converters;

/// <summary>
/// Colours for the small rounded status "pills" in lists (Draft, Confirmed,
/// Cancelled, Purchase Receipt, ...). One mapping from status name to tone,
/// shared by the background and foreground converters so the two can never
/// disagree. Pass ConverterParameter="Foreground" for the text colour.
/// </summary>
public sealed partial class StatusPillBrushConverter : IValueConverter
{
    private enum Tone { Neutral, Positive, Negative, Warning, Info }

    private static readonly Dictionary<Tone, (Brush Background, Brush Foreground)> Palette = new()
    {
        [Tone.Neutral] = (Brush(0xF1, 0xF5, 0xF9), Brush(0x47, 0x55, 0x69)),
        [Tone.Positive] = (Brush(0xDC, 0xFC, 0xE7), Brush(0x15, 0x80, 0x3D)),
        [Tone.Negative] = (Brush(0xFE, 0xE2, 0xE2), Brush(0xB9, 0x1C, 0x1C)),
        [Tone.Warning] = (Brush(0xFE, 0xF3, 0xC7), Brush(0xB4, 0x53, 0x09)),
        [Tone.Info] = (Brush(0xDB, 0xEA, 0xFE), Brush(0x1D, 0x4E, 0xD8)),
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var tone = ToneFor(value?.ToString() ?? string.Empty);
        var colors = Palette[tone];
        return string.Equals(parameter as string, "Foreground", StringComparison.OrdinalIgnoreCase)
            ? colors.Foreground
            : colors.Background;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Tone ToneFor(string status) => status switch
    {
        "Confirmed" or "Invoiced" or "Paid" or "PurchaseReceipt" or "SalesReturn" or "AdjustmentIncrease" or "OpeningBalance"
            or "In Stock" or "Active" or "Success" => Tone.Positive,
        "Cancelled" or "PurchaseReturn" or "SaleIssue" or "AdjustmentDecrease" or "Unpaid"
            or "Out of Stock" or "Failed" => Tone.Negative,
        "PartiallyPaid" or "Low Stock" => Tone.Warning,
        "InProgress" or "Category" => Tone.Info,
        _ => Tone.Neutral,
    };

    private static Brush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Turns an enum name like "PurchaseReceipt" into "Purchase Receipt" for display.</summary>
public sealed partial class EnumDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? string.Empty : WordBoundary().Replace(value.ToString() ?? string.Empty, " $1");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    [GeneratedRegex("(?<=[a-z])([A-Z])")]
    private static partial Regex WordBoundary();
}

/// <summary>"Surya Prakash" -&gt; "S"; used for the avatar circle.</summary>
public sealed class InitialsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value as string;
        return string.IsNullOrWhiteSpace(name) ? "?" : char.ToUpperInvariant(name.Trim()[0]).ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
