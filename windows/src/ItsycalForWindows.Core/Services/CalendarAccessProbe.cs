#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.Appointments;

namespace ItsycalForWindows.Services;

/// <summary>
/// DEBUG-only diagnostic for the Windows appointment store. Every access attempt is appended to
/// <c>%LOCALAPPDATA%\ItsycalForWindows\demo-access.log</c> (timestamp, access level, result, and -
/// for successful reads - the number of calendars and appointments), so a packaged run can be
/// diagnosed even when the Command Palette toast is not visible.
/// </summary>
/// <remarks>
/// This type exists only in DEBUG builds. Logging is best effort and can never throw into the
/// extension host; the log contains only counts and exception details, never event content.
/// </remarks>
internal static class CalendarAccessProbe
{
    internal const string DemoCalendarName = "Itsycal demo";

    private const string DataFolderName = "ItsycalForWindows";
    private const string LogFileName = "demo-access.log";
    private const int LogFileMaxBytes = 256 * 1024;

    /// <summary>Full path of the diagnostic log.</summary>
    internal static string LogFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        DataFolderName,
        LogFileName);

    /// <summary>
    /// Appends one timestamped line to the diagnostic log. Best effort: a file-system failure must
    /// never affect the extension.
    /// </summary>
    internal static void Log(string message)
    {
        Debug.WriteLine($"[ItsycalForWindows/demo] {message}");

#pragma warning disable CA1031 // A DEBUG diagnostic log must never break the extension.
        try
        {
            var path = LogFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var line = $"{DateTimeOffset.Now:yyyy-MM-ddTHH:mm:ss.fffzzz} {message}{Environment.NewLine}";
            if (File.Exists(path) && new FileInfo(path).Length > LogFileMaxBytes)
            {
                File.Move(path, path + ".old", overwrite: true);
            }

            File.AppendAllText(path, line);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[ItsycalForWindows/demo] log write failed: {exception.GetType().Name}");
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Probes the three appointment-store access levels the extension might use and returns a short
    /// human-readable summary of what is granted. All details are also written to the log.
    /// </summary>
    internal static async Task<string> DescribeAsync()
    {
        var summary = new StringBuilder();
        Log("probe: start");

        // (a) The read path used by the calendar flyout.
        summary.Append("AllCalendarsReadOnly: ").AppendLine(await ProbeAsync(
            "AllCalendarsReadOnly+FindAppointmentCalendarsAsync",
            AppointmentStoreAccessType.AllCalendarsReadOnly,
            async store =>
            {
                var calendars = await store.FindAppointmentCalendarsAsync();

                var options = new FindAppointmentsOptions();
                options.FetchProperties.Add(AppointmentProperties.Subject);
                options.FetchProperties.Add(AppointmentProperties.StartTime);

                var from = DateOnly.FromDateTime(DateTime.Now);
                var appointments = await store.FindAppointmentsAsync(
                    StartOfDayLocal(from),
                    TimeSpan.FromDays(7),
                    options);

                return $"{calendars.Count} calendar(s), {appointments.Count} appointment(s) in next 7 days";
            }));

        // (b) The write path used for app-owned calendars. Creating the demo calendar is the
        // operation that fails first when no writable provider is installed, so it is probed
        // explicitly. An existing demo calendar is removed first to keep the probe idempotent.
        summary.Append("AppCalendarsReadWrite: ").AppendLine(await ProbeAsync(
            "AppCalendarsReadWrite+CreateAppointmentCalendarAsync",
            AppointmentStoreAccessType.AppCalendarsReadWrite,
            async store =>
            {
                await DeleteDemoCalendarsAsync(store);

                var calendar = await store.CreateAppointmentCalendarAsync(DemoCalendarName);
                Log($"probe: created calendar '{calendar.DisplayName}' ({calendar.LocalId})");

                await calendar.DeleteAsync();
                Log($"probe: removed the probe calendar '{calendar.LocalId}'");

                return $"created and deleted '{DemoCalendarName}'";
            }));

        // (c) Full read/write access to every calendar; this is what DemoAppointments prefers.
        summary.Append("AllCalendarsReadWrite: ").AppendLine(await ProbeAsync(
            "AllCalendarsReadWrite",
            AppointmentStoreAccessType.AllCalendarsReadWrite,
            _ => Task.FromResult("store acquired")));

        summary.Append("log: ").Append(LogFilePath);
        var text = summary.ToString();
        Log("probe: summary = " + text.Replace(Environment.NewLine, " | "));
        return text;
    }

    /// <summary>
    /// Tries to acquire a writable store: <see cref="AppointmentStoreAccessType.AllCalendarsReadWrite"/>
    /// first, then <see cref="AppointmentStoreAccessType.AppCalendarsReadWrite"/>. Every attempt is
    /// logged. Never throws; <see cref="WritableStoreResult.Store"/> is <c>null</c> when neither
    /// level is granted.
    /// </summary>
    internal static async Task<WritableStoreResult> OpenWritableStoreAsync()
    {
        var accessLevels = new[]
        {
            AppointmentStoreAccessType.AllCalendarsReadWrite,
            AppointmentStoreAccessType.AppCalendarsReadWrite,
        };

        var failures = new List<string>(accessLevels.Length);
        foreach (var accessLevel in accessLevels)
        {
            var started = Stopwatch.StartNew();
            try
            {
                var store = await AppointmentManager.RequestStoreAsync(accessLevel);
                started.Stop();
                Log($"attempt: {accessLevel} -> ok ({started.ElapsedMilliseconds} ms)");
                return new WritableStoreResult(store, accessLevel, null);
            }
            catch (Exception exception)
            {
                started.Stop();
                var description = Describe(exception);
                failures.Add($"{accessLevel}: {description}");
                Log($"attempt: {accessLevel} -> failed after {started.ElapsedMilliseconds} ms: {description}");
            }
        }

        return new WritableStoreResult(null, null, string.Join("; ", failures));
    }

    /// <summary>Deletes every calendar named <see cref="DemoCalendarName"/>. Best effort; failures are logged.</summary>
    internal static async Task<int> DeleteDemoCalendarsAsync(AppointmentStore store)
    {
        var deleted = 0;

#pragma warning disable CA1031 // Best-effort demo cleanup; the caller reports the result.
        try
        {
            var calendars = await store.FindAppointmentCalendarsAsync();
            foreach (var calendar in calendars)
            {
                if (!string.Equals(calendar.DisplayName, DemoCalendarName, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    await calendar.DeleteAsync();
                    deleted++;
                    Log($"cleanup: deleted calendar '{DemoCalendarName}' ({calendar.LocalId})");
                }
                catch (Exception exception)
                {
                    Log($"cleanup: could not delete calendar '{calendar.LocalId}': {Describe(exception)}");
                }
            }
        }
        catch (Exception exception)
        {
            Log($"cleanup: could not enumerate calendars: {Describe(exception)}");
        }
#pragma warning restore CA1031

        return deleted;
    }

    internal static DateTimeOffset StartOfDayLocal(DateOnly date)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, TimeZoneInfo.Local.GetUtcOffset(midnight));
    }

    internal static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    internal static string Describe(Exception exception)
    {
        var hresult = exception.HResult != 0 ? $"HRESULT=0x{exception.HResult:X8}" : "HRESULT=n/a";
        return $"{exception.GetType().Name} {hresult}: {exception.Message}";
    }

    private static async Task<string> ProbeAsync(
        string label,
        AppointmentStoreAccessType accessLevel,
        Func<AppointmentStore, Task<string>> inspect)
    {
        Log($"attempt: {label}");
        var started = Stopwatch.StartNew();
        try
        {
            var store = await AppointmentManager.RequestStoreAsync(accessLevel);
            var details = await inspect(store);
            started.Stop();
            Log($"result: {label} -> ok ({started.ElapsedMilliseconds} ms): {details}");
            return $"granted ({details})";
        }
        catch (Exception exception)
        {
            started.Stop();
            var description = Describe(exception);
            Log($"result: {label} -> failed after {started.ElapsedMilliseconds} ms: {description}");
            return $"denied ({description})";
        }
    }

    /// <summary>Result of <see cref="OpenWritableStoreAsync"/>.</summary>
    internal sealed record WritableStoreResult(
        AppointmentStore? Store,
        AppointmentStoreAccessType? AccessLevel,
        string? Failure);
}
#endif
