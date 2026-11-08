using System;
using System.Collections.Generic;
using System.Linq;
using ItsycalForWindows.Models;

namespace ItsycalForWindows.Services;

/// <summary>
/// Pure date arithmetic over <see cref="CalendarEvent"/> values: local-day projection, clipping
/// to a visible range, grouping and per-day counting.
/// </summary>
/// <remarks>
/// <para>
/// Every range passed to these helpers is half-open, <c>[fromInclusive, toExclusive)</c>, the
/// same contract as <see cref="IEventSource.GetEventsAsync"/>. An event that ends exactly at
/// local midnight does not occupy the following day; an event that merely crosses midnight does.
/// </para>
/// <para>
/// The helpers are deterministic and do no I/O, so the pages can call them while rendering.
/// Times are converted with the local time zone because the calendar grid is built from local
/// dates.
/// </para>
/// </remarks>
public static class EventRange
{
    /// <summary>
    /// Converts a local calendar date to the instant of local midnight at the start of that day,
    /// which is the boundary used by every range helper and by the appointment store query.
    /// </summary>
    /// <param name="date">Local calendar date.</param>
    /// <returns>Local midnight of <paramref name="date"/> with the zone offset of that time.</returns>
    public static DateTimeOffset StartOfDayLocal(DateOnly date)
    {
        var localMidnight = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(localMidnight, TimeZoneInfo.Local.GetUtcOffset(localMidnight));
    }

    /// <summary>
    /// Returns the first and last local dates that <paramref name="calendarEvent"/> touches.
    /// The day span is derived from the event's local calendar dates
    /// (<see cref="CalendarEvent.StartDate"/> and <see cref="CalendarEvent.EndDateInclusive"/>),
    /// which is the single source of truth shared with the calendar grid; it stays correct
    /// across daylight-saving changes and for all-day events.
    /// </summary>
    /// <param name="calendarEvent">Event to inspect.</param>
    public static (DateOnly FirstDay, DateOnly LastDay)? GetLocalDaySpan(CalendarEvent calendarEvent)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);

        var firstDay = calendarEvent.StartDate;
        var lastDay = calendarEvent.EndDateInclusive;

        return lastDay < firstDay ? null : (firstDay, lastDay);
    }

    /// <summary>
    /// True when <paramref name="calendarEvent"/> overlaps the half-open visible range
    /// <c>[fromInclusive, toExclusive)</c>.
    /// </summary>
    public static bool IntersectsRange(CalendarEvent calendarEvent, DateOnly fromInclusive, DateOnly toExclusive)
    {
        if (toExclusive <= fromInclusive)
        {
            return false;
        }

        var span = GetLocalDaySpan(calendarEvent);
        return span is not null &&
            span.Value.LastDay >= fromInclusive &&
            span.Value.FirstDay < toExclusive;
    }

    /// <summary>
    /// Clips an event to the half-open range <c>[fromInclusive, toExclusive)</c> so the returned
    /// start and end never fall outside the range. Returns <c>null</c> when the event does not
    /// overlap it. All-day events stay all-day while clipped.
    /// </summary>
    public static CalendarEvent? ClipToRange(CalendarEvent calendarEvent, DateOnly fromInclusive, DateOnly toExclusive)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);

        if (!IntersectsRange(calendarEvent, fromInclusive, toExclusive))
        {
            return null;
        }

        var rangeStart = StartOfDayLocal(fromInclusive);
        var rangeEnd = StartOfDayLocal(toExclusive);

        if (calendarEvent.End <= calendarEvent.Start)
        {
            // Zero-length event: keep it unchanged when its instant is inside the range.
            return calendarEvent.Start >= rangeStart && calendarEvent.Start < rangeEnd
                ? calendarEvent
                : null;
        }

        var start = calendarEvent.Start > rangeStart ? calendarEvent.Start : rangeStart;
        var end = calendarEvent.End < rangeEnd ? calendarEvent.End : rangeEnd;

        return end <= start ? null : calendarEvent with { Start = start, End = end };
    }

    /// <summary>
    /// Counts how many events overlap each local date in <c>[fromInclusive, toExclusive)</c>.
    /// The result contains an entry only for dates that have at least one event, so the caller can
    /// pass it straight to <see cref="CalendarMonth.WithEventCounts"/>.
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, int> CountPerDay(
        IEnumerable<CalendarEvent> events,
        DateOnly fromInclusive,
        DateOnly toExclusive)
    {
        ArgumentNullException.ThrowIfNull(events);

        var counts = new Dictionary<DateOnly, int>();
        if (toExclusive <= fromInclusive)
        {
            return counts;
        }

        foreach (var calendarEvent in events)
        {
            if (calendarEvent is null)
            {
                continue;
            }

            var span = GetLocalDaySpan(calendarEvent);
            if (span is null)
            {
                continue;
            }

            var first = span.Value.FirstDay < fromInclusive ? fromInclusive : span.Value.FirstDay;
            var last = span.Value.LastDay >= toExclusive ? toExclusive.AddDays(-1) : span.Value.LastDay;

            for (var day = first; day <= last; day = day.AddDays(1))
            {
                counts[day] = counts.TryGetValue(day, out var count) ? count + 1 : 1;
            }
        }

        return counts;
    }

    /// <summary>
    /// Groups the events overlapping each local date in <c>[fromInclusive, toExclusive)</c>.
    /// Only dates with at least one event are present and each group is ordered by start time.
    /// Dates whose leading/trailing cells belong to the visible grid but not to the requested
    /// range are absent.
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, IReadOnlyList<CalendarEvent>> GroupByDay(
        IEnumerable<CalendarEvent> events,
        DateOnly fromInclusive,
        DateOnly toExclusive)
    {
        ArgumentNullException.ThrowIfNull(events);

        var groups = new Dictionary<DateOnly, List<CalendarEvent>>();
        if (toExclusive <= fromInclusive)
        {
            return new Dictionary<DateOnly, IReadOnlyList<CalendarEvent>>();
        }

        foreach (var calendarEvent in events)
        {
            if (calendarEvent is null)
            {
                continue;
            }

            var span = GetLocalDaySpan(calendarEvent);
            if (span is null)
            {
                continue;
            }

            var first = span.Value.FirstDay < fromInclusive ? fromInclusive : span.Value.FirstDay;
            var last = span.Value.LastDay >= toExclusive ? toExclusive.AddDays(-1) : span.Value.LastDay;

            for (var day = first; day <= last; day = day.AddDays(1))
            {
                if (!groups.TryGetValue(day, out var dayEvents))
                {
                    dayEvents = [];
                    groups.Add(day, dayEvents);
                }

                dayEvents.Add(calendarEvent);
            }
        }

        var result = new Dictionary<DateOnly, IReadOnlyList<CalendarEvent>>(groups.Count);
        foreach (var (day, dayEvents) in groups)
        {
            result.Add(day, dayEvents.OrderBy(static e => e.Start).ToArray());
        }

        return result;
    }
}
