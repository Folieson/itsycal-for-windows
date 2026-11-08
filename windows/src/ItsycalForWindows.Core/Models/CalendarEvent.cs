using System;

namespace ItsycalForWindows.Models;

/// <summary>
/// One calendar event read from the local Windows appointment store. Times are kept as
/// <see cref="DateTimeOffset"/> so multi-day and all-day events keep their original
/// offsets; the UI decides how to present them.
/// </summary>
/// <param name="Subject">Event title, never null; may be empty.</param>
/// <param name="Start">Start instant.</param>
/// <param name="End">End instant (exclusive for all-day events).</param>
/// <param name="IsAllDay">True for all-day events.</param>
/// <param name="Location">Optional location text.</param>
/// <param name="OnlineMeetingLink">Optional online-meeting (Zoom/Teams/...) URL.</param>
/// <param name="Details">Optional event description/notes.</param>
public sealed record CalendarEvent(
    string Subject,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay = false,
    string? Location = null,
    string? OnlineMeetingLink = null,
    string? Details = null)
{
    /// <summary>First local calendar day the event covers.</summary>
    public DateOnly StartDate => DateOnly.FromDateTime(Start.LocalDateTime);

    /// <summary>
    /// Last local calendar day the event covers, inclusive. An end at exactly midnight is
    /// exclusive, which is how the appointment store reports all-day events and events
    /// that stop at the end of the day; a zero-length or inverted span counts as one day.
    /// </summary>
    public DateOnly EndDateInclusive
    {
        get
        {
            var startDate = StartDate;
            var lastDay = DateOnly.FromDateTime(End.LocalDateTime);
            if (End.LocalDateTime.TimeOfDay == TimeSpan.Zero && lastDay > startDate)
            {
                lastDay = lastDay.AddDays(-1);
            }

            return lastDay < startDate ? startDate : lastDay;
        }
    }

    /// <summary>True when the event spans more than one calendar day.</summary>
    public bool IsMultiDay => EndDateInclusive > StartDate;

    /// <summary>Short display text used by lists and cards.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Subject) ? "(no title)" : Subject;

    /// <summary>True when the event carries a usable http(s) meeting link.</summary>
    public bool HasOnlineMeetingLink => TryGetOnlineMeetingUri(out _);

    /// <summary>
    /// Parses <see cref="OnlineMeetingLink"/> defensively: only absolute http(s) URIs are
    /// accepted so a malformed calendar field cannot make the join button launch anything
    /// unexpected.
    /// </summary>
    public bool TryGetOnlineMeetingUri(out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(OnlineMeetingLink))
        {
            return false;
        }

        if (!Uri.TryCreate(OnlineMeetingLink.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
