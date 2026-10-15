using System.Windows;
using System.Windows.Media;

namespace RangeDesk.App.Controls;

/// <summary>Compact segmented transfer progress as seen in the approved download list.
/// The colored blocks reflect real received bytes / known total only.</summary>
public sealed class SegmentedProgress : FrameworkElement
{
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
        var baseBrush = new SolidColorBrush(Color.FromRgb(40, 64, 95));
        var fill = new SolidColorBrush(Color.FromRgb(27, 144, 250));
        baseBrush.Freeze();
        fill.Freeze();
        for (int i = 0; i < segments; i++)
        {
            double start = i * (width + gap);
            double occupied = Math.Clamp(fraction * segments - i, 0, 1);
            var rect = new Rect(start, 0, width, ActualHeight);
            drawing.DrawRoundedRectangle(baseBrush, null, rect, Math.Min(2, ActualHeight / 3), Math.Min(2, ActualHeight / 3));
            if (occupied > 0)
                drawing.DrawRoundedRectangle(fill, null, new Rect(start, 0, width * occupied, ActualHeight),
                    Math.Min(2, ActualHeight / 3), Math.Min(2, ActualHeight / 3));
        }
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, 8);
}
