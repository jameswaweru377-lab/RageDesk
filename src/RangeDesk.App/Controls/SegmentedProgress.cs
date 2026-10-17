using System.Windows;
using System.Windows.Media;

namespace RangeDesk.App.Controls;

/// <summary>Compact segmented transfer progress as seen in the approved download list.
/// The colored blocks reflect real received bytes / known total only.</summary>
public sealed class SegmentedProgress : FrameworkElement
{
    // Frozen brushes are reused for every painted segment and never allocate per download tick.
    private static readonly Brush TrackFill = CreateTrackFill();
    private static readonly Brush ActiveFill = CreateActiveFill();

    private static Brush CreateTrackFill()
    {
        var brush = new SolidColorBrush(Color.FromRgb(24, 54, 90));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateActiveFill()
    {
        var brush = new LinearGradientBrush(
            Color.FromRgb(34, 214, 255),
            Color.FromRgb(9, 104, 251),
            new Point(0, 0), new Point(1, 0));
        brush.Freeze();
        return brush;
    }

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(SegmentedProgress),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty SegmentCountProperty =
        DependencyProperty.Register(nameof(SegmentCount), typeof(int), typeof(SegmentedProgress),
            new FrameworkPropertyMetadata(12, FrameworkPropertyMetadataOptions.AffectsRender));

    public int SegmentCount
    {
        get => (int)GetValue(SegmentCountProperty);
        set => SetValue(SegmentCountProperty, value);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        int segments = Math.Clamp(SegmentCount, 1, 48);
        double gap = Math.Min(3, ActualWidth / Math.Max(1, segments * 9d));
        double width = (ActualWidth - gap * (segments - 1)) / segments;
        if (width <= 0 || ActualHeight <= 0) return;
        double fraction = double.IsFinite(Value) ? Math.Clamp(Value, 0, 100) / 100 : 0;

        for (int i = 0; i < segments; i++)
        {
            double start = i * (width + gap);
            double occupied = Math.Clamp(fraction * segments - i, 0, 1);
            var rect = new Rect(start, 0, width, ActualHeight);
            drawing.DrawRoundedRectangle(TrackFill, null, rect, Math.Min(2, ActualHeight / 3), Math.Min(2, ActualHeight / 3));
            if (occupied > 0)
                drawing.DrawRoundedRectangle(ActiveFill, null, new Rect(start, 0, width * occupied, ActualHeight),
                    Math.Min(2, ActualHeight / 3), Math.Min(2, ActualHeight / 3));
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, 8);
}
