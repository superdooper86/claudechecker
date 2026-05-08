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

    // Arc sweeps 270° starting at 135° (bottom-left to bottom-right, like macOS version)
    private const double StartAngleDeg = 135;
    private const double SweepDeg      = 270;
    private const double StrokeWidth   = 6;

    protected override void OnRender(DrawingContext dc)
    {
        var cx     = RenderSize.Width  / 2;
        var cy     = RenderSize.Height / 2;
        var radius = Math.Min(cx, cy) - StrokeWidth / 2 - 1;

        // Track
        DrawArc(dc, cx, cy, radius, StartAngleDeg, SweepDeg,
            new Pen(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), StrokeWidth));

        // Fill
        if (Percent > 0)
        {
            var sweep = SweepDeg * (Percent / 100.0);
            DrawArc(dc, cx, cy, radius, StartAngleDeg, sweep, new Pen(Accent, StrokeWidth));
        }

        var dpi  = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Percent label
        var pctText = new FormattedText(
            $"{(int)Math.Round(Percent)}%",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            14, Brushes.White, dpi);

        dc.DrawText(pctText, new Point(cx - pctText.Width / 2, cy - pctText.Height - 1));

        // Sub-label
        var sub = new FormattedText(
            "Updated\nnow",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            7, new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), dpi);
        sub.TextAlignment = TextAlignment.Center;

        dc.DrawText(sub, new Point(cx - sub.Width / 2, cy + 2));
    }

    private static void DrawArc(DrawingContext dc, double cx, double cy, double r,
        double startDeg, double sweepDeg, Pen pen)
    {
        if (sweepDeg <= 0) return;

        var start    = PointOnCircle(cx, cy, r, startDeg);
        var end      = PointOnCircle(cx, cy, r, startDeg + sweepDeg);
        var isLarge  = sweepDeg > 180;

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
