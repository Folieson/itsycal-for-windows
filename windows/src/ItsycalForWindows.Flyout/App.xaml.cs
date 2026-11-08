using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace ItsycalForWindows.Flyout;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        // A failed XAML load otherwise fails fast with an opaque stowed exception, so keep a
        // small crash log next to the executable. Written only when something goes wrong.
        UnhandledException += (_, e) => DiagnosticLog.Write("UnhandledException: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DiagnosticLog.Write("AppDomain.UnhandledException: " + e.ExceptionObject);

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
        _window = new MainWindow();
        _window.Activate();
    }
}

/// <summary>
/// Spike-only crash diagnostics: a startup failure without a debugger is otherwise
/// indistinguishable from a native fail-fast.
/// </summary>
internal static class DiagnosticLog
{
    private static readonly object Sync = new();
    private static readonly string Path =
        System.IO.Path.Combine(
            Environment.GetEnvironmentVariable("ITSYCAL_FLYOUT_LOG_DIR") ?? AppContext.BaseDirectory,
            "itsycal-flyout.log");

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never break the app.
        }
    }
}
