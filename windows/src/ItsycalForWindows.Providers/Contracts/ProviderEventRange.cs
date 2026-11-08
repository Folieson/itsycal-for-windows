using ItsycalForWindows.Models;
using ItsycalForWindows.Services;

namespace ItsycalForWindows.Providers;

/// <summary>
/// Half-open date range passed to a provider, mirroring the Core
/// <see cref="IEventSource.GetEventsAsync"/> contract: events that overlap
/// <c>[FromInclusive, ToExclusive)</c> are returned. An empty or inverted range yields no events.
/// </summary>
/// <remarks>
/// Core's <see cref="EventRange"/> is a static helper class, so it cannot be used as a parameter
/// type. This value type is the provider-facing equivalent and keeps the same half-open semantics
/// and the same <see cref="EventRange.IntersectsRange"/> overlap rule.
/// </remarks>
public readonly record struct ProviderEventRange(DateOnly FromInclusive, DateOnly ToExclusive)
{
    /// <summary>True when the range contains no day.</summary>
    public bool IsEmpty => ToExclusive <= FromInclusive;

    /// <summary>Number of days in the range, or zero when empty.</summary>
    public int DayCount => IsEmpty ? 0 : ToExclusive.DayNumber - FromInclusive.DayNumber;

    /// <summary>True when <paramref name="calendarEvent"/> overlaps the range.</summary>
    public bool Contains(CalendarEvent calendarEvent)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        return !IsEmpty && EventRange.IntersectsRange(calendarEvent, FromInclusive, ToExclusive);
    }

    /// <summary>Creates a range from local dates.</summary>
    public static ProviderEventRange Days(DateOnly fromInclusive, DateOnly toExclusive) =>
        new(fromInclusive, toExclusive);

    /// <summary>Human-readable range used only in debug output, never in user errors.</summary>
    public override string ToString() => $"[{FromInclusive:yyyy-MM-dd}, {ToExclusive:yyyy-MM-dd})";
}
