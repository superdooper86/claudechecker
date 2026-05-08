using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ClaudeCheckerWindows.Controls;

public class SparklineControl : FrameworkElement
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(IList<double>), typeof(SparklineControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Color), typeof(SparklineControl),
            new FrameworkPropertyMetadata(Colors.Orange, FrameworkPropertyMetadataOptions.AffectsRender));

    public IList<double>? Data      { get => (IList<double>?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public Color          LineColor { get => (Color)GetValue(LineColorProperty);      set => SetValue(LineColorProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var data = Data;
        if (data == null || data.Count < 1) return;

        var plot = data.Count == 1 ? new[] { data[0], data[0] } : [.. data];

        var w    = RenderSize.Width;
        var h    = RenderSize.Height;
        var minV = double.MaxValue;
        var maxV = double.MinValue;
        foreach (var v in plot) { if (v < minV) minV = v; if (v > maxV) maxV = v; }
        var range = System.Math.Max(maxV - minV, 1);

        Point Pt(int i) => new(
            i / (double)(plot.Length - 1) * w,
            h - (plot[i] - minV) / range * (h - 6) - 3);

        var fill = new StreamGeometry();
        using (var ctx = fill.Open())
        {
            ctx.BeginFigure(new Point(0, h), true, true);
            for (var i = 0; i < plot.Length; i++) ctx.LineTo(Pt(i), true, false);
            ctx.LineTo(new Point(w, h), true, false);
        }
        fill.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(26, LineColor.R, LineColor.G, LineColor.B)), null, fill);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(Pt(0), false, false);
            for (var i = 1; i < plot.Length; i++) ctx.LineTo(Pt(i), true, false);
        }
        line.Freeze();
        dc.DrawGeometry(null,
            new System.Windows.Media.Pen(new SolidColorBrush(LineColor), 1.5) { LineJoin = PenLineJoin.Round },
            line);
    }
}
