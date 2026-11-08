using System;
using Microsoft.UI.Xaml;

namespace ItsycalForWindows.Flyout;

public partial class App : Application
{
    private SingleInstance? _singleInstance;

    public App()
    {
        // A failed XAML load otherwise fails fast with an opaque stowed exception. Crash
        // diagnostics stay enabled in Release: one line, capped, under %LOCALAPPDATA%.
        UnhandledException += (_, e) => DiagnosticLog.WriteCrash("XAML", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DiagnosticLog.WriteCrash("AppDomain", e.ExceptionObject as Exception);

        // Single instance must be decided before any XAML/window/tray object exists. A second
        // launch signals the owner and exits; it never creates a second window or tray icon.
        _singleInstance = SingleInstance.TryAcquire(out var alreadyRunning);
        if (_singleInstance is null && alreadyRunning)
        {
            SingleInstance.SignalExistingInstance();
            DiagnosticLog.Write("SINGLE result=secondary-exit");
            Environment.Exit(0);
            return;
        }

        DiagnosticLog.Write($"SINGLE result={(_singleInstance is null ? "degraded" : "primary")}");

        // Evidence hook: the design review needs a light-theme capture while the host runs dark.
        // Application.RequestedTheme is only settable before the first window is created.
        var requested = Environment.GetEnvironmentVariable("ITSYCAL_FLYOUT_THEME");
        if (Enum.TryParse<ApplicationTheme>(requested, ignoreCase: true, out var theme))
        {
            RequestedTheme = theme;
        }

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();

        // Wired after construction so a wake-up that lands during startup is replayed once the
        // window exists (the named event is auto-reset and must not be lost).
        _singleInstance?.SetShowHandler(() =>
        {
            DiagnosticLog.Write("SINGLE show-requested");
            window.DispatcherQueue.TryEnqueue(window.ShowFlyout);
        });

        // Hidden start is the production behaviour when the tray icon is available. The
        // screenshot workflow sets ITSYCAL_FLYOUT_STAY_VISIBLE=1, and a failed tray
        // registration keeps the window visible so there is always a way to use and quit it.
        if (window.ShouldStartVisible)
        {
            window.ShowFlyout();
        }

        window.Closed += (_, _) =>
        {
            window.Dispose();
            _singleInstance?.Dispose();
            _singleInstance = null;
        };
    }
}
