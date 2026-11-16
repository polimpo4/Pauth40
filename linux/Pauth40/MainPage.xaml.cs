using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Pauth40.Localization;
using Pauth40.Services;
using Pauth40.ViewModels;
using Pauth40.Views;
using Windows.System;

namespace Pauth40;

/// <summary>
/// Single window shell. It hosts the security gate, the authenticator, the
/// settings page, the mandatory update prompt and the screen capture overlay,
/// so every Uno target shares the same navigation flow.
/// </summary>
public sealed partial class MainPage : Page
{
    private static readonly string[] SupportedImageExtensions =
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"
    };

    private readonly MainViewModel _viewModel;
    private readonly SecurityService _security;
    private readonly EmailService _email;
    private readonly CaptureService _captureService;
    private readonly UpdateService _updateService;
    private readonly DispatcherTimer _toastTimer;

    private bool _updateShown;
    private bool _capturing;

    public MainPage()
    {
        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        _security = App.Services.GetRequiredService<SecurityService>();
        _email = App.Services.GetRequiredService<EmailService>();
        _captureService = App.Services.GetRequiredService<CaptureService>();
        _updateService = App.Services.GetRequiredService<UpdateService>();

        InitializeComponent();

        VersionText.Text = AppVersion.Display;

        _viewModel.CaptureProvider = CaptureAsync;
        _viewModel.ToastRequested += OnToastRequested;

        _toastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.8)
        };
        _toastTimer.Tick += OnToastTick;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

#if PAUTH40_DESKTOP || PAUTH40_WINDOWS
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDrop;
        CloseButton.Visibility = Visibility.Visible;
#endif
    }

    // ---------------------------------------------------------------- lifecycle

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Focus(FocusState.Programmatic);
        ShowSecurity();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _toastTimer.Stop();
        _viewModel.ToastRequested -= OnToastRequested;
    }

    // ---------------------------------------------------------------- navigation

    private void ShowSecurity()
    {
        var view = new SecurityView(_security, _email);
        view.Authenticated += OnAuthenticated;
        SetContent(view, back: false, settings: false);
    }

    private async void OnAuthenticated(object? sender, EventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch
        {
            ShowToast(Loc.T("main_vault_load_error"));
        }

        ShowMain();

        _ = CheckForUpdateAsync();
    }

    private void ShowMain()
    {
        var view = new MainView(_viewModel);
        SetContent(view, back: false, settings: true);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var view = new SettingsView(_viewModel, _security, _email);

        view.AccountsChanged += (_, _) => _viewModel.ReloadAccounts();

        SetContent(view, back: true, settings: false);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (ContentHost.Content is SettingsView)
            ShowMain();
    }

    private void SetContent(UserControl view, bool back, bool settings)
    {
        ContentHost.Content = view;
        BackButton.Visibility = back ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            App.Instance?.Close();
        }
        catch
        {
            App.ExitApplication();
        }
    }

    // ---------------------------------------------------------------- updates

    private Task CheckForUpdateAsync()
    {
#if PAUTH40_WINDOWS
        return CheckForUpdateCoreAsync();
#elif PAUTH40_DESKTOP
        // The self-updater replaces a Windows executable or a Linux AppImage.
        // macOS, mobile and WebAssembly use the store/manual download instead.
        return OperatingSystem.IsMacOS()
            ? Task.CompletedTask
            : CheckForUpdateCoreAsync();
#else
        return Task.CompletedTask;
#endif
    }

    private async Task CheckForUpdateCoreAsync()
    {
        if (_updateShown)
            return;

        try
        {
            UpdateInfo? info = await _updateService.CheckForUpdateAsync();
            if (info is null || _updateShown)
                return;

            _updateShown = true;

            var view = new UpdateView(info, _updateService);
            SetContent(view, back: false, settings: false);
        }
        catch
        {
            // An update check must never interrupt the authenticator.
        }
    }

    // ---------------------------------------------------------------- capture

    private async Task<byte[]?> CaptureAsync()
    {
        if (_capturing)
            return null;

        _capturing = true;

        try
        {
            byte[]? screenshot = await _captureService.CaptureScreenAsync();

            if (screenshot is null)
                return await FilePickerService.PickImageAsync();

            var view = new CaptureView(screenshot);

            OverlayHost.Child = view;
            OverlayHost.Visibility = Visibility.Visible;

            try
            {
                return await view.Result;
            }
            finally
            {
                OverlayHost.Visibility = Visibility.Collapsed;
                OverlayHost.Child = null;
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            _capturing = false;
        }
    }

    // ---------------------------------------------------------------- drag/drop

#if PAUTH40_DESKTOP || PAUTH40_WINDOWS
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(
            Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)
            ? Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
            : Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;

        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(
                Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();

        string[] paths = items
            .OfType<Windows.Storage.StorageFile>()
            .Where(file => SupportedImageExtensions.Contains(
                Path.GetExtension(file.Name), StringComparer.OrdinalIgnoreCase))
            .Select(file => file.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

        if (paths.Length > 0)
            await _viewModel.ImportFilesAsync(paths);

        e.Handled = true;
    }
#endif

    // ---------------------------------------------------------------- toast

    private void OnToastRequested(object? sender, string message) => ShowToast(message);

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        ToastHost.Visibility = Visibility.Visible;

        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OnToastTick(object? sender, object e)
    {
        _toastTimer.Stop();
        HideToast();
    }

    private void HideToast()
    {
        ToastHost.Visibility = Visibility.Collapsed;
        ToastText.Text = string.Empty;
    }

    // ---------------------------------------------------------------- keyboard

    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
            return;

        if (OverlayHost.Visibility == Visibility.Visible)
            return;

        if (ToastHost.Visibility == Visibility.Visible)
        {
            _toastTimer.Stop();
            HideToast();
            e.Handled = true;
        }
    }
}
