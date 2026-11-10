using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pauth40.Localization;
using Pauth40.Services;
using Pauth40.ViewModels;
using Pauth40.Views;

namespace Pauth40;

public partial class MainWindow : Window
{
    private static readonly string[] SupportedImageExtensions =
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"
    };

    private readonly MainViewModel _viewModel;
    private readonly CaptureService _captureService;
    private readonly SecurityService? _securityService;
    private readonly EmailService? _emailService;
    private readonly DispatcherTimer _toastTimer;

    public MainWindow(
        MainViewModel viewModel,
        CaptureService captureService,
        SecurityService? securityService = null,
        EmailService? emailService = null)
    {
        _viewModel = viewModel;
        _captureService = captureService;
        _securityService = securityService;
        _emailService = emailService;

        InitializeComponent();

        DataContext = viewModel;
        viewModel.CaptureProvider = () => _captureService.CaptureAsync(this);
        viewModel.ToastRequested += OnToastRequested;

        _toastTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(2800)
        };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            HideToast();
        };

        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1120, Math.Max(MinWidth, workArea.Width - 32));
        Height = Math.Min(1008, Math.Max(MinHeight, workArea.Height - 32));

        Loaded += OnLoaded;
        Closed += OnClosed;
        StateChanged += OnStateChanged;
        SourceInitialized += OnSourceInitialized;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch
        {
            ShowToast(Loc.T("main_vault_load_error"));
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        _viewModel.ToastRequested -= OnToastRequested;
        _viewModel.Dispose();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        WindowBorder.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(12);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        NativeMethods.UseModernWindowFrame(handle);
        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WM_GETMINMAXINFO)
        {
            var info = Marshal.PtrToStructure<NativeMethods.MinMaxInfo>(lParam);
            NativeMethods.ConstrainMaximizedBounds(hwnd, ref info);
            Marshal.StructureToPtr(info, lParam, true);
        }

        return IntPtr.Zero;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>((DependencyObject)e.OriginalSource) is not null)
            return;

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // The mouse was released between the event and DragMove.
            }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_securityService is null || _emailService is null)
            return;

        var window = new SettingsWindow(_viewModel, _securityService, _emailService)
        {
            Owner = this
        };

        window.AccountsChanged += (_, _) => _viewModel.ReloadAccounts();
        window.ShowDialog();
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (_viewModel.CopyCommand.CanExecute(null))
                _viewModel.CopyCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && ToastHost.Visibility == Visibility.Visible)
        {
            _toastTimer.Stop();
            HideToast();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _viewModel.RemoveAccountCommand.CanExecute(null))
        {
            _viewModel.RemoveAccountCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetImagePaths(e.Data, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetImagePaths(e.Data, out var paths))
            return;

        await _viewModel.ImportFilesAsync(paths);
        e.Handled = true;
    }

    private static bool TryGetImagePaths(IDataObject data, out string[] paths)
    {
        paths = Array.Empty<string>();

        if (!data.GetDataPresent(DataFormats.FileDrop) ||
            data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return false;
        }

        paths = files
            .Where(file => SupportedImageExtensions.Contains(
                Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return paths.Length > 0;
    }

    private void OnToastRequested(object? sender, string message) => ShowToast(message);

    private void ShowToast(string message)
    {
        ToastText.Text = message;

        ToastHost.BeginAnimation(OpacityProperty, null);
        ToastHost.Opacity = 0;
        ToastHost.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        ToastHost.BeginAnimation(OpacityProperty, fadeIn);

        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void HideToast()
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (_, _) =>
        {
            ToastHost.Visibility = Visibility.Collapsed;
            ToastHost.Opacity = 0;
        };

        ToastHost.BeginAnimation(OpacityProperty, fadeOut);
    }

    public void RenderPreview(string outputPath)
    {
        UpdateLayout();
        RootGrid.UpdateLayout();

        double width = RootGrid.ActualWidth;
        double height = RootGrid.ActualHeight;
        if (width < 10 || height < 10)
            throw new InvalidOperationException("The window has no layout to render.");

        var target = new RenderTargetBitmap(
            (int)Math.Ceiling(width),
            (int)Math.Ceiling(height),
            96,
            96,
            PixelFormats.Pbgra32);

        target.Render(RootGrid);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;

            DependencyObject? parent;
            try
            {
                parent = VisualTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
                parent = LogicalTreeHelper.GetParent(current);
            }

            current = parent;
        }

        return null;
    }
}
