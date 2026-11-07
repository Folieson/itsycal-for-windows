using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Models;
using Windows.ApplicationModel.Appointments;

namespace ItsycalForWindows.Services;

/// <summary>
/// Read-only event source backed by the local Windows appointment store
/// (<c>Windows.ApplicationModel.Appointments</c>).
/// </summary>
/// <remarks>
/// <para>
/// The store is requested with <see cref="AppointmentStoreAccessType.AllCalendarsReadOnly"/> and
/// is only ever queried, so the extension cannot change the user's calendars. Nothing leaves the
/// machine: no network calls and no telemetry.
/// </para>
/// <para>
/// <c>FindAppointmentsAsync</c> returns one <c>Appointment</c> per occurrence of a recurring
/// event (the store expands recurrence into instances), therefore recurring series need no
/// expansion here. Each occurrence is mapped to a <see cref="CalendarEvent"/> and the overlap
/// with the requested half-open range is re-checked in memory.
/// </para>
/// <para>
/// Failures never escape this class. A missing <c>appointmentsSystem</c> restricted capability,
/// a disabled calendar privacy setting, a machine without a configured account, access-denied and
/// COM errors, and cancellation all produce an empty list so that the calendar UI keeps working.
/// Only the exception type is written to the debug output - never event titles or other event
/// content.
/// </para>
/// </remarks>
internal sealed class AppointmentsEventSource : IEventSource
{
    /// <summary>
    /// How long a result for one exact range is reused. Deliberately short: a flyout reopened
    /// after this window queries the store again, so an event just created in the Calendar app
    /// shows up without restarting the extension.
    /// </summary>
    internal static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Extra days queried before the requested range so that a multi-day event starting earlier
    /// still contributes to the visible days. Bounded to keep the store query small; an event
    /// longer than this that also starts before the range is not reported.
    /// </summary>
    internal const int LookbackDays = 31;

    private readonly object _cacheLock = new();

    private IReadOnlyList<CalendarEvent> _cachedEvents = Array.Empty<CalendarEvent>();
    private DateOnly _cachedFromInclusive;
    private DateOnly _cachedToExclusive;
    private DateTimeOffset _cacheExpiresAt = DateTimeOffset.MinValue;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default)
    {
        if (toExclusive <= fromInclusive || cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<CalendarEvent>();
        }

        if (TryGetCached(fromInclusive, toExclusive, out var cached))
        {
            return cached;
        }

#pragma warning disable CA1031 // IEventSource requires that calendar failures never reach the UI.
        try
        {
            var events = await QueryAsync(fromInclusive, toExclusive, cancellationToken).ConfigureAwait(false);
#if DEBUG
            CalendarAccessProbe.Log(
                $"events: read {events.Count} for {fromInclusive:yyyy-MM-dd}..{toExclusive:yyyy-MM-dd}");
#endif
            StoreInCache(fromInclusive, toExclusive, events);
            return events;
        }
        catch (OperationCanceledException)
        {
            // Cancellation means the UI moved on. Report no data and keep the previous cache.
#if DEBUG
            CalendarAccessProbe.Log(
                $"events: read cancelled for {fromInclusive:yyyy-MM-dd}..{toExclusive:yyyy-MM-dd}");
#endif
            return Array.Empty<CalendarEvent>();
        }
        catch (Exception exception)
        {
            // UnauthorizedAccessException (capability or privacy setting), COMException (store
            // or provider failure), IOException, InvalidOperationException and WinRT projection
            // failures all mean the same thing to the UI: no events right now. The empty result
            // is cached briefly so a persistently failing store is not called on every render.
#if DEBUG
            CalendarAccessProbe.Log(
                $"events: read failed: {exception.GetType().Name} 0x{exception.HResult:X8}: {exception.Message}");
#endif
            Debug.WriteLine($"[ItsycalForWindows] Appointment store unavailable: {exception.GetType().Name}");
            StoreInCache(fromInclusive, toExclusive, Array.Empty<CalendarEvent>());
            return Array.Empty<CalendarEvent>();
        }
#pragma warning restore CA1031
    }

    private static async Task<IReadOnlyList<CalendarEvent>> QueryAsync(
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken)
    {
        var queryFrom = fromInclusive.AddDays(-LookbackDays);
        var rangeStart = EventRange.StartOfDayLocal(queryFrom);

        // One extra day so a daylight-saving change at the range boundary cannot truncate the
        // window; the exact overlap filter below trims the surplus.
        var rangeLength = TimeSpan.FromDays(toExclusive.DayNumber - queryFrom.DayNumber + 1);

        var options = new FindAppointmentsOptions();

        // Only properties that map to CalendarEvent are requested. The store does not populate
        // most properties unless their name is listed in FetchProperties.
        options.FetchProperties.Add(AppointmentProperties.Subject);
        options.FetchProperties.Add(AppointmentProperties.StartTime);
        options.FetchProperties.Add(AppointmentProperties.Duration);
        options.FetchProperties.Add(AppointmentProperties.AllDay);
        options.FetchProperties.Add(AppointmentProperties.Location);

        // Hidden calendars (for example one the user unchecked in the Calendar app) stay hidden.
        options.IncludeHidden = false;

        var store = await AppointmentManager
            .RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        var appointments = await store
            .FindAppointmentsAsync(rangeStart, rangeLength, options)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        return MapAppointments(appointments, fromInclusive, toExclusive);
    }

    private static List<CalendarEvent> MapAppointments(
        IReadOnlyList<Appointment> appointments,
        DateOnly fromInclusive,
        DateOnly toExclusive)
    {
        var rangeStart = EventRange.StartOfDayLocal(fromInclusive);
        var rangeEnd = EventRange.StartOfDayLocal(toExclusive);

        var events = new List<CalendarEvent>(appointments.Count);

        foreach (var appointment in appointments)
        {
            var start = appointment.StartTime;
            var duration = appointment.Duration;

            // The API documents a non-negative duration; treat anything else as unusable data.
            if (duration < TimeSpan.Zero)
            {
                continue;
            }

            var end = start + duration;

            // Half-open overlap: an event ending exactly at rangeStart, or starting exactly at
            // rangeEnd, belongs to the neighbouring range only. Zero-length events are kept when
            // their instant falls inside the range.
            var overlaps = end > start
                ? end > rangeStart && start < rangeEnd
                : start >= rangeStart && start < rangeEnd;

            if (!overlaps)
            {
                continue;
            }

            var subject = appointment.Subject;
            var location = appointment.Location;

            events.Add(new CalendarEvent(
                subject ?? string.Empty,
                start,
                end,
                appointment.AllDay,
                string.IsNullOrWhiteSpace(location) ? null : location!.Trim()));
        }

        events.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return events;
    }

    private bool TryGetCached(
        DateOnly fromInclusive,
        DateOnly toExclusive,
        out IReadOnlyList<CalendarEvent> events)
    {
        lock (_cacheLock)
        {
            if (_cachedFromInclusive == fromInclusive &&
                _cachedToExclusive == toExclusive &&
                DateTimeOffset.UtcNow < _cacheExpiresAt)
            {
                events = _cachedEvents;
                return true;
            }
        }

        events = Array.Empty<CalendarEvent>();
        return false;
    }

    private void StoreInCache(
        DateOnly fromInclusive,
        DateOnly toExclusive,
        IReadOnlyList<CalendarEvent> events)
    {
        lock (_cacheLock)
        {
            _cachedFromInclusive = fromInclusive;
            _cachedToExclusive = toExclusive;
            _cachedEvents = events;
            _cacheExpiresAt = DateTimeOffset.UtcNow.Add(CacheLifetime);
        }
    }
}
