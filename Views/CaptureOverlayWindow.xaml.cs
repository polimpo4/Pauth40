using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingRectangle = System.Drawing.Rectangle;

namespace Pauth40.Views;

public partial class CaptureOverlayWindow : Window, IDisposable
{
    private readonly DrawingBitmap _screenshot;
    private Point _start;
    private Rect _selection;
    private bool _dragging;
    private bool _finished;

    public CaptureOverlayWindow(DrawingBitmap screenshot, DrawingRectangle virtualScreen)
    {
        _screenshot = screenshot;

        InitializeComponent();

        ScreenImage.Source = ToImageSource(screenshot);

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = Math.Max(1, SystemParameters.VirtualScreenWidth);
        Height = Math.Max(1, SystemParameters.VirtualScreenHeight);

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            UpdateDimming(Rect.Empty);
        };
    }

    public DrawingBitmap? CapturedImage { get; private set; }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_finished)
            return;

        _dragging = true;
        _selection = Rect.Empty;
        _start = ClampToWindow(e.GetPosition(Root));
        Root.CaptureMouse();
        UpdateSelection(new Rect(_start, _start));
        e.Handled = true;
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
            return;

        Point current = ClampToWindow(e.GetPosition(Root));
        var rect = new Rect(_start, current);
        UpdateSelection(rect);
        e.Handled = true;
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        Root.ReleaseMouseCapture();

        Rect rect = Normalize(new Rect(_start, ClampToWindow(e.GetPosition(Root))));

        if (rect.Width < 12 || rect.Height < 12)
        {
            _selection = Rect.Empty;
            UpdateDimming(Rect.Empty);
            e.Handled = true;
            return;
        }

        CompleteSelection(rect);
        e.Handled = true;
    }

    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        Cancel();
        e.Handled = true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
    }

    private void CompleteSelection(Rect rect)
    {
        double width = Math.Max(1, Root.ActualWidth);
        double height = Math.Max(1, Root.ActualHeight);

        int left = (int)Math.Round(rect.Left / width * _screenshot.Width);
        int top = (int)Math.Round(rect.Top / height * _screenshot.Height);
        int right = (int)Math.Round(rect.Right / width * _screenshot.Width);
        int bottom = (int)Math.Round(rect.Bottom / height * _screenshot.Height);

        left = Math.Clamp(left, 0, _screenshot.Width - 1);
        top = Math.Clamp(top, 0, _screenshot.Height - 1);
        right = Math.Clamp(right, left + 1, _screenshot.Width);
        bottom = Math.Clamp(bottom, top + 1, _screenshot.Height);

        var crop = new DrawingRectangle(left, top, right - left, bottom - top);

        try
        {
            CapturedImage = _screenshot.Clone(crop, PixelFormat.Format32bppArgb);
            _finished = true;
            DialogResult = true;
        }
        catch
        {
            _finished = true;
            DialogResult = false;
        }
    }

    private void Cancel()
    {
        if (_finished)
            return;

        _finished = true;
        DialogResult = false;
    }

    private void UpdateSelection(Rect rect)
    {
        rect = Normalize(rect);
        _selection = rect;

        if (rect.Width < 1 || rect.Height < 1)
        {
            UpdateDimming(Rect.Empty);
            return;
        }

        UpdateDimming(rect);

        SelectionFill.Visibility = Visibility.Visible;
        SelectionBorder.Visibility = Visibility.Visible;

        Canvas.SetLeft(SelectionFill, rect.Left);
        Canvas.SetTop(SelectionFill, rect.Top);
        SelectionFill.Width = rect.Width;
        SelectionFill.Height = rect.Height;

        Canvas.SetLeft(SelectionBorder, rect.Left);
        Canvas.SetTop(SelectionBorder, rect.Top);
        SelectionBorder.Width = rect.Width;
        SelectionBorder.Height = rect.Height;

        UpdateSizeChip(rect);
    }

    private void UpdateDimming(Rect clear)
    {
        double width = Math.Max(1, Root.ActualWidth);
        double height = Math.Max(1, Root.ActualHeight);
        Rect rect = clear.IsEmpty ? Rect.Empty : Normalize(clear);

        if (rect.IsEmpty)
        {
            SetRect(DimTop, 0, 0, width, height);
            SetRect(DimBottom, 0, 0, 0, 0);
            SetRect(DimLeft, 0, 0, 0, 0);
            SetRect(DimRight, 0, 0, 0, 0);
            SelectionFill.Visibility = Visibility.Collapsed;
            SelectionBorder.Visibility = Visibility.Collapsed;
            SizeChip.Visibility = Visibility.Collapsed;
            return;
        }

        SetRect(DimTop, 0, 0, width, rect.Top);
        SetRect(DimBottom, 0, rect.Bottom, width, height - rect.Bottom);
        SetRect(DimLeft, 0, rect.Top, rect.Left, rect.Height);
        SetRect(DimRight, rect.Right, rect.Top, width - rect.Right, rect.Height);
    }

    private void UpdateSizeChip(Rect rect)
    {
        double width = Math.Max(1, Root.ActualWidth);
        double height = Math.Max(1, Root.ActualHeight);

        int pixelWidth = Math.Max(1, (int)Math.Round(rect.Width / width * _screenshot.Width));
        int pixelHeight = Math.Max(1, (int)Math.Round(rect.Height / height * _screenshot.Height));

        SizeText.Text = $"{pixelWidth} \u00D7 {pixelHeight} px";
        SizeChip.Visibility = Visibility.Visible;
        SizeChip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        double chipWidth = SizeChip.DesiredSize.Width;
        double chipHeight = SizeChip.DesiredSize.Height;

        double left = rect.Right + 10;
        if (left + chipWidth > width - 8)
            left = Math.Max(8, rect.Left - chipWidth - 10);

        double top = rect.Bottom + 10;
        if (top + chipHeight > height - 8)
            top = Math.Max(8, rect.Top - chipHeight - 10);

        Canvas.SetLeft(SizeChip, left);
        Canvas.SetTop(SizeChip, top);
    }

    private Point ClampToWindow(Point point)
    {
        return new Point(
            Math.Clamp(point.X, 0, Math.Max(0, Root.ActualWidth)),
            Math.Clamp(point.Y, 0, Math.Max(0, Root.ActualHeight)));
    }

    private static Rect Normalize(Rect rect)
    {
        double x = Math.Min(rect.Left, rect.Right);
        double y = Math.Min(rect.Top, rect.Bottom);
        double width = Math.Abs(rect.Width);
        double height = Math.Abs(rect.Height);
        return new Rect(x, y, width, height);
    }

    private static void SetRect(FrameworkElement element, double left, double top, double width, double height)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
    }

    private static BitmapSource ToImageSource(DrawingBitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public void Dispose()
    {
        _screenshot.Dispose();
    }
}
