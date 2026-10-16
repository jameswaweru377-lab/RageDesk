using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using RangeDesk.Core;

namespace RangeDesk.App.Controls;

/// <summary>
/// Draws actual, measured receive-rate samples. It never invents chart values.
/// Retained samples are capped in the download model and redraws are event-driven.
/// </summary>
public sealed class SpeedGraph : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(DownloadEntry), typeof(SpeedGraph),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSourceChanged));

    private static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(32, 164, 255));
    private static readonly Brush FillBrush = new SolidColorBrush(Color.FromArgb(38, 32, 164, 255));
    private static readonly Brush GridBrush = new SolidColorBrush(Color.FromRgb(29, 63, 96));
    private static readonly Brush LabelBrush = new SolidColorBrush(Color.FromRgb(141, 175, 203));

    public DownloadEntry? Source
    {
        get => (DownloadEntry?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var graph = (SpeedGraph)d;
        if (e.OldValue is DownloadEntry previous) previous.PropertyChanged -= graph.OnEntryChanged;
        if (e.NewValue is DownloadEntry current) current.PropertyChanged += graph.OnEntryChanged;
        graph.InvalidateVisual();
    }

    private void OnEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) InvalidateVisual();
        else Dispatcher.BeginInvoke((Action)InvalidateVisual);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double w = RenderSize.Width;
        double h = RenderSize.Height;
        if (w < 60 || h < 35) return;

        var region = new Rect(0, 0, w, h);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(7, 26, 47)), null, region, 5, 5);

        const double left = 10, right = 10, top = 22, bottom = 12;
        var plot = new Rect(left, top, Math.Max(1, w - left - right), Math.Max(1, h - top - bottom));
        var gridPen = new Pen(GridBrush, 0.7);
        for (int i = 1; i < 4; i++)
        {
            double y = plot.Top + plot.Height * i / 4;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        var data = Source?.SpeedSamples.ToArray() ?? Array.Empty<double>();
        if (data.Length == 0)
        {
            DrawCaption(dc, "Speed graph starts when you download", plot.Left + 5, plot.Top + 12);
            return;
        }

        double maximum = Math.Max(1, data.Max() * 1.16);
        DrawCaption(dc, "LIVE RECEIVED SPEED  ·  " + DownloadEntry.FormatSize(data[^1]) + "/s",
            plot.Left + 4, 4);

        if (data.Length == 1) return;
        var coordinates = new List<Point>(data.Length);
        for (int i = 0; i < data.Length; i++)
        {
            double x = plot.Left + plot.Width * i / (data.Length - 1);
            double y = plot.Bottom - plot.Height * Math.Clamp(data[i] / maximum, 0, 1);
            coordinates.Add(new Point(x, y));
        }

        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(coordinates[0].X, plot.Bottom), true, true);
            foreach (var p in coordinates) ctx.LineTo(p, true, false);
            ctx.LineTo(new Point(coordinates[^1].X, plot.Bottom), true, false);
        }
        area.Freeze();
        dc.DrawGeometry(FillBrush, null, area);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(coordinates[0], false, false);
            foreach (var p in coordinates.Skip(1)) ctx.LineTo(p, true, false);
        }
        line.Freeze();
        dc.DrawGeometry(null, new Pen(LineBrush, 1.75), line);
    }

    private void DrawCaption(DrawingContext dc, string value, double x, double y)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = new FormattedText(value, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, LabelBrush, pixelsPerDip);
        dc.DrawText(text, new Point(x, y));
    }
}
