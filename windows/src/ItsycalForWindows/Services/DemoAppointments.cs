#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel.Appointments;

namespace ItsycalForWindows.Services;

/// <summary>
/// DEBUG-only demo data: creates a small set of fake appointments in an app-owned calendar so the
/// calendar flyout can be verified against real data from the Windows appointment store, and
/// removes them again.
/// </summary>
/// <remarks>
/// <para>
/// The methods never throw: every failure is logged to the diagnostic log written by
/// <see cref="CalendarAccessProbe"/> and returned as a short human-readable result string. Created
/// appointment ids are recorded in <c>%LOCALAPPDATA%\ItsycalForWindows\demo-events.json</c>.
/// </para>
/// <para>
/// The five appointments are the same demo set a developer expects: two today, two tomorrow
/// (including an all-day one) and one in two days.
/// </para>
/// </remarks>
internal static class DemoAppointments
{
    private const string DataFolderName = "ItsycalForWindows";
    private const string DataFileName = "demo-events.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Seeds the demo appointments when a marker file asks for it. Verification runs (CI or a
    /// screenshot pass) create <c>%LOCALAPPDATA%\ItsycalForWindows\demo-auto-seed</c> and restart the
    /// host instead of driving the Command Palette UI; the marker is removed after seeding.
    /// </summary>
    internal static void AutoSeedIfRequested()
    {
        try
        {
            var marker = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                DataFolderName,
                "demo-auto-seed");
            if (!File.Exists(marker))
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await CreateAsync();
                }
                finally
                {
                    try
                    {
                        File.Delete(marker);
                    }
                    catch
                    {
                        // Best effort: a leftover marker only seeds once more.
                    }
                }
            });
        }
        catch
        {
            // Auto-seeding is diagnostics only and must never break startup.
        }
    }
    /// <summary>Full path of the recorded demo event ids.</summary>
    internal static string DataFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        DataFolderName,
        DataFileName);

    /// <summary>
    /// Creates the demo calendar and its five appointments. Prefers
    /// <see cref="AppointmentStoreAccessType.AllCalendarsReadWrite"/> and falls back to
    /// <see cref="AppointmentStoreAccessType.AppCalendarsReadWrite"/> (the access level that was
    /// actually granted is written to the log and to the result string).
    /// </summary>
    internal static async Task<string> CreateAsync()
    {
#pragma warning disable CA1031 // DEBUG helper: report failures through the log and the result string.
        try
        {
            CalendarAccessProbe.Log("demo: create requested");
            var writable = await CalendarAccessProbe.OpenWritableStoreAsync();
            if (writable.Store is null)
            {
                CalendarAccessProbe.Log($"demo: create failed - no writable store: {writable.Failure}");
                return "Demo events not created: no writable appointment store. See demo-access.log.";
            }

            var accessLevel = writable.AccessLevel!.Value.ToString();
            CalendarAccessProbe.Log($"demo: writable store acquired with {accessLevel}");

            // Stay idempotent: a leftover demo calendar from an earlier run is replaced.
            var staleCalendars = await CalendarAccessProbe.DeleteDemoCalendarsAsync(writable.Store);
            if (staleCalendars > 0)
            {
                CalendarAccessProbe.Log($"demo: removed {staleCalendars} stale demo calendar(s)");
            }

            AppointmentCalendar calendar;
            try
            {
                calendar = await writable.Store.CreateAppointmentCalendarAsync(CalendarAccessProbe.DemoCalendarName);
            }
            catch (Exception exception)
            {
                CalendarAccessProbe.Log($"demo: CreateAppointmentCalendarAsync failed: {CalendarAccessProbe.Describe(exception)}");
                return $"Demo events not created: calendar creation failed ({exception.GetType().Name} 0x{exception.HResult:X8}). See demo-access.log.";
            }

            CalendarAccessProbe.Log($"calendar: created '{calendar.DisplayName}' ({calendar.LocalId})");

            var specs = BuildDemoSpecs(CalendarAccessProbe.Today());
            var saved = 0;
            foreach (var spec in specs)
            {
                try
                {
                    await calendar.SaveAppointmentAsync(CreateAppointment(spec));
                    saved++;
                    CalendarAccessProbe.Log($"save: ok '{spec.Subject}' {spec.Start:yyyy-MM-dd HH:mm} duration={spec.Duration}");
                }
                catch (Exception exception)
                {
                    CalendarAccessProbe.Log($"save: failed '{spec.Subject}': {CalendarAccessProbe.Describe(exception)}");
                }
            }

            var localIds = await ReadLocalIdsBySubjectAsync(calendar);
            var dataFile = new DemoStoreFile
            {
                AccessLevel = accessLevel,
                CalendarId = calendar.LocalId,
                CalendarDisplayName = calendar.DisplayName,
                CreatedAt = DateTimeOffset.Now,
                Events = [],
            };

            foreach (var spec in specs)
            {
                var hasId = localIds.TryGetValue(spec.Subject, out var localId);
                if (!hasId)
                {
                    CalendarAccessProbe.Log($"ids: could not resolve the created id of '{spec.Subject}'");
                }

                dataFile.Events.Add(new DemoStoreEvent
                {
                    LocalId = hasId ? localId : null,
                    Subject = spec.Subject,
                    Start = spec.Start,
                    End = spec.Start + spec.Duration,
                    IsAllDay = spec.IsAllDay,
                });
            }

            WriteDataFile(dataFile);
            CalendarAccessProbe.Log(
                $"demo: create done saved={saved}/{specs.Count} calendar={calendar.LocalId} json={DataFilePath}");

            return $"Created {saved}/{specs.Count} demo events in '{CalendarAccessProbe.DemoCalendarName}' " +
                $"(access: {accessLevel}). See demo-access.log.";
        }
        catch (Exception exception)
        {
            CalendarAccessProbe.Log($"demo: create threw {CalendarAccessProbe.Describe(exception)}");
            return $"Demo events not created: {exception.GetType().Name} 0x{exception.HResult:X8}. See demo-access.log.";
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Deletes the demo calendar (its appointments are deleted with it) and the recorded JSON file.
    /// </summary>
    internal static async Task<string> RemoveAsync()
    {
#pragma warning disable CA1031 // DEBUG helper: report failures through the log and the result string.
        try
        {
            CalendarAccessProbe.Log("demo: remove requested");
            var writable = await CalendarAccessProbe.OpenWritableStoreAsync();
            if (writable.Store is null)
            {
                CalendarAccessProbe.Log($"demo: remove failed - no writable store: {writable.Failure}");
                return "Demo events not removed: no writable appointment store. See demo-access.log.";
            }

            var accessLevel = writable.AccessLevel!.Value.ToString();
            var deletedCalendars = await CalendarAccessProbe.DeleteDemoCalendarsAsync(writable.Store);
            var jsonDeleted = DeleteDataFile();

            CalendarAccessProbe.Log(
                $"demo: remove done calendars={deletedCalendars} jsonDeleted={jsonDeleted} access={accessLevel}");

            return $"Removed {deletedCalendars} demo calendar(s) and {jsonDeleted switch { true => "the demo event list", false => "no event list (already absent)" }} " +
                $"(access: {accessLevel}). See demo-access.log.";
        }
        catch (Exception exception)
        {
            CalendarAccessProbe.Log($"demo: remove threw {CalendarAccessProbe.Describe(exception)}");
            return $"Demo events not removed: {exception.GetType().Name} 0x{exception.HResult:X8}. See demo-access.log.";
        }
#pragma warning restore CA1031
    }

    private static IReadOnlyList<DemoSpec> BuildDemoSpecs(DateOnly today)
    {
        var local = CalendarAccessProbe.StartOfDayLocal;
        return
        [
            new DemoSpec(
                "Design review",
                local(today).AddHours(10),
                TimeSpan.FromMinutes(45),
                "Teams",
                "Reviewing the new flyout with the team",
                IsAllDay: false),
            new DemoSpec(
                "Lunch with the team",
                local(today).AddHours(13),
                TimeSpan.FromHours(1),
                null,
                null,
                IsAllDay: false),
            new DemoSpec(
                "Sprint planning",
                local(today.AddDays(1)).AddHours(9) + TimeSpan.FromMinutes(30),
                TimeSpan.FromMinutes(90),
                null,
                null,
                IsAllDay: false),
            new DemoSpec(
                "Conference day",
                local(today.AddDays(1)),
                TimeSpan.FromDays(1),
                null,
                null,
                IsAllDay: true),
            new DemoSpec(
                "1:1 with Alex",
                local(today.AddDays(2)).AddHours(16),
                TimeSpan.FromMinutes(30),
                null,
                null,
                IsAllDay: false),
        ];
    }

    private static Appointment CreateAppointment(DemoSpec spec) => new()
    {
        Subject = spec.Subject,
        StartTime = spec.Start,
        Duration = spec.Duration,
        AllDay = spec.IsAllDay,
        Location = spec.Location ?? string.Empty,
        Details = spec.Details ?? string.Empty,
    };

    private static async Task<Dictionary<string, string>> ReadLocalIdsBySubjectAsync(AppointmentCalendar calendar)
    {
        var options = new FindAppointmentsOptions();
        options.FetchProperties.Add(AppointmentProperties.Subject);
        options.FetchProperties.Add(AppointmentProperties.StartTime);

        var from = CalendarAccessProbe.Today().AddDays(-1);
        var appointments = await calendar.FindAppointmentsAsync(
            CalendarAccessProbe.StartOfDayLocal(from),
            TimeSpan.FromDays(6),
            options);

        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var appointment in appointments)
        {
            ids[appointment.Subject] = appointment.LocalId;
        }

        return ids;
    }

    private static void WriteDataFile(DemoStoreFile dataFile)
    {
        var path = DataFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(dataFile, JsonOptions));
    }

    private static bool DeleteDataFile()
    {
        var path = DataFilePath;
        if (!File.Exists(path))
        {
            return false;
        }

#pragma warning disable CA1031 // Best-effort JSON cleanup.
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exception)
        {
            CalendarAccessProbe.Log($"demo: could not delete {path}: {CalendarAccessProbe.Describe(exception)}");
            return false;
        }
#pragma warning restore CA1031
    }

    private sealed record DemoSpec(
        string Subject,
        DateTimeOffset Start,
        TimeSpan Duration,
        string? Location,
        string? Details,
        bool IsAllDay);

    private sealed class DemoStoreFile
    {
        public string AccessLevel { get; set; } = string.Empty;

        public string CalendarId { get; set; } = string.Empty;

        public string CalendarDisplayName { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }

        public List<DemoStoreEvent> Events { get; set; } = [];
    }

    private sealed class DemoStoreEvent
    {
        public string? LocalId { get; set; }

        public string Subject { get; set; } = string.Empty;

        public DateTimeOffset Start { get; set; }

        public DateTimeOffset End { get; set; }

        public bool IsAllDay { get; set; }
    }
}
#endif
