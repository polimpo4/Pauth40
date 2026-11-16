using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pauth40.Localization;
using Pauth40.Services;
using Windows.System;

namespace Pauth40.Views;

/// <summary>
/// Mandatory update prompt. It cannot be dismissed while a newer release is
/// available; the user either updates or exits the application.
/// </summary>
public sealed partial class UpdateView : UserControl
{
    private readonly UpdateInfo _info;
    private readonly UpdateService _service;
    private readonly CancellationTokenSource _cts = new();

    private bool _busy;

    public UpdateView(UpdateInfo info, UpdateService service)
    {
        _info = info ?? throw new ArgumentNullException(nameof(info));
        _service = service ?? throw new ArgumentNullException(nameof(service));

        InitializeComponent();

        CurrentVersionText.Text = "v" + info.CurrentVersion;
        LatestVersionText.Text = "v" + info.LatestVersion;

        string notes = ReleaseNotes.Select(info.ReleaseNotes, Loc.CurrentLanguageCode);
        ReleaseNotesText.Text = notes.Length > 0 ? notes : Loc.T("update_no_notes");

        VersionText.Text = AppVersion.Display;

        if (!info.HasAsset)
        {
            StatusText.Text = Loc.T("update_err_no_asset");
            UpdateButtonText.Text = Loc.T("update_open_release");
            ReleaseLink.Visibility = Visibility.Visible;
        }

        Unloaded += (_, _) => _cts.Dispose();
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (!_info.HasAsset)
        {
            await OpenReleasePageAsync();
            return;
        }

        _busy = true;

        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        UpdateButton.IsEnabled = false;
        ReleaseLink.Visibility = Visibility.Collapsed;
        StatusText.Text = Loc.T("update_downloading");

        string destination = Path.Combine(
            Path.GetTempPath(),
            $"Pauth40-update-{_info.LatestVersion}-{Guid.NewGuid():N}{DestinationExtension}");

        try
        {
            var progress = new Progress<UpdateProgress>(value =>
            {
                DownloadProgress.Value = value.Percent;
                StatusText.Text = Loc.Format(
                    "update_progress",
                    (int)Math.Round(value.Percent));
            });

            await _service.DownloadAsync(_info, destination, progress, _cts.Token);

            StatusText.Text = Loc.T("update_installing");
            DownloadProgress.Value = 100;
            ExitButton.IsEnabled = false;

            _service.LaunchUpdater(destination);

            // Give the message one frame to render before the process exits.
            await Task.Delay(250);

            App.ExitApplication();
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

    private static string DestinationExtension
    {
        get
        {
#if PAUTH40_WINDOWS
            return ".exe";
#elif PAUTH40_DESKTOP
            return OperatingSystem.IsMacOS() ? ".dmg" : ".AppImage";
#else
            return ".bin";
#endif
        }
    }

    private void ShowFailure(string detail)
    {
        _busy = false;

        DownloadProgress.Visibility = Visibility.Collapsed;
        UpdateButton.IsEnabled = true;
        UpdateButtonText.Text = Loc.T("update_retry");
        ExitButton.IsEnabled = true;
        ReleaseLink.Visibility = Visibility.Visible;
        StatusText.Text = Loc.Format("update_failed", detail);
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        App.ExitApplication();
    }

    private async void OpenRelease_Click(object sender, RoutedEventArgs e) =>
        await OpenReleasePageAsync();

    private async Task OpenReleasePageAsync()
    {
        try
        {
            if (Uri.TryCreate(_info.ReleaseUrl, UriKind.Absolute, out Uri? uri))
                await Launcher.LaunchUriAsync(uri);
        }
        catch
        {
            // The link is a convenience; a broken URL must not crash the app.
        }
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
