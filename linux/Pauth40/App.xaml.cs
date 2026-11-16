using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Pauth40.Localization;
using Pauth40.Services;
using Pauth40.ViewModels;

namespace Pauth40;

/// <summary>
/// Application entry point. A single native window hosts the whole flow: the
/// security gate, the authenticator, the settings page and the mandatory update
/// prompt. Every Uno target shares the same shell.
/// </summary>
public partial class App : Application
{
    public static Window? Instance { get; private set; }

    public static IServiceProvider Services { get; private set; } = null!;

    protected Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Load the saved interface language (or detect it from the system)
        // before any view is created.
        LocalizationManager.Instance.Initialize();

        var services = new ServiceCollection();
        services.AddSingleton<AccountStore>();
        services.AddSingleton<SecurityService>();
        services.AddSingleton<EmailService>();
        services.AddSingleton<ClipboardService>();
        services.AddSingleton<CaptureService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<MainViewModel>();
        Services = services.BuildServiceProvider();

        UnhandledException += OnUnhandledException;

        MainWindow = new Window
        {
            Title = "Pauth40"
        };

        Instance = MainWindow;
        MainWindow.Content = new MainPage();
        MainWindow.Activate();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        WriteDiagnostics(e.Exception);
        e.Handled = true;
    }

    private static void WriteDiagnostics(Exception exception)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "Pauth40-error.log"),
                $"{DateTimeOffset.Now:O} {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never break the application.
        }
    }

    /// <summary>Stops the process on every platform where that is meaningful.</summary>
    public static void ExitApplication()
    {
#if __WASM__
        // A browser tab cannot be closed from script. Leaving the session
        // untouched is the only safe option.
#else
        Environment.Exit(0);
#endif
    }

    /// <summary>
    /// Configures global Uno Platform logging
    /// </summary>
    public static void InitializeLogging()
    {
#if DEBUG
        var factory = LoggerFactory.Create(builder =>
        {
#if __WASM__
            builder.AddProvider(new global::Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider());
#elif __IOS__
            builder.AddProvider(new global::Uno.Extensions.Logging.OSLogLoggerProvider());
            builder.AddConsole();
#else
            builder.AddConsole();
#endif

            builder.SetMinimumLevel(LogLevel.Information);

            builder.AddFilter("Uno", LogLevel.Warning);
            builder.AddFilter("Windows", LogLevel.Warning);
            builder.AddFilter("Microsoft", LogLevel.Warning);
        });

        global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;

#if HAS_UNO
        global::Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
#endif
    }
}
