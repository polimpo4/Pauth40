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
        _provider?.GetService<SecurityService>()?.LockKey();
        _provider?.Dispose();
        base.OnExit(e);
    }
}
