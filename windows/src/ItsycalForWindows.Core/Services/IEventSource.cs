using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Models;

namespace ItsycalForWindows.Services;

/// <summary>
/// Read-only source of calendar events for a visible date range. Implementations must
/// never throw for an unavailable calendar: report an empty list instead, so the
/// calendar UI keeps working without events.
/// </summary>
public interface IEventSource
{
    /// <summary>
    /// Returns events overlapping <paramref name="fromInclusive"/> .. 
    /// <paramref name="toExclusive"/>. The range is half-open: an event that ends exactly
    /// at midnight of the end date is included only in the previous range.
    /// </summary>
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default);
}
