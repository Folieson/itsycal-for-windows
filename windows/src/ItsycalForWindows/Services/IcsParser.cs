using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ItsycalForWindows.Models;

namespace ItsycalForWindows.Services;

/// <summary>
/// Minimal, dependency-free reader for RFC 5545 (iCalendar) calendar data.
/// </summary>
/// <remarks>
/// <para>
/// Supported subset: one or more <c>VEVENT</c> components inside any calendar wrapper;
/// <c>DTSTART</c>/<c>DTEND</c> as DATE or DATE-TIME values (local/floating, UTC with a trailing
/// <c>Z</c>, or <c>TZID=</c> resolved through <see cref="TimeZoneInfo"/>); <c>DURATION</c>;
/// <c>SUMMARY</c>, <c>LOCATION</c>, <c>DESCRIPTION</c> with RFC text escaping; online-meeting links
/// from <c>CONFERENCE</c>, <c>X-MICROSOFT-SKYPETEAMSMEETINGURL</c> or <c>URL</c>; and a basic
/// <c>RRULE</c> subset (<c>FREQ=DAILY|WEEKLY</c> with <c>INTERVAL</c>, <c>COUNT</c>, <c>UNTIL</c>)
/// plus simple <c>EXDATE</c> exclusions.
/// </para>
/// <para>
/// The parser never throws on malformed input: a broken property, VEVENT or file is skipped and
/// everything that could be parsed is returned. Recurring series are expanded up to
/// <see cref="MaxRecurrenceOccurrences"/> occurrences and are clipped to the requested window.
/// Unsupported features (e.g. <c>FREQ=MONTHLY</c>, <c>BYDAY</c>) degrade to the first occurrence
/// rather than failing.
/// </para>
/// </remarks>
public static class IcsParser
{
    /// <summary>Hard cap on expanded occurrences of one recurring VEVENT.</summary>
    public const int MaxRecurrenceOccurrences = 366;

    private const string VEvent = "VEVENT";

    /// <summary>Parses all events in <paramref name="icsText"/> without a window filter.</summary>
    public static IReadOnlyList<CalendarEvent> Parse(string? icsText) =>
        ParseCore(icsText, fromInclusive: null, toExclusive: null);

    /// <summary>
    /// Parses every event in <paramref name="icsText"/> that overlaps the half-open window
    /// <c>[fromInclusive, toExclusive)</c> and expands recurring events inside that window.
    /// </summary>
    public static IReadOnlyList<CalendarEvent> Parse(string? icsText, DateOnly fromInclusive, DateOnly toExclusive) =>
        ParseCore(icsText, fromInclusive, toExclusive);

    private static List<CalendarEvent> ParseCore(
        string? icsText,
        DateOnly? fromInclusive,
        DateOnly? toExclusive)
    {
        var events = new List<CalendarEvent>();
        if (string.IsNullOrWhiteSpace(icsText))
        {
            return events;
        }

        List<string> lines;
        try
        {
            lines = UnfoldLines(icsText);
        }
#pragma warning disable CA1031 // Malformed calendar data is expected; degrade to "no events".
        catch (Exception)
        {
            return events;
        }
#pragma warning restore CA1031

        List<ContentLine>? currentEvent = null;
        foreach (var line in lines)
        {
            var token = line.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            try
            {
                if (IsComponent(token, "BEGIN", VEvent))
                {
                    currentEvent = [];
                    continue;
                }

                if (currentEvent is null)
                {
                    continue;
                }

                if (IsComponent(token, "END", VEvent))
                {
                    try
                    {
                        events.AddRange(BuildEvents(currentEvent, fromInclusive, toExclusive));
                    }
#pragma warning disable CA1031 // Skip the bad VEVENT, keep the rest of the file.
                    catch (Exception)
                    {
                        // Ignored: a single malformed event must not hide the other ones.
                    }
#pragma warning restore CA1031

                    currentEvent = null;
                    continue;
                }

                var contentLine = ContentLine.TryParse(line);
                if (contentLine is not null)
                {
                    currentEvent.Add(contentLine);
                }
            }
#pragma warning disable CA1031 // Skip the bad property, keep the rest of the event.
            catch (Exception)
            {
                // Ignored: malformed properties are skipped.
            }
#pragma warning restore CA1031
        }

        events.Sort(static (left, right) => CompareEvents(left, right));
        return events;
    }

