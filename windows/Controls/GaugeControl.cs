using Brush = System.Windows.Media.Brush;
using Pen   = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ClaudeCheckerWindows.Controls;

public class GaugeControl : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty =
        DependencyProperty.Register(nameof(Percent), typeof(double), typeof(GaugeControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(nameof(Accent), typeof(Brush), typeof(GaugeControl),
            new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Percent { get => (double)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public Brush  Accent  { get => (Brush)GetValue(AccentProperty);   set => SetValue(AccentProperty, value); }

    public GaugeControl()
    {
        ThemeManager.ThemeChanged += InvalidateVisual;
    }

    private const double StartAngleDeg = 135;
    private const double SweepDeg      = 270;
    private const double StrokeWidth   = 6;

    protected override void OnRender(DrawingContext dc)
    {
        var cx     = RenderSize.Width  / 2;
        var cy     = RenderSize.Height / 2;
        var radius = Math.Min(cx, cy) - StrokeWidth / 2 - 1;

        var trackColor = ThemeManager.IsDark
            ? Color.FromArgb(40, 255, 255, 255)
            : Color.FromArgb(30, 0, 0, 0);
        DrawArc(dc, cx, cy, radius, StartAngleDeg, SweepDeg,
            new Pen(new SolidColorBrush(trackColor), StrokeWidth));

        if (Percent > 0)
            DrawArc(dc, cx, cy, radius, StartAngleDeg, SweepDeg * (Percent / 100.0),
                new Pen(Accent, StrokeWidth));

        var dpi       = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var textColor = ThemeManager.IsDark ? Color.FromRgb(0xF5, 0xF5, 0xF5) : Color.FromRgb(0x1C, 0x1C, 0x1C);

        var pctText = new FormattedText(
            $"{(int)Math.Round(Percent)}%",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            18, new SolidColorBrush(textColor), dpi);

        dc.DrawText(pctText, new Point(cx - pctText.Width / 2, cy - pctText.Height / 2));
    }

    private static void DrawArc(DrawingContext dc, double cx, double cy, double r,
        double startDeg, double sweepDeg, Pen pen)
    {
        if (sweepDeg <= 0) return;
        var start   = PointOnCircle(cx, cy, r, startDeg);
        var end     = PointOnCircle(cx, cy, r, startDeg + sweepDeg);
        var isLarge = sweepDeg > 180;

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, isLarge, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private static Point PointOnCircle(double cx, double cy, double r, double deg)
    {
        var rad = deg * Math.PI / 180;
        return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
    }
}
