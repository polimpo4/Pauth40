using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Pauth40.Localization;
using Pauth40.Services;

namespace Pauth40.Views;

/// <summary>
/// Mandatory update prompt. It cannot be dismissed while a newer release is
/// available; the user either updates or exits the application.
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _info;
    private readonly UpdateService _service;
    private readonly CancellationTokenSource _cts = new();

    private bool _allowClose;
    private bool _busy;

    public UpdateWindow(UpdateInfo info, UpdateService service)
    {
        _info = info ?? throw new ArgumentNullException(nameof(info));
        _service = service ?? throw new ArgumentNullException(nameof(service));

        InitializeComponent();

        CurrentVersionText.Text = "v" + info.CurrentVersion;
        LatestVersionText.Text = "v" + info.LatestVersion;
        ReleaseNotesText.Text = info.ReleaseNotes ?? Loc.T("update_no_notes");
        VersionText.Text = AppVersion.Display;

        if (!info.HasAsset)
        {
            StatusText.Text = Loc.T("update_err_no_asset");
            UpdateButtonText.Text = Loc.T("update_open_release");
            ReleaseLink.Visibility = Visibility.Visible;
        }

        Loaded += OnLoaded;
        Closed += (_, _) => _cts.Dispose();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Rect workArea = SystemParameters.WorkArea;

        Width = Math.Min(660, Math.Max(MinWidth, workArea.Width - 80));
        Height = Math.Min(660, Math.Max(MinHeight, workArea.Height - 48));

        Activate();
        UpdateButton.Focus();
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (!_info.HasAsset)
        {
            OpenReleasePage();
            return;
        }

        _busy = true;

        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        UpdateButton.Visibility = Visibility.Collapsed;
        ReleaseLink.Visibility = Visibility.Collapsed;
        StatusText.Text = Loc.T("update_downloading");

        string destination = Path.Combine(
            Path.GetTempPath(),
            $"Pauth40-update-{_info.LatestVersion}-{Guid.NewGuid():N}.exe");

        try
        {
            var progress = new Progress<UpdateProgress>(value =>
            {
                DownloadProgress.Value = value.Percent;
                StatusText.Text = Loc.Format("update_progress", (int)Math.Round(value.Percent));
            });

            await _service.DownloadAsync(_info, destination, progress, _cts.Token);

            StatusText.Text = Loc.T("update_installing");
            DownloadProgress.Value = 100;
            ExitButton.IsEnabled = false;
            UpdateButton.IsEnabled = false;

            _service.LaunchUpdater(destination);

            _allowClose = true;

            // Give the message one frame to render before the process exits.
            await Task.Delay(250);

            Application.Current.Shutdown();
        }
        catch (UpdateException exception)
        {
            TryDelete(destination);
            ShowFailure(Loc.T(exception.Code));
        }
        catch (OperationCanceledException)
        {
            TryDelete(destination);
            ShowFailure(Loc.T("update_err_download"));
        }
        catch (Exception exception)
        {
            TryDelete(destination);
            ShowFailure(exception.Message);
        }
    }

    private void ShowFailure(string detail)
    {
        _busy = false;

        DownloadProgress.Visibility = Visibility.Collapsed;
        UpdateButton.Visibility = Visibility.Visible;
        UpdateButton.IsEnabled = true;
        UpdateButtonText.Text = Loc.T("update_retry");
        ExitButton.IsEnabled = true;
        ReleaseLink.Visibility = Visibility.Visible;
        StatusText.Text = Loc.Format("update_failed", detail);
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        _cts.Cancel();
        Application.Current.Shutdown();
    }

    private void OpenRelease_Click(object sender, RoutedEventArgs e) =>
        OpenReleasePage();

    private void OpenReleasePage()
    {
        try
        {
            if (Uri.TryCreate(_info.ReleaseUrl, UriKind.Absolute, out Uri? uri))
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // The link is a convenience; a broken URL must not crash the app.
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
            e.Cancel = true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // A leftover temporary file is harmless.
        }
    }
}
