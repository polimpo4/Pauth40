using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp;
using Windows.Foundation;
using Windows.System;

namespace Pauth40.Views;

/// <summary>
/// Full page screen capture overlay. It shows the PNG taken by the platform
/// capture service and lets the user drag a rectangle around the QR code. The
/// cropped selection is returned as PNG bytes through <see cref="Result"/>.
/// </summary>
public sealed partial class CaptureView : UserControl
{
    private readonly TaskCompletionSource<byte[]?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private SKBitmap? _bitmap;
    private Point _start;
    private bool _dragging;
    private bool _finished;

    public CaptureView(byte[] screenshot)
    {
        InitializeComponent();

        VersionText.Text = AppVersion.Display;

        try
        {
            _bitmap = SKBitmap.Decode(screenshot);
        }
        catch
        {
            _bitmap = null;
        }

        _ = LoadImageAsync(screenshot);

        Loaded += OnLoaded;
        Unloaded += (_, _) => Complete(null);
    }

    /// <summary>Completes with the cropped PNG, or null when cancelled.</summary>
    public Task<byte[]?> Result => _completion.Task;

    private async Task LoadImageAsync(byte[] screenshot)
    {
        try
        {
            using var stream = new MemoryStream(screenshot, writable: false);
            var bitmap = new BitmapImage();

            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());

            ScreenImage.Source = bitmap;
        }
        catch
        {
            // The selection can still be made; the crop comes from SkiaSharp.
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Focus(FocusState.Programmatic);
        UpdateDimming(Rect.Empty);
    }

    // ---------------------------------------------------------------- pointer

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_finished)
            return;

        var point = e.GetCurrentPoint(Root);

        if (point.Properties.IsRightButtonPressed)
        {
            Complete(null);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;

        _dragging = true;
        _start = ClampToImage(point.Position);
        UpdateSelection(new Rect(_start, _start));
        Root.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _finished)
            return;

        Point current = ClampToImage(e.GetCurrentPoint(Root).Position);
        UpdateSelection(new Rect(_start, current));
        e.Handled = true;
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        Root.ReleasePointerCapture(e.Pointer);

        Rect rect = Normalize(new Rect(_start, ClampToImage(e.GetCurrentPoint(Root).Position)));

        if (rect.Width < 12 || rect.Height < 12)
        {
            UpdateSelection(Rect.Empty);
            e.Handled = true;
            return;
        }

        CompleteSelection(rect);
        e.Handled = true;
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
            return;

        Complete(null);
        e.Handled = true;
    }

    // ---------------------------------------------------------------- cropping

    private void CompleteSelection(Rect rect)
    {
        byte[]? result = null;

        try
        {
            result = Crop(rect);
        }
        catch
        {
            result = null;
        }

        Complete(result);
    }

    private byte[]? Crop(Rect selection)
    {
        if (_bitmap is null || _bitmap.Width <= 0 || _bitmap.Height <= 0)
            return null;

        Rect image = ImageRect;
        if (image.Width < 1 || image.Height < 1)
            return null;

        double scaleX = _bitmap.Width / image.Width;
        double scaleY = _bitmap.Height / image.Height;

        int left = ClampToPixels((int)Math.Round((selection.Left - image.Left) * scaleX), _bitmap.Width);
        int top = ClampToPixels((int)Math.Round((selection.Top - image.Top) * scaleY), _bitmap.Height);
        int right = ClampToPixels((int)Math.Round((selection.Right - image.Left) * scaleX), _bitmap.Width);
        int bottom = ClampToPixels((int)Math.Round((selection.Bottom - image.Top) * scaleY), _bitmap.Height);

        if (right <= left || bottom <= top)
            return null;

        using var cropped = new SKBitmap(
            new SKImageInfo(right - left, bottom - top, SKColorType.Bgra8888, SKAlphaType.Premul));

        using (var canvas = new SKCanvas(cropped))
        {
            canvas.DrawBitmap(
                _bitmap,
                new SKRect(left, top, right, bottom),
                new SKRect(0, 0, cropped.Width, cropped.Height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            canvas.Flush();
        }

        using var encoded = SKImage.FromBitmap(cropped);
        using var data = encoded.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }

    private static int ClampToPixels(int value, int maximum) =>
        Math.Clamp(value, 0, Math.Max(0, maximum));

    // ---------------------------------------------------------------- overlay

    private void UpdateSelection(Rect rect)
    {
        rect = Normalize(rect);

        if (rect.Width < 1 || rect.Height < 1)
        {
            UpdateDimming(Rect.Empty);
            return;
        }

        UpdateDimming(rect);

        SelectionFill.Visibility = Visibility.Visible;
        SelectionBorder.Visibility = Visibility.Visible;

        SetRect(SelectionFill, rect.Left, rect.Top, rect.Width, rect.Height);
        SetRect(SelectionBorder, rect.Left, rect.Top, rect.Width, rect.Height);

        UpdateSizeChip(rect);
    }

    private void UpdateDimming(Rect clear)
    {
        double width = Math.Max(1, Root.ActualWidth);
        double height = Math.Max(1, Root.ActualHeight);
        Rect rect = clear.Width < 1 || clear.Height < 1 ? Rect.Empty : Normalize(clear);

        if (rect.Width < 1 || rect.Height < 1)
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
        if (_bitmap is null)
            return;

        double width = Math.Max(1, Root.ActualWidth);
        double height = Math.Max(1, Root.ActualHeight);
        Rect image = ImageRect;

        if (image.Width < 1 || image.Height < 1)
            return;

        int pixelWidth = Math.Max(
            1,
            (int)Math.Round(rect.Width / image.Width * _bitmap.Width));
        int pixelHeight = Math.Max(
            1,
            (int)Math.Round(rect.Height / image.Height * _bitmap.Height));

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

    private Rect ImageRect
    {
        get
        {
            double width = Math.Max(1, Root.ActualWidth);
            double height = Math.Max(1, Root.ActualHeight);

            if (_bitmap is null || _bitmap.Width <= 0 || _bitmap.Height <= 0)
                return new Rect(0, 0, width, height);

            double scale = Math.Min(width / _bitmap.Width, height / _bitmap.Height);
            double imageWidth = Math.Max(1, _bitmap.Width * scale);
            double imageHeight = Math.Max(1, _bitmap.Height * scale);

            return new Rect(
                (width - imageWidth) / 2,
                (height - imageHeight) / 2,
                imageWidth,
                imageHeight);
        }
    }

    private Point ClampToImage(Point point)
    {
        Rect image = ImageRect;

        return new Point(
            Math.Clamp(point.X, image.Left, image.Right),
            Math.Clamp(point.Y, image.Top, image.Bottom));
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

    private void Complete(byte[]? result)
    {
        if (_finished)
            return;

        _finished = true;
        _completion.TrySetResult(result);
    }
}
