using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;

namespace Pauth40.Controls;

/// <summary>
/// Draws the two symmetrical arcs that frame the authentication code. The arc
/// sweep follows the remaining TOTP validity so the indicator visibly drains.
/// </summary>
public sealed class CircularCountdown : Grid
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction),
        typeof(double),
        typeof(CircularCountdown),
        new PropertyMetadata(1.0, OnVisualChanged));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush),
        typeof(Brush),
        typeof(CircularCountdown),
        new PropertyMetadata(
            new SolidColorBrush(Color.FromArgb(255, 16, 43, 66)),
            OnVisualChanged));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(CircularCountdown),
        new PropertyMetadata(true, OnVisualChanged));

    private readonly Ellipse _track;
    private readonly XamlPath _cyanGlow;
    private readonly XamlPath _blueGlow;
    private readonly XamlPath _cyanArc;
    private readonly XamlPath _blueArc;

    public CircularCountdown()
    {
        IsHitTestVisible = false;

        _track = new Ellipse
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = null
        };

        _cyanGlow = CreateArcPath(0.18);
        _blueGlow = CreateArcPath(0.17);
        _cyanArc = CreateArcPath(1.0);
        _blueArc = CreateArcPath(1.0);

        Children.Add(_track);
        Children.Add(_cyanGlow);
        Children.Add(_blueGlow);
        Children.Add(_cyanArc);
        Children.Add(_blueArc);

        SizeChanged += (_, _) => UpdateVisuals();
        Loaded += (_, _) => UpdateVisuals();
    }

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, Math.Clamp(value, 0.0, 1.0));
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

    private static XamlPath CreateArcPath(double opacity) => new()
    {
        Fill = null,
        StrokeThickness = 1,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Opacity = opacity,
        IsHitTestVisible = false
    };

    private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CircularCountdown)d).UpdateVisuals();

    private void UpdateVisuals()
    {
        double width = ActualWidth;
        double height = ActualHeight;
        double size = Math.Min(width, height);

        if (size <= 16 || double.IsNaN(width) || double.IsNaN(height))
            return;

        double thickness = Math.Max(5.0, size * 0.023);
        double radius = size / 2.0 - thickness / 2.0 - 2.0;

        _track.Width = radius * 2;
        _track.Height = radius * 2;
        _track.Stroke = TrackBrush;
        _track.StrokeThickness = thickness + 2;

        double fraction = Math.Clamp(Fraction, 0.0, 1.0);
        bool active = IsActive && fraction > 0.001;

        double halfSweep = 21.0 * fraction;

        if (!active || halfSweep < 1.2)
        {
            _cyanArc.Visibility = Visibility.Collapsed;
            _blueArc.Visibility = Visibility.Collapsed;
            _cyanGlow.Visibility = Visibility.Collapsed;
            _blueGlow.Visibility = Visibility.Collapsed;
            return;
        }

        _cyanArc.Visibility = Visibility.Visible;
        _blueArc.Visibility = Visibility.Visible;
        _cyanGlow.Visibility = Visibility.Visible;
        _blueGlow.Visibility = Visibility.Visible;

        var cyan = new SolidColorBrush(Color.FromArgb(255, 0, 217, 255));
        var blue = new SolidColorBrush(Color.FromArgb(255, 0, 123, 255));

        var cyanGlowBrush = new SolidColorBrush(Color.FromArgb(60, 0, 217, 255));
        var blueGlowBrush = new SolidColorBrush(Color.FromArgb(58, 0, 123, 255));

        _cyanArc.Stroke = cyan;
        _cyanArc.StrokeThickness = thickness;

        _blueArc.Stroke = blue;
        _blueArc.StrokeThickness = thickness;

        _cyanGlow.Stroke = cyanGlowBrush;
        _cyanGlow.StrokeThickness = thickness + 10;

        _blueGlow.Stroke = blueGlowBrush;
        _blueGlow.StrokeThickness = thickness + 10;

        var center = new Point(width / 2.0, height / 2.0);

        _cyanArc.Data = BuildArc(center, radius, 180.0 - halfSweep, halfSweep * 2);
        _blueArc.Data = BuildArc(center, radius, -halfSweep, halfSweep * 2);
        _cyanGlow.Data = _cyanArc.Data;
        _blueGlow.Data = _blueArc.Data;
    }

    private static Geometry BuildArc(
        Point center,
        double radius,
        double startAngle,
        double sweepAngle)
    {
        Point start = PointOnCircle(center, radius, startAngle);
        Point end = PointOnCircle(center, radius, startAngle + sweepAngle);

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false
        };

        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            IsLargeArc = Math.Abs(sweepAngle) > 180.0,
            SweepDirection = SweepDirection.Clockwise
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
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