    private static int CompareEvents(CalendarEvent left, CalendarEvent right)
    {
        var byStart = left.Start.CompareTo(right.Start);
        return byStart != 0
            ? byStart
            : string.Compare(left.Subject, right.Subject, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<CalendarEvent> BuildEvents(
        IReadOnlyList<ContentLine> lines,
        DateOnly? fromInclusive,
        DateOnly? toExclusive)
    {
        var properties = new Dictionary<string, ContentLine>(StringComparer.OrdinalIgnoreCase);
        var exDates = new List<ContentLine>();
        var conferences = new List<ContentLine>();

        foreach (var line in lines)
        {
            switch (line.Name)
            {
                case "EXDATE":
                    exDates.Add(line);
                    break;
                case "CONFERENCE":
                    conferences.Add(line);
                    break;
                default:
                    properties.TryAdd(line.Name, line);
                    break;
            }
        }

        if (!properties.TryGetValue("DTSTART", out var dtStartLine) ||
            !TryParseDateTime(dtStartLine, out var start, out var isAllDay, out var startDate))
        {
            yield break;
        }

        var end = isAllDay ? start.AddDays(1) : start;
        var hasEnd = false;

        if (properties.TryGetValue("DTEND", out var dtEndLine) &&
            TryParseDateTime(dtEndLine, out var parsedEnd, out _, out _))
        {
            // A DATE-valued DTEND is the exclusive end day; TryParseDateTime already returns its
            // local midnight, which is exactly the exclusive end instant the model expects.
            end = parsedEnd;
            hasEnd = true;
        }

        if (!hasEnd && properties.TryGetValue("DURATION", out var durationLine) &&
            TryParseDuration(durationLine.Value, out var duration))
        {
            end = start + (duration < TimeSpan.Zero ? TimeSpan.Zero : duration);
            hasEnd = true;
        }

        if (!hasEnd)
        {
            end = isAllDay ? start.AddDays(1) : start;
        }

        if (end < start)
        {
            end = start;
        }

        var subject = ReadText(properties, "SUMMARY");
        var location = ReadText(properties, "LOCATION");
        var details = ReadText(properties, "DESCRIPTION");
        var onlineMeetingLink = ReadOnlineMeetingLink(conferences, properties);

        var baseEvent = new CalendarEvent(
            subject,
            start,
            end,
            isAllDay,
            string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            onlineMeetingLink,
            string.IsNullOrWhiteSpace(details) ? null : details.Trim());

        if (!properties.TryGetValue("RRULE", out var ruleLine) ||
            !TryParseRecurrence(ruleLine.Value, out var recurrence) ||
            recurrence is null)
        {
            if (OverlapsWindow(baseEvent, fromInclusive, toExclusive))
            {
                yield return baseEvent;
            }

            yield break;
        }

        var durationSpan = baseEvent.End - baseEvent.Start;
        var allDaySpanDays = Math.Max(1, baseEvent.EndDateInclusive.DayNumber - baseEvent.StartDate.DayNumber + 1);
        var stepDays = recurrence.Frequency == "WEEKLY"
            ? 7 * recurrence.Interval
            : recurrence.Interval;

        // Jump close to the requested window instead of walking every occurrence since DTSTART;
        // the hard cap then applies to the occurrences actually considered for the window.
        var firstIndex = 0;
        if (fromInclusive is { } windowStart)
        {
            var daysUntilWindow = windowStart.DayNumber - startDate.DayNumber;
            if (daysUntilWindow > 0)
            {
                firstIndex = Math.Max(0, (daysUntilWindow / stepDays) - 1);
            }
        }

        if (recurrence.Count is { } occurrenceCount && firstIndex >= occurrenceCount)
        {
            yield break;
        }

        for (var index = firstIndex; index < firstIndex + MaxRecurrenceOccurrences; index++)
        {
            var occurrenceDate = startDate.AddDays(stepDays * index);
            var occurrenceStart = isAllDay
                ? EventRange.StartOfDayLocal(occurrenceDate)
                : start.AddDays((double)stepDays * index);

            if (recurrence.Count is { } count && index >= count)
            {
                yield break;
            }

            if (recurrence.UntilInstant is { } untilInstant && occurrenceStart > untilInstant)
            {
                yield break;
            }

            if (recurrence.UntilDate is { } untilDate && occurrenceDate > untilDate)
            {
                yield break;
            }

            if (toExclusive is { } windowEnd && occurrenceDate >= windowEnd)
            {
                yield break;
            }

            if (IsExcluded(exDates, occurrenceStart, occurrenceDate, isAllDay))
            {
                continue;
            }

            var occurrenceEnd = isAllDay
                ? EventRange.StartOfDayLocal(occurrenceDate.AddDays(allDaySpanDays))
                : occurrenceStart + durationSpan;
            var occurrence = baseEvent with { Start = occurrenceStart, End = occurrenceEnd };

            if (OverlapsWindow(occurrence, fromInclusive, toExclusive))
            {
                yield return occurrence;
            }
        }
    }

    private static bool OverlapsWindow(CalendarEvent calendarEvent, DateOnly? fromInclusive, DateOnly? toExclusive)
    {
        if (fromInclusive is not { } from || toExclusive is not { } to)
        {
            return true;
        }

        return calendarEvent.StartDate < to && calendarEvent.EndDateInclusive >= from;
    }

    private static bool IsExcluded(
        IReadOnlyList<ContentLine> exDates,
        DateTimeOffset occurrenceStart,
        DateOnly occurrenceDate,
        bool isAllDay)
    {
        foreach (var line in exDates)
        {
            foreach (var value in SplitUnquoted(line.Value, ','))
            {
                if (!TryParseDateTime(value, line.Parameters, out var excludedStart, out var excludedIsAllDay, out var excludedDate))
                {
                    continue;
                }

                if (isAllDay && excludedIsAllDay && excludedDate == occurrenceDate)
                {
                    return true;
                }

                if (!isAllDay && !excludedIsAllDay && excludedStart == occurrenceStart)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string ReadText(IReadOnlyDictionary<string, ContentLine> properties, string name) =>
        properties.TryGetValue(name, out var line) ? Unescape(line.Value) : string.Empty;

    private static string? ReadOnlineMeetingLink(
        IReadOnlyList<ContentLine> conferences,
        IReadOnlyDictionary<string, ContentLine> properties)
    {
        foreach (var conference in conferences)
        {
            var value = conference.Value.Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        if (properties.TryGetValue("X-MICROSOFT-SKYPETEAMSMEETINGURL", out var teamsLink))
        {
            var value = teamsLink.Value.Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        if (properties.TryGetValue("URL", out var url))
        {
            var value = url.Value.Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryParseDateTime(
        string value,
        IReadOnlyDictionary<string, string> parameters,
        out DateTimeOffset instant,
        out bool isAllDay,
        out DateOnly date)
    {
        instant = default;
        isAllDay = false;
        date = default;

        var text = value.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var declaredDate = parameters.TryGetValue("VALUE", out var valueType) &&
            string.Equals(valueType.Trim(), "DATE", StringComparison.OrdinalIgnoreCase);

        if (declaredDate || (text.Length == 8 && text.All(char.IsAsciiDigit)))
        {
            if (!TryParseDate(text, out date))
            {
                return false;
            }

            isAllDay = true;
            instant = EventRange.StartOfDayLocal(date);
            return true;
        }

        var separator = text.IndexOf('T', StringComparison.OrdinalIgnoreCase);
        if (separator != 8 || !TryParseDate(text[..separator], out date))
        {
            return false;
        }

        var timePart = text[(separator + 1)..];
        var isUtc = timePart.EndsWith('Z') || timePart.EndsWith('z');
        if (isUtc)
        {
            timePart = timePart[..^1];
        }

        if (!TryParseTime(timePart, out var time))
        {
            return false;
        }

        var wallClock = date.ToDateTime(time);
        if (isUtc)
        {
            instant = new DateTimeOffset(wallClock, TimeSpan.Zero);
            return true;
        }

        var zone = parameters.TryGetValue("TZID", out var tzid) ? TryResolveTimeZone(tzid) : null;
        instant = zone is null
            ? new DateTimeOffset(wallClock, TimeZoneInfo.Local.GetUtcOffset(wallClock))
            : new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock));
        return true;
    }

    private static bool TryParseDateTime(
        ContentLine line,
        out DateTimeOffset instant,
        out bool isAllDay,
        out DateOnly date) =>
        TryParseDateTime(line.Value, line.Parameters, out instant, out isAllDay, out date);

    private static bool TryParseDate(string text, out DateOnly date)
    {
        date = default;
        if (text.Length != 8 || !text.All(char.IsAsciiDigit))
        {
            return false;
        }

        return DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryParseTime(string text, out TimeOnly time)
    {
        time = default;
        foreach (var format in new[] { "HHmmss", "HHmm" })
        {
            if (TimeOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            {
                return true;
            }
        }

        return false;
    }

    private static TimeZoneInfo? TryResolveTimeZone(string tzid)
    {
        if (string.IsNullOrWhiteSpace(tzid))
        {
            return null;
        }

        var candidates = new List<string>();
        var trimmed = tzid.Trim();
        candidates.Add(trimmed);

        if (trimmed.StartsWith('/'))
        {
            trimmed = trimmed[1..];
            candidates.Add(trimmed);
        }

        // Some producers prefix the IANA id (e.g. "/mozilla.org/20050126_1/Europe/Berlin");
        // try every suffix that looks like a zone id, most specific first.
        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var start = 1; start < segments.Length; start++)
        {
            candidates.Add(string.Join('/', segments[start..]));
        }

        foreach (var candidate in candidates)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
#pragma warning disable CA1031 // Unknown zone ids are expected; fall back to local time.
            catch (Exception)
            {
                // Try the next candidate.
            }
#pragma warning restore CA1031
        }

        return null;
    }

    private static bool TryParseDuration(string value, out TimeSpan duration)
    {
        duration = default;
        var text = value.Trim();
        if (text.Length < 2)
        {
            return false;
        }

        var sign = 1;
        if (text[0] == '-')
        {
            sign = -1;
            text = text[1..];
        }
        else if (text[0] == '+')
        {
            text = text[1..];
        }

        if (text.Length < 2 || (text[0] != 'P' && text[0] != 'p'))
        {
            return false;
        }

        var weeks = 0;
        var days = 0;
        var hours = 0;
        var minutes = 0;
        var seconds = 0;
        var inTime = false;
        var number = new StringBuilder();

        foreach (var character in text[1..])
        {
            if (char.IsAsciiDigit(character))
            {
                number.Append(character);
                continue;
            }

            // The "T" designator separates date and time parts and never carries a number.
            if (character is 'T' or 't')
            {
                if (number.Length != 0 || inTime)
                {
                    return false;
                }

                inTime = true;
                continue;
            }

            if (number.Length == 0)
            {
                return false;
            }

            var amount = int.Parse(number.ToString(), CultureInfo.InvariantCulture);
            number.Clear();

            switch (char.ToUpperInvariant(character))
            {
                case 'W':
                    weeks = amount;
                    break;
                case 'D':
                    days = amount;
                    break;

                case 'H' when inTime:
                    hours = amount;
                    break;
                case 'M' when inTime:
                    minutes = amount;
                    break;
                case 'S' when inTime:
                    seconds = amount;
                    break;
                default:
                    return false;
            }
        }

        if (number.Length != 0)
        {
            return false;
        }

        duration = new TimeSpan((weeks * 7) + days, hours, minutes, seconds) * sign;
        return true;
    }

    private static bool TryParseRecurrence(string value, out Recurrence? recurrence)
    {
        recurrence = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string? frequency = null;
        var interval = 1;
        int? count = null;
        DateTimeOffset? untilInstant = null;
        DateOnly? untilDate = null;

        foreach (var part in SplitUnquoted(value, ';'))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = part[..separator].Trim().ToUpperInvariant();
            var rawValue = part[(separator + 1)..].Trim();
            switch (key)
            {
                case "FREQ":
                    frequency = rawValue.ToUpperInvariant();
                    break;
                case "INTERVAL":
                    if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInterval) && parsedInterval > 0)
                    {
                        interval = parsedInterval;
                    }

                    break;
                case "COUNT":
                    if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount) && parsedCount > 0)
                    {
                        count = parsedCount;
                    }

                    break;
                case "UNTIL":
                    if (TryParseUntil(rawValue, out var parsedUntilInstant, out var parsedUntilDate))
                    {
                        untilInstant = parsedUntilInstant;
                        untilDate = parsedUntilDate;
                    }

                    break;
            }
        }

        if (frequency is not ("DAILY" or "WEEKLY"))
        {
            return false;
        }

        recurrence = new Recurrence(frequency, interval, count, untilInstant, untilDate);
        return true;
    }

    private static bool TryParseUntil(string value, out DateTimeOffset? untilInstant, out DateOnly? untilDate)
    {
        untilInstant = null;
        untilDate = null;

        var text = value.Trim();
        if (text.Length == 8 && text.All(char.IsAsciiDigit))
        {
            if (TryParseDate(text, out var date))
            {
                untilDate = date;
                return true;
            }

            return false;
        }

        if (!TryParseDateTime(text, EmptyParameters, out var instant, out var isAllDay, out var parsedDate))
        {
            return false;
        }

        if (isAllDay)
        {
            untilDate = parsedDate;
        }
        else
        {
            untilInstant = instant;
        }

        return true;
    }

    private static List<string> UnfoldLines(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = new List<string>();
        foreach (var line in normalized.Split('\n'))
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                if (lines.Count > 0)
                {
                    lines[^1] += line[1..];
                }

                continue;
            }

            lines.Add(line);
        }

        return lines;
    }

