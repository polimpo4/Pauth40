using System.Windows;
using System.Windows.Media;

namespace Pauth40.Controls;

/// <summary>
/// Draws the two symmetrical arcs that frame the authentication code. The arc
/// sweep follows the remaining TOTP validity so the indicator visibly drains.
/// </summary>
public sealed class CircularCountdown : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction),
        typeof(double),
        typeof(CircularCountdown),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, null, CoerceFraction));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(CircularCountdown),
        new FrameworkPropertyMetadata(
            new SolidColorBrush(Color.FromRgb(16, 48, 72)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(CircularCountdown),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private static object CoerceFraction(DependencyObject d, object baseValue)
    {
        return Math.Clamp((double)baseValue, 0.0, 1.0);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 16)
            return;

        var center = new Point(ActualWidth / 2.0, ActualHeight / 2.0);
        double thickness = Math.Max(5.0, size * 0.023);
        double radius = size / 2.0 - thickness / 2.0 - 2.0;

        // Soft dark groove makes the dark interior read as a glass dial.
        var trackPen = new Pen(TrackBrush, thickness + 2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        dc.DrawEllipse(null, trackPen, center, radius, radius);

        if (!IsActive || Fraction <= 0.001)
            return;

        // At 24 of 30 seconds the reference shows roughly a 32 degree arc on
        // each side. Keep the same proportion across the whole TOTP period.
        double halfSweep = 21.0 * Fraction;
        if (halfSweep < 1.2)
            return;

        var cyan = new SolidColorBrush(Color.FromRgb(0, 217, 255));
        var blue = new SolidColorBrush(Color.FromRgb(0, 123, 255));
        var cyanGlow = new SolidColorBrush(Color.FromArgb(46, 0, 217, 255));
        var blueGlow = new SolidColorBrush(Color.FromArgb(44, 0, 123, 255));

        DrawArc(dc, center, radius, 180.0 - halfSweep, 180.0 + halfSweep, thickness, cyan, cyanGlow);
        DrawArc(dc, center, radius, -halfSweep, halfSweep, thickness, blue, blueGlow);
    }

    private static void DrawArc(
        DrawingContext dc,
        Point center,
        double radius,
        double startAngle,
        double endAngle,
        double thickness,
        Brush stroke,
        Brush glow)
    {
        var geometry = BuildArc(center, radius, startAngle, endAngle - startAngle);

        var glowPen = new Pen(glow, thickness + 10)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        dc.DrawGeometry(null, glowPen, geometry);

        var pen = new Pen(stroke, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        dc.DrawGeometry(null, pen, geometry);
    }

    private static Geometry BuildArc(Point center, double radius, double startAngle, double sweepAngle)
    {
        Point start = PointOnCircle(center, radius, startAngle);
        Point end = PointOnCircle(center, radius, startAngle + sweepAngle);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(
                end,
                new Size(radius, radius),
                0,
                Math.Abs(sweepAngle) > 180.0,
                SweepDirection.Clockwise,
                true,
                false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180.0;
        return new Point(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
