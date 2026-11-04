using System.Windows;
using System.Windows.Media;

namespace InventoryManagement.App.Controls;

/// <summary>
/// A ring chart drawn in OnRender: one arc per <see cref="ChartSegment"/>,
/// proportional to its value, starting at 12 o'clock and going clockwise.
/// The text in the middle is left to XAML (overlay a TextBlock), so the
/// control only draws geometry. With no data it draws an empty grey ring.
/// </summary>
public sealed class DonutChart : FrameworkElement
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<ChartSegment>), typeof(DonutChart),
        new FrameworkPropertyMetadata(Array.Empty<ChartSegment>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(DonutChart),
        new FrameworkPropertyMetadata(26.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush EmptyBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)));

    public IReadOnlyList<ChartSegment> Segments
    {
        get => (IReadOnlyList<ChartSegment>)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var outer = size / 2;
        var inner = Math.Max(0, outer - Thickness);

        var segments = (Segments ?? Array.Empty<ChartSegment>()).Where(s => s.Value > 0).ToList();
        var total = segments.Sum(s => s.Value);

        if (total == 0)
        {
            dc.DrawGeometry(EmptyBrush, null, Ring(center, outer, inner));
            return;
        }

        var startAngle = 0.0;
        foreach (var segment in segments)
        {
            var sweep = 360.0 * segment.Value / total;
            var geometry = sweep >= 359.999
                ? Ring(center, outer, inner)
                : Arc(center, outer, inner, startAngle, sweep);
            dc.DrawGeometry(segment.Brush, null, geometry);
            startAngle += sweep;
        }
    }

    private static Geometry Ring(Point center, double outer, double inner) =>
        new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new EllipseGeometry(center, outer, outer),
            new EllipseGeometry(center, inner, inner));

    private static Geometry Arc(Point center, double outer, double inner, double startDegrees, double sweepDegrees)
    {
        var endDegrees = startDegrees + sweepDegrees;
        var largeArc = sweepDegrees > 180;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(PointOn(center, outer, startDegrees), true, true);
            ctx.ArcTo(PointOn(center, outer, endDegrees), new Size(outer, outer), 0, largeArc, SweepDirection.Clockwise, true, true);
            ctx.LineTo(PointOn(center, inner, endDegrees), true, true);
            ctx.ArcTo(PointOn(center, inner, startDegrees), new Size(inner, inner), 0, largeArc, SweepDirection.Counterclockwise, true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>0° is 12 o'clock, increasing clockwise.</summary>
    private static Point PointOn(Point center, double radius, double degrees)
    {
        var radians = (degrees - 90) * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
