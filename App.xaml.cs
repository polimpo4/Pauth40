using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Pauth40.Localization;
using Pauth40.Services;
using Pauth40.ViewModels;
using Pauth40.Views;

namespace Pauth40;

public partial class App : Application
{
    private ServiceProvider? _provider;
    private readonly CancellationTokenSource _updateCts = new();
    private int _updateWindowShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Load the saved interface language (or detect it from Windows) before
        // any window is created. The security gate also lets the user change it.
        LocalizationManager.Instance.Initialize();

        var services = new ServiceCollection();
        services.AddSingleton<AccountStore>();
        services.AddSingleton<SecurityService>();
        services.AddSingleton<EmailService>();
        services.AddSingleton<ClipboardService>();
        services.AddSingleton<CaptureService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<MainViewModel>();
        _provider = services.BuildServiceProvider();

        int renderIndex = Array.IndexOf(e.Args, "--render");
        if (renderIndex >= 0 && renderIndex + 1 < e.Args.Length)
        {
            StartPreview(e.Args[renderIndex + 1]);
            return;
        }

        var security = _provider.GetRequiredService<SecurityService>();
        var emailService = _provider.GetRequiredService<EmailService>();

        // The security window is the first window: closing it without
        // authenticating must end the application instead of leaving an empty
        // process behind.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var gate = new SecurityWindow(security, emailService);
        MainWindow = gate;

        // The check runs in the background so an offline computer opens the
        // vault immediately. If a newer release exists, the update window is
        // shown on top of the current window and cannot be dismissed.
        gate.Loaded += (_, _) => StartUpdateCheck();

        bool? authenticated;
        try
        {
            authenticated = gate.ShowDialog();
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            Shutdown(1);
            return;
        }

        if (authenticated != true)
        {
            Shutdown(0);
            return;
        }

        var viewModel = _provider.GetRequiredService<MainViewModel>();
        var window = new MainWindow(
            viewModel,
            _provider.GetRequiredService<CaptureService>(),
            security,
            emailService);

        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        try
        {
            window.Show();
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            Shutdown(1);
        }
    }

    private void StartUpdateCheck()
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("PAUTH40_SKIP_UPDATE_CHECK"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            UpdateInfo? update = await _provider!
                .GetRequiredService<UpdateService>()
                .CheckForUpdateAsync(_updateCts.Token)
                .ConfigureAwait(false);

            if (update is null || _updateCts.IsCancellationRequested)
                return;

            await Dispatcher.InvokeAsync(() => ShowUpdateWindow(update));
        }
        catch
        {
            // Update checks must never interfere with opening the vault.
        }
    }

    private void ShowUpdateWindow(UpdateInfo update)
    {
        if (Interlocked.Exchange(ref _updateWindowShown, 1) != 0)
            return;

        var service = _provider!.GetRequiredService<UpdateService>();
        Window? owner = Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsVisible);

        var window = new UpdateWindow(update, service);

        if (owner is not null && owner.IsLoaded && !ReferenceEquals(owner, window))
            window.Owner = owner;

        window.ShowDialog();
    }

    private void StartPreview(string outputPath)
    {
        var clipboard = _provider!.GetRequiredService<ClipboardService>();
        var capture = _provider!.GetRequiredService<CaptureService>();
        var viewModel = MainViewModel.CreatePreview(clipboard);
        var window = new MainWindow(viewModel, capture)
        {
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 24,
            Top = 24
        };

        MainWindow = window;

        try
        {
            window.Show();
            _ = RenderThenShutdownAsync(window, outputPath);
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
            Shutdown(1);
        }
    }

    private async Task RenderThenShutdownAsync(MainWindow window, string outputPath)
    {
        try
        {
            await Task.Delay(650);
            window.RenderPreview(outputPath);
        }
        catch (Exception ex)
        {
            WriteStartupError(ex);
        }
        finally
        {
            Shutdown();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteStartupError(e.Exception);
        e.Handled = true;
    }

    private static void WriteStartupError(Exception exception)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "Pauth40-error.log"),
                $"{DateTimeOffset.Now:O} {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Never let diagnostics break the application.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updateCts.Cancel();
        _updateCts.Dispose();
        _provider?.GetService<SecurityService>()?.LockKey();
        _provider?.Dispose();
        base.OnExit(e);
    }
}
