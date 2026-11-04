using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace InventoryManagement.App.Controls;

/// <summary>
/// A small, dependency-free area/line chart drawn directly in OnRender: a
/// "nice" rounded y-axis with dashed gridlines, up to eight x-axis labels,
/// a gradient-filled area under the line, and dots on each point. Hovering
/// shows the nearest point's tooltip text. Deliberately minimal - the
/// Dashboard needs one trend line, not a charting library.
/// </summary>
public sealed class LineChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<ChartPoint>), typeof(LineChart),
        new FrameworkPropertyMetadata(Array.Empty<ChartPoint>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(LineChart),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LeftAxisWidth = 52;
    private const double BottomAxisHeight = 28;
    private const double TopPadding = 10;
    private const double RightPadding = 16;

    private static readonly Brush AxisTextBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)));
    private static readonly Pen GridPen = FrozenPen(new Pen(new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)), 1) { DashStyle = new DashStyle(new double[] { 4, 4 }, 0) });
    private static readonly Pen BaselinePen = FrozenPen(new Pen(new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)), 1));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private readonly ToolTip _toolTip = new() { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    private List<Point> _screenPoints = new();

    public LineChart()
    {
        ToolTip = _toolTip;
        ToolTipService.SetInitialShowDelay(this, 0);
        MouseMove += OnMouseMove;
    }

    public IReadOnlyList<ChartPoint> Points
    {
        get => (IReadOnlyList<ChartPoint>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Transparent background so the whole area receives mouse events for the tooltip.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        var points = Points ?? Array.Empty<ChartPoint>();
        var plot = new Rect(
            LeftAxisWidth, TopPadding,
            Math.Max(0, ActualWidth - LeftAxisWidth - RightPadding),
            Math.Max(0, ActualHeight - TopPadding - BottomAxisHeight));

        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        var max = points.Count == 0 ? 0 : (double)points.Max(p => p.Value);
        var (axisMax, step) = NiceScale(max);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        for (var value = 0.0; value <= axisMax + step / 2; value += step)
        {
            var y = plot.Bottom - value / axisMax * plot.Height;
            dc.DrawLine(value == 0 ? BaselinePen : GridPen, new Point(plot.Left, y), new Point(plot.Right, y));

            var text = Text(FormatAxis(value), pixelsPerDip);
            dc.DrawText(text, new Point(plot.Left - 10 - text.Width, y - text.Height / 2));
        }

        if (points.Count == 0)
        {
            return;
        }

        var stepX = points.Count == 1 ? 0 : plot.Width / (points.Count - 1);
        _screenPoints = points
            .Select((p, i) => new Point(
                points.Count == 1 ? plot.Left + plot.Width / 2 : plot.Left + i * stepX,
                plot.Bottom - (double)p.Value / axisMax * plot.Height))
            .ToList();

        // X labels: every point when there are few, otherwise evenly thinned to at most eight.
        var labelEvery = Math.Max(1, (int)Math.Ceiling(points.Count / 8.0));
        for (var i = 0; i < points.Count; i++)
        {
            if (i % labelEvery != 0 && i != points.Count - 1)
            {
                continue;
            }

            var text = Text(points[i].Label, pixelsPerDip);
            var x = Math.Clamp(_screenPoints[i].X - text.Width / 2, 0, ActualWidth - text.Width);
            dc.DrawText(text, new Point(x, plot.Bottom + 8));
        }

        var lineColor = (LineBrush as SolidColorBrush)?.Color ?? Color.FromRgb(0x25, 0x63, 0xEB);

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(_screenPoints[0].X, plot.Bottom), true, true);
            foreach (var p in _screenPoints)
            {
                ctx.LineTo(p, true, true);
            }

            ctx.LineTo(new Point(_screenPoints[^1].X, plot.Bottom), true, true);
        }

        area.Freeze();
        var fill = new LinearGradientBrush(
            Color.FromArgb(0x40, lineColor.R, lineColor.G, lineColor.B),
            Color.FromArgb(0x00, lineColor.R, lineColor.G, lineColor.B),
            90);
        dc.DrawGeometry(fill, null, area);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(_screenPoints[0], false, false);
            foreach (var p in _screenPoints.Skip(1))
            {
                ctx.LineTo(p, true, true);
            }
        }

        line.Freeze();
        var linePen = new Pen(LineBrush, 2) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, linePen, line);

        // Dots only while they stay distinguishable.
        if (points.Count <= 31)
        {
            var dotPen = new Pen(Brushes.White, 1.5);
            foreach (var p in _screenPoints)
            {
                dc.DrawEllipse(LineBrush, dotPen, p, 4, 4);
            }
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var points = Points;
        if (points is null || points.Count == 0 || _screenPoints.Count != points.Count)
        {
            _toolTip.Content = null;
            _toolTip.IsOpen = false;
            return;
        }

        var x = e.GetPosition(this).X;
        var nearest = 0;
        for (var i = 1; i < _screenPoints.Count; i++)
        {
            if (Math.Abs(_screenPoints[i].X - x) < Math.Abs(_screenPoints[nearest].X - x))
            {
                nearest = i;
            }
        }

        _toolTip.Content = points[nearest].ToolTip ?? $"{points[nearest].Label}: {points[nearest].Value:N2}";
        _toolTip.IsOpen = true;
    }

    /// <summary>Rounds the axis top up to 1/2/2.5/5 × 10ⁿ with four or five steps, e.g. 980 → 1,000 in steps of 250.</summary>
    private static (double Max, double Step) NiceScale(double max)
    {
        if (max <= 0)
        {
            return (100, 25);
        }

        var rough = max / 4;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var step = new[] { 1, 2, 2.5, 5, 10 }.Select(m => m * magnitude).First(s => s >= rough);
        return (Math.Ceiling(max / step) * step, step);
    }

    private static string FormatAxis(double value) => value switch
    {
        >= 10_000_000 => (value / 1_000_000).ToString("0.#", CultureInfo.CurrentCulture) + "M",
        >= 100_000 => (value / 1_000).ToString("0.#", CultureInfo.CurrentCulture) + "K",
        _ => value.ToString("N0", CultureInfo.CurrentCulture),
    };

    private static FormattedText Text(string text, double pixelsPerDip) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface, 12, AxisTextBrush, pixelsPerDip);

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