    private static bool IsComponent(string token, string keyword, string component) =>
        token.StartsWith(keyword + ":", StringComparison.OrdinalIgnoreCase) &&
        token[(keyword.Length + 1)..].Trim().Equals(component, StringComparison.OrdinalIgnoreCase);

    private static List<string> SplitUnquoted(string text, char separator)
    {
        var parts = new List<string>();
        var start = 0;
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (character == separator && !quoted)
            {
                parts.Add(text[start..index]);
                start = index + 1;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    private static int FindValueSeparator(string line)
    {
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (character == ':' && !quoted)
            {
                return index;
            }
        }

        return -1;
    }

    private static string Unescape(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\\' && index + 1 < value.Length)
            {
                index++;
                builder.Append(value[index] switch
                {
                    'n' or 'N' => '\n',
                    ',' => ',',
                    ';' => ';',
                    '\\' => '\\',
                    _ => value[index],
                });
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyParameters =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private sealed class ContentLine
    {
        private ContentLine(string name, Dictionary<string, string> parameters, string value)
        {
            Name = name;
            Parameters = parameters;
            Value = value;
        }

        public string Name { get; }

        public IReadOnlyDictionary<string, string> Parameters { get; }

        public string Value { get; }

        public static ContentLine? TryParse(string line)
        {
            var separator = FindValueSeparator(line);
            if (separator <= 0)
            {
                return null;
            }

            var head = line[..separator];
            var value = line[(separator + 1)..];
            var segments = SplitUnquoted(head, ';');
            var name = segments[0].Trim();

            var groupSeparator = name.LastIndexOf('.');
            if (groupSeparator >= 0)
            {
                name = name[(groupSeparator + 1)..];
            }

            if (name.Length == 0)
            {
                return null;
            }

            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 1; index < segments.Count; index++)
            {
                var segment = segments[index];
                var equals = segment.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                var key = segment[..equals].Trim();
                var parameterValue = segment[(equals + 1)..].Trim();
                if (parameterValue.Length >= 2 && parameterValue[0] == '"' && parameterValue[^1] == '"')
                {
                    parameterValue = parameterValue[1..^1];
                }

                parameters[key] = parameterValue;
            }

            return new ContentLine(name.ToUpperInvariant(), parameters, value);
        }
    }

    private sealed record Recurrence(
        string Frequency,
        int Interval,
        int? Count,
        DateTimeOffset? UntilInstant,
        DateOnly? UntilDate);
}
