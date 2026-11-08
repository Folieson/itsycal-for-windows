using System;
using System.Collections.Generic;

namespace ItsycalForWindows.Models;

/// <summary>
/// One week of the month grid: seven consecutive day cells in reading order plus the
/// ISO-8601 week number of the week they belong to.
/// </summary>
/// <remarks>
/// ISO-8601 weeks start on Monday and are numbered by the week containing the first
/// Thursday. A Sunday-start grid therefore reports the number of the ISO week its Monday
/// cell belongs to (the Monday and Thursday of a row are always in the same ISO week),
/// which is also how the upstream ItsyCal calendar reads the week number (it evaluates the
/// cell in the Monday column).
/// </remarks>
/// <param name="IsoWeekNumber">ISO-8601 week number, 1-53.</param>
/// <param name="Days">Exactly seven day cells in reading order.</param>
public sealed record WeekRow(int IsoWeekNumber, IReadOnlyList<CalendarDay> Days)
{
    /// <summary>Days in one grid row.</summary>
    public const int DayCount = 7;
}
