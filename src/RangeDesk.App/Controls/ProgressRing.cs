using System.Windows;
using System.Windows.Media;

namespace RangeDesk.App.Controls;

/// <summary>Draws actual aggregate transfer coverage; no synthetic animation or metrics.</summary>
public sealed class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(ProgressRing),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double diameter = Math.Min(ActualWidth, ActualHeight);
        if (diameter < 12) return;

        double thickness = Math.Max(4, diameter * .105);
        double radius = (diameter - thickness) / 2.0;
        var center = new Point(ActualWidth / 2.0, ActualHeight / 2.0);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(214, 228, 245)), thickness),
            center, radius, radius);

        double fraction = double.IsFinite(Value) ? Math.Clamp(Value, 0, 100) / 100.0 : 0;
        if (fraction <= 0) return;

        var positive = new Pen(new SolidColorBrush(Color.FromRgb(18, 155, 95)), thickness)
        { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (fraction >= 1)
        {
            dc.DrawEllipse(null, positive, center, radius, radius);
            return;
        }
        double angle = (-Math.PI / 2) + fraction * Math.PI * 2;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, fraction > .5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, positive, geometry);
    }
}
