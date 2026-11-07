using System;
using System.Collections.Generic;
using System.Linq;
using CalendarEvent = global::ItsycalForWindows.Models.CalendarEvent;

namespace ItsycalForWindows.Tests;

/// <summary>Small builders for iCalendar fixtures used by the ICS tests.</summary>
internal static class IcsTestData
{
    /// <summary>Wraps the given VEVENT lines in a minimal VCALENDAR (CRLF line endings).</summary>
    public static string Event(params string[] lines) => Calendar(["BEGIN:VEVENT", .. lines, "END:VEVENT"]);

    /// <summary>Wraps the given lines in a minimal VCALENDAR (CRLF line endings).</summary>
    public static string Calendar(params string[] lines) =>
        string.Join("\r\n", ["BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//Itsycal//Tests//EN", .. lines, "END:VCALENDAR", ""]);

    /// <summary>Wraps several events (each already a set of VEVENT lines) in one VCALENDAR.</summary>
    public static string CalendarWithEvents(IEnumerable<IEnumerable<string>> events)
    {
        var lines = new List<string> { "BEGIN:VCALENDAR", "VERSION:2.0" };
        foreach (var linesOfEvent in events)
        {
            lines.Add("BEGIN:VEVENT");
            lines.AddRange(linesOfEvent);
            lines.Add("END:VEVENT");
        }

        lines.Add("END:VCALENDAR");
        lines.Add(string.Empty);
        return string.Join("\r\n", lines);
    }

    /// <summary>The same content with Unix line endings, for line-ending robustness tests.</summary>
    public static string WithLfLineEndings(string icsText) => icsText.Replace("\r\n", "\n");

    /// <summary>Maps parsed events to their UTC start instants, for compact assertions.</summary>
    public static string[] UtcStarts(IEnumerable<CalendarEvent> events) =>
        events.Select(calendarEvent => calendarEvent.Start.UtcDateTime.ToString("yyyy-MM-ddTHH:mmZ")).ToArray();
}
