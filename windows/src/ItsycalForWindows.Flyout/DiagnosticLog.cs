using System;
using System.IO;
using System.Text;

namespace ItsycalForWindows.Flyout;

/// <summary>
/// Two deliberately separate logs:
///
/// 1. <see cref="Write"/> is the development/measurement log. It is compiled in only for Debug
///    builds, or enabled at runtime by <c>ITSYCAL_FLYOUT_LOG_DIR</c> (the screenshot workflow
///    sets it and the layout/measurement output keeps working). A Release build without that
///    variable writes nothing. The file never lives next to the executable — next to the exe
///    is often a read-only program directory and puts user data in an unexpected place.
/// 2. <see cref="WriteCrash"/> always works: a single line per crash in
///    <c>%LOCALAPPDATA%\Calwren\logs\flyout-crash.log</c>, capped at 256 KB with one backup.
///    (The folder is still <c>ItsycalForWindows</c> on machines that predate the Calwren rename;
///    new writes use Calwren.)
///
/// Privacy rule for every caller: log counts, ids and state only. Event titles, locations,
/// notes and meeting URLs are calendar content and must never reach either file.
/// </summary>
internal static class DiagnosticLog
{
    private const string LogDirVariable = "ITSYCAL_FLYOUT_LOG_DIR";
    private const string MeasurementFileName = "itsycal-flyout.log";
    private const string DebugFileName = "flyout-debug.log";
    private const string CrashFileName = "flyout-crash.log";
    private const long MaxMeasurementBytes = 4 * 1024 * 1024;
    private const long MaxCrashBytes = 256 * 1024;
    private const int MaxCrashSummaryLength = 300;

    private static readonly object Sync = new();

    private static readonly string? MeasurementPath = ResolveMeasurementPath();
    private static readonly string CrashPath = ResolveCrashPath();
    private static string? _lastCrashSignature;
    private static long _lastCrashTicks;

    /// <summary>True when the caller should bother composing a diagnostic message.</summary>
    public static bool Enabled => MeasurementPath is not null;

    public static void Write(string message)
    {
        var path = MeasurementPath;
        if (path is null)
        {
            return;
        }

        try
        {
            lock (Sync)
            {
                EnsureDirectory(path);
                RotateIfTooLarge(path, MaxMeasurementBytes);
                File.AppendAllText(
                    path,
                    string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}"),
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never break the app.
        }
    }

    /// <summary>
    /// One-line crash diagnostic. Callers pass the exception; only its type and a short,
    /// single-line summary are stored, never a full stack trace or calendar content.
    /// </summary>
    public static void WriteCrash(string source, Exception? exception)
    {
        var summary = exception is null
            ? "unknown failure"
            : exception.GetType().Name + ": " + SanitizeSummary(exception.Message);
        WriteCrashLine(source, summary);
    }

    public static void WriteCrashLine(string source, string summary)
    {
        try
        {
            var cleanSource = SanitizeSummary(source);
            var cleanSummary = SanitizeSummary(summary);
            var signature = cleanSource + "|" + cleanSummary;
            var line = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {cleanSource} {cleanSummary}");

            lock (Sync)
            {
                // The UI and AppDomain handlers can both fire for the same failure. Deduplicate
                // on the content (the timestamp in the line always differs).
                var now = Environment.TickCount64;
                if (string.Equals(signature, _lastCrashSignature, StringComparison.Ordinal) && now - _lastCrashTicks < 2000)
                {
                    return;
                }

                _lastCrashSignature = signature;
                _lastCrashTicks = now;

                EnsureDirectory(CrashPath);
                RotateToBackupIfTooLarge(CrashPath, MaxCrashBytes);
                File.AppendAllText(CrashPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Crash diagnostics must never mask the original failure.
        }
    }

    private static string? ResolveMeasurementPath()
    {
        var explicitDirectory = Environment.GetEnvironmentVariable(LogDirVariable);
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            return Path.Combine(explicitDirectory.Trim(), MeasurementFileName);
        }

#if DEBUG
        // Debug builds may log locally for development, but never into the program directory.
        return Path.Combine(LocalAppDataRoot(), "Calwren", "logs", DebugFileName);
#else
        return null;
#endif
    }

    private static string ResolveCrashPath() => Path.Combine(LocalAppDataRoot(), "Calwren", "logs", CrashFileName);

    private static string LocalAppDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData))
        {
            // Environment.SpecialFolder.LocalApplicationData is always present on Windows, but
            // never fall back to AppContext.BaseDirectory: the log must not land next to the exe.
            localAppData = Path.GetTempPath();
        }

        return localAppData;
    }

    private static void EnsureDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void RotateIfTooLarge(string path, long maxBytes)
    {
        var info = new FileInfo(path);
        if (info.Exists && info.Length >= maxBytes)
        {
            // One rolling replacement keeps the current run's measurements usable.
            File.Move(path, path + ".1", overwrite: true);
        }
    }

    private static void RotateToBackupIfTooLarge(string path, long maxBytes)
    {
        var info = new FileInfo(path);
        if (info.Exists && info.Length >= maxBytes)
        {
            File.Move(path, path + ".1", overwrite: true);
        }
    }

    private static string SanitizeSummary(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "-";
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;
        foreach (var character in value)
        {
            var isSpace = char.IsWhiteSpace(character) || char.IsControl(character);
            if (isSpace)
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }
            }
            else
            {
                builder.Append(character);
                previousWasSpace = false;
            }

            if (builder.Length >= MaxCrashSummaryLength)
            {
                builder.Append('…');
                break;
            }
        }

        return builder.ToString().Trim();
    }
}
