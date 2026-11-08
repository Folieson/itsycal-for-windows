using ItsycalForWindows.Models;
using ItsycalForWindows.Services;

namespace ItsycalForWindows.Providers;

/// <summary>
/// Normalized event returned by a provider. It wraps the Core calendar model and carries the
/// provider's remote identity so a future cache can update or delete individual events without
/// re-fetching the whole range.
/// </summary>
/// <remarks>
/// <see cref="RemoteId"/> and <see cref="CalendarId"/> are optional: the ICS parser does not expose
/// a UID today, and a provider may not have one for a local file. They are identifiers, never
/// credentials. The event title, details and location are calendar content and must not be logged.
/// </remarks>
public sealed record ProviderEvent
{
    /// <summary>Wraps a Core event. Throws when <paramref name="calendarEvent"/> is null.</summary>
    public ProviderEvent(CalendarEvent calendarEvent, string? remoteId = null, string? calendarId = null)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);

        Event = calendarEvent;
        RemoteId = string.IsNullOrWhiteSpace(remoteId) ? null : remoteId.Trim();
        CalendarId = string.IsNullOrWhiteSpace(calendarId) ? null : calendarId.Trim();
    }

    /// <summary>The normalized Core event consumed by the calendar surfaces.</summary>
    public CalendarEvent Event { get; }

    /// <summary>Provider-specific event id, when the provider exposes one.</summary>
    public string? RemoteId { get; }

    /// <summary>Provider-specific calendar/collection id, when it differs from the account.</summary>
    public string? CalendarId { get; }

    /// <summary>Creates a provider event from a Core event.</summary>
    public static ProviderEvent FromCalendarEvent(
        CalendarEvent calendarEvent,
        string? remoteId = null,
        string? calendarId = null) =>
        new(calendarEvent, remoteId, calendarId);

    /// <summary>True when the event overlaps the half-open range.</summary>
    public bool IsInRange(ProviderEventRange range) => range.Contains(Event);
}
