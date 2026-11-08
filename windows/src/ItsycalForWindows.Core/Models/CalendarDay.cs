using System;

namespace ItsycalForWindows.Models;

/// <summary>
/// One cell of the month grid. The grid always has 42 cells (6 weeks x 7 days), so
/// leading and trailing days from adjacent months are included with
/// <see cref="IsCurrentMonth"/> set to false.
/// </summary>
/// <param name="Date">Calendar date of the cell.</param>
/// <param name="IsToday">True when the cell is the current local date.</param>
/// <param name="IsCurrentMonth">True when the date belongs to the displayed month.</param>
/// <param name="EventCount">Number of events overlapping the cell, 0 when unknown.</param>
public sealed record CalendarDay(
    DateOnly Date,
    bool IsToday,
    bool IsCurrentMonth,
    int EventCount = 0)
{
    /// <summary>True when at least one event overlaps the day.</summary>
    public bool HasEvents => EventCount > 0;
}
