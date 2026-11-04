using System.Windows.Media;

namespace InventoryManagement.App.Controls;

/// <summary>One point on a <see cref="LineChart"/>: the x-axis label and its value.</summary>
public sealed record ChartPoint(string Label, decimal Value, string? ToolTip = null);

/// <summary>One slice of a <see cref="DonutChart"/>, also used as a row of the legend next to it.</summary>
public sealed record ChartSegment(string Label, int Value, Brush Brush, double Percent)
{
    public string PercentText => $"{Percent:0}%";
}
