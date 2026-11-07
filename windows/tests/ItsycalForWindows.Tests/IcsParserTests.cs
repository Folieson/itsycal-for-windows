using System;
using System.Collections.Generic;
using System.Linq;
using ItsycalForWindows.Models;
using ItsycalForWindows.Services;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for <see cref="IcsParser"/>: all-day and timed values, UTC/floating/TZID times, folded
/// and escaped text, DURATION, online-meeting links, the basic recurrence subset, range filtering
/// and tolerance for malformed input.
/// </summary>
public sealed class IcsParserTests
{
    private const string Berlin = "Europe/Berlin";

    [Fact]
    public void Parse_AllDayEventFromDateValues()
    {
        var ics = IcsTestData.Event(
            "UID:all-day",
            "DTSTART;VALUE=DATE:20261110",
            "DTEND;VALUE=DATE:20261111",
            "SUMMARY:Holiday");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        var calendarEvent = events[0];
        Assert.True(calendarEvent.IsAllDay);
        Assert.Equal("Holiday", calendarEvent.Subject);
        Assert.Equal(TestDates.LocalMidnight(TestDates.Day(2026, 11, 10)), calendarEvent.Start);
        Assert.Equal(TestDates.LocalMidnight(TestDates.Day(2026, 11, 11)), calendarEvent.End);
        Assert.Equal(TestDates.Day(2026, 11, 10), calendarEvent.EndDateInclusive);
    }

    [Fact]
    public void Parse_AllDayEventWithoutDtEnd_LastsOneDay()
    {
        var ics = IcsTestData.Event("DTSTART;VALUE=DATE:20261110", "SUMMARY:All hands");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        Assert.True(events[0].IsAllDay);
        Assert.Equal(TestDates.Day(2026, 11, 10), events[0].StartDate);
        Assert.Equal(TestDates.Day(2026, 11, 10), events[0].EndDateInclusive);
    }

    [Fact]
    public void Parse_UtcDateTimeUsesUtcOffset()
    {
        var ics = IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:Standup");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        var calendarEvent = events[0];
        Assert.False(calendarEvent.IsAllDay);
        Assert.Equal(TimeSpan.Zero, calendarEvent.Start.Offset);
        Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0), calendarEvent.Start.UtcDateTime);
    }

    [Fact]
    public void Parse_TzidResolvesNamedZone()
    {
        var ics = IcsTestData.Event(
            $"DTSTART;TZID={Berlin}:20261110T090000",
            "SUMMARY:Berlin standup");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        var start = events[0].Start;
        Assert.Equal(TimeSpan.FromHours(1), start.Offset);
        Assert.Equal(new DateTime(2026, 11, 10, 8, 0, 0), start.UtcDateTime);
    }

    [Fact]
    public void Parse_TzidWithVendorPrefixIsResolved()
    {
        var ics = IcsTestData.Event(
            "DTSTART;TZID=/mozilla.org/20050126_1/Europe/Berlin:20261110T090000",
            "SUMMARY:Prefixed zone");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        Assert.Equal(TimeSpan.FromHours(1), events[0].Start.Offset);
    }

    [Fact]
    public void Parse_UnknownTzidFallsBackToLocalTime()
    {
        var ics = IcsTestData.Event(
            "DTSTART;TZID=Unknown/Zone:20261110T090000",
            "SUMMARY:Fallback");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0), events[0].Start.LocalDateTime);
    }

    [Fact]
    public void Parse_FloatingDateTimeUsesLocalTime()
    {
        var ics = IcsTestData.Event("DTSTART:20261110T090000", "SUMMARY:Floating");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0), events[0].Start.LocalDateTime);
    }

    [Fact]
    public void Parse_FoldedSummaryIsUnfolded()
    {
        var ics = IcsTestData.Event(
            "DTSTART:20261110T090000Z",
            "SUMMARY:Design review with",
            "  the team");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        Assert.Equal("Design review with the team", events[0].Subject);
    }

    [Fact]
    public void Parse_FoldedLinesWithTabAreUnfolded()
    {
        var ics = IcsTestData.Event(
            "DTSTART:20261110T090000Z",
            "SUMMARY:Sprint",
            "\tplanning");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        Assert.Equal("Sprintplanning", events[0].Subject);
    }

    [Fact]
    public void Parse_TextEscapingIsDecoded()
    {
        var ics = IcsTestData.Event(
            "DTSTART:20261110T090000Z",
            @"SUMMARY:Review\, planning\nwith the team",
            @"LOCATION:Room 1\; Floor 2",
            @"DESCRIPTION:First line\nSecond line with a \\ backslash");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        Assert.Equal("Review, planning\nwith the team", events[0].Subject);
        Assert.Equal("Room 1; Floor 2", events[0].Location);
        Assert.Equal("First line\nSecond line with a \\ backslash", events[0].Details);
    }

    [Fact]
    public void Parse_DurationPropertyIsApplied()
    {
        var ics = IcsTestData.Event(
            "DTSTART:20261110T100000Z",
            "DURATION:PT45M",
            "SUMMARY:Review");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        Assert.Equal(TimeSpan.FromMinutes(45), events[0].End - events[0].Start);
        Assert.Equal(new DateTime(2026, 11, 10, 10, 45, 0), events[0].End.UtcDateTime);
    }

    [Fact]
    public void Parse_DurationDaysForAllDayEvent()
    {
        var ics = IcsTestData.Event(
            "DTSTART;VALUE=DATE:20261110",
            "DURATION:P3D",
            "SUMMARY:Conference");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        Assert.True(events[0].IsAllDay);
        Assert.Equal(TestDates.Day(2026, 11, 10), events[0].StartDate);
        Assert.Equal(TestDates.Day(2026, 11, 12), events[0].EndDateInclusive);
    }

    [Fact]
    public void Parse_DescriptionLocationAndUrlAreMapped()
    {
        var ics = IcsTestData.Event(
            "DTSTART:20261110T090000Z",
            "SUMMARY:Planning",
            "DESCRIPTION:Bring the roadmap",
            "LOCATION:Room A",
            "URL:https://example.com/meet");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        Assert.Equal("Bring the roadmap", events[0].Details);
        Assert.Equal("Room A", events[0].Location);
        Assert.Equal("https://example.com/meet", events[0].OnlineMeetingLink);
        Assert.True(events[0].HasOnlineMeetingLink);
    }

    [Fact]
    public void Parse_ConferenceUriWinsOverUrl()
    {
        var ics = IcsTestData.Event(
            "DTSTART:20261110T090000Z",
            "SUMMARY:Weekly sync",
            "CONFERENCE;VALUE=URI;FEATURE=VIDEO:https://teams.example/join",
            "URL:https://example.com/other");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        Assert.Equal("https://teams.example/join", events[0].OnlineMeetingLink);
    }

    [Fact]
    public void Parse_TeamsMeetingUrlIsUsedAsFallback()
    {
        var ics = IcsTestData.Event(
            "DTSTART:20261110T090000Z",
            "SUMMARY:Teams call",
            "X-MICROSOFT-SKYPETEAMSMEETINGURL:https://teams.example/meeting",
            "URL:https://example.com/other");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        Assert.Equal("https://teams.example/meeting", events[0].OnlineMeetingLink);
    }

    [Fact]
    public void Parse_PropertyGroupPrefixIsIgnored()
    {
        var ics = IcsTestData.Event(
            "DTSTART:20261110T090000Z",
            "ITEM1.SUMMARY:Grouped summary");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        Assert.Equal("Grouped summary", events[0].Subject);
    }

    [Fact]
    public void Parse_ZeroLengthTimedEventHasNoDuration()
    {
        var ics = IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:Point");

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        Assert.Equal(events[0].Start, events[0].End);
        Assert.Equal(TestDates.Day(2026, 11, 10), events[0].EndDateInclusive);
    }

    [Fact]
    public void Parse_DailyRecurrenceExpandsWithinWindow()
    {
        var ics = IcsTestData.Event(
            "DTSTART:20261110T090000Z",
            "RRULE:FREQ=DAILY;COUNT=5",
            "SUMMARY:Daily standup");

        var events = IcsParser.Parse(ics, TestDates.Day(2026, 11, 10), TestDates.Day(2026, 11, 15));

        Assert.Equal(5, events.Count);
        Assert.Equal(
            new[]
            {
                "2026-11-10T09:00Z",
                "2026-11-11T09:00Z",
                "2026-11-12T09:00Z",
                "2026-11-13T09:00Z",
                "2026-11-14T09:00Z",
            },
            IcsTestData.UtcStarts(events));
    }

    [Fact]
    public void Parse_WeeklyRecurrenceHonoursInterval()
    {
        var ics = IcsTestData.Event(
            "DTSTART:20261110T090000Z",
            "RRULE:FREQ=WEEKLY;INTERVAL=2;COUNT=3",
            "SUMMARY:Biweekly review");

        var events = IcsParser.Parse(ics, TestDates.Day(2026, 11, 1), TestDates.Day(2026, 12, 31));

        Assert.Equal(3, events.Count);
        Assert.Equal(
            new[] { "2026-11-10T09:00Z", "2026-11-24T09:00Z", "2026-12-08T09:00Z" },
            IcsTestData.UtcStarts(events));
    }

    [Fact]
    public void Parse_RecurrenceUntilStopsTheSeries()
    {
        var ics = IcsTestData.Event(
            "DTSTART:20261110T090000Z",
            "RRULE:FREQ=DAILY;UNTIL=20261112T090000Z",
            "SUMMARY:Until series");

        var events = IcsParser.Parse(ics, TestDates.Day(2026, 11, 1), TestDates.Day(2026, 12, 1));

        Assert.Equal(3, events.Count);
        Assert.Equal(
            new[] { "2026-11-10T09:00Z", "2026-11-11T09:00Z", "2026-11-12T09:00Z" },
            IcsTestData.UtcStarts(events));
    }

    [Fact]
    public void Parse_UnboundedDailyRecurrenceRespectsHardCap()
    {
        var ics = IcsTestData.Event(
            "DTSTART;VALUE=DATE:20260101",
            "RRULE:FREQ=DAILY",
            "SUMMARY:Unbounded daily");

        var events = IcsParser.Parse(ics, TestDates.Day(2026, 1, 1), TestDates.Day(2028, 1, 1));

        Assert.Equal(IcsParser.MaxRecurrenceOccurrences, events.Count);
    }

    [Fact]
    public void Parse_RecurrenceSkipsAheadToTheWindow()
    {
        var ics = IcsTestData.Event(
            "DTSTART;VALUE=DATE:20200101",
            "RRULE:FREQ=DAILY",
            "SUMMARY:Old series");

        var events = IcsParser.Parse(ics, TestDates.Day(2026, 11, 1), TestDates.Day(2026, 11, 5));

        Assert.Equal(4, events.Count);
        Assert.Equal(
            new[]
            {
                TestDates.LocalMidnight(TestDates.Day(2026, 11, 1)),
                TestDates.LocalMidnight(TestDates.Day(2026, 11, 2)),
                TestDates.LocalMidnight(TestDates.Day(2026, 11, 3)),
                TestDates.LocalMidnight(TestDates.Day(2026, 11, 4)),
            },
            events.Select(calendarEvent => calendarEvent.Start).ToArray());
    }

    [Fact]
    public void Parse_ExdateExcludesMatchingOccurrence()
    {
        var ics = IcsTestData.Event(
            "DTSTART:20261110T090000Z",
            "RRULE:FREQ=DAILY;COUNT=3",
            "EXDATE:20261111T090000Z",
            "SUMMARY:Except one");

        var events = IcsParser.Parse(ics, TestDates.Day(2026, 11, 1), TestDates.Day(2026, 12, 1));

        Assert.Equal(2, events.Count);
        Assert.Equal(new[] { "2026-11-10T09:00Z", "2026-11-12T09:00Z" }, IcsTestData.UtcStarts(events));
    }

    [Fact]
    public void Parse_UnsupportedFrequencyFallsBackToSingleOccurrence()
    {
        var ics = IcsTestData.Event(
            "DTSTART:20261110T090000Z",
            "RRULE:FREQ=MONTHLY;COUNT=5",
            "SUMMARY:Monthly");

        var events = IcsParser.Parse(ics, TestDates.Day(2026, 11, 1), TestDates.Day(2026, 12, 1));

        Assert.Equal(1, events.Count);
        Assert.Equal("2026-11-10T09:00Z", IcsTestData.UtcStarts(events)[0]);
    }

    [Fact]
    public void Parse_MalformedEventIsSkippedAndValidOnesKept()
    {
        var ics = IcsTestData.CalendarWithEvents(
        [
            ["DTSTART:not-a-date", "SUMMARY:Broken"],
            ["DTSTART:20261110T090000Z", "SUMMARY:Valid"],
        ]);

        var events = IcsParser.Parse(ics);

        Assert.Equal(1, events.Count);
        Assert.Equal("Valid", events[0].Subject);
    }

    [Fact]
    public void Parse_MalformedInputReturnsEmptyListWithoutThrowing()
    {
        Assert.Empty(IcsParser.Parse(null));
        Assert.Empty(IcsParser.Parse(string.Empty));
        Assert.Empty(IcsParser.Parse("this is not a calendar"));
        Assert.Empty(IcsParser.Parse(IcsTestData.Calendar("BEGIN:VEVENT", "DTSTART:20261110T090000Z")));
    }

    [Fact]
    public void Parse_MultipleEventsAreSortedByStart()
    {
        var ics = IcsTestData.CalendarWithEvents(
        [
            ["DTSTART:20261112T090000Z", "SUMMARY:Later"],
            ["DTSTART:20261110T090000Z", "SUMMARY:Earlier"],
            ["DTSTART:20261111T090000Z", "SUMMARY:Middle"],
        ]);

        var events = IcsParser.Parse(ics);

        Assert.Equal(new[] { "Earlier", "Middle", "Later" }, events.Select(calendarEvent => calendarEvent.Subject).ToArray());
    }

    [Fact]
    public void Parse_RangeFilterKeepsOverlappingEventsOnly()
    {
        var ics = IcsTestData.CalendarWithEvents(
        [
            ["DTSTART;VALUE=DATE:20261101", "SUMMARY:Before"],
            ["DTSTART;VALUE=DATE:20261110", "SUMMARY:Inside"],
            ["DTSTART;VALUE=DATE:20261120", "SUMMARY:After"],
        ]);

        var events = IcsParser.Parse(ics, TestDates.Day(2026, 11, 5), TestDates.Day(2026, 11, 15));

        Assert.Equal(1, events.Count);
        Assert.Equal("Inside", events[0].Subject);
    }

    [Fact]
    public void Parse_MultiDayEventOverlappingWindowIsIncluded()
    {
        var ics = IcsTestData.Event(
            "DTSTART;VALUE=DATE:20261108",
            "DTEND;VALUE=DATE:20261113",
            "SUMMARY:Trip");

        var events = IcsParser.Parse(ics, TestDates.Day(2026, 11, 10), TestDates.Day(2026, 11, 11));

        Assert.Equal(1, events.Count);
        Assert.Equal(TestDates.Day(2026, 11, 8), events[0].StartDate);
        Assert.Equal(TestDates.Day(2026, 11, 12), events[0].EndDateInclusive);
    }

    [Fact]
    public void Parse_LfAndCrLfLineEndingsProduceTheSameEvents()
    {
        var crlf = IcsTestData.Event(
            "UID:same",
            "DTSTART;VALUE=DATE:20261110",
            "SUMMARY:Holiday");
        var lf = IcsTestData.WithLfLineEndings(crlf);

        var crlfEvents = IcsParser.Parse(crlf);
        var lfEvents = IcsParser.Parse(lf);

        Assert.Equal(1, crlfEvents.Count);
        Assert.Equal(crlfEvents[0], lfEvents[0]);
    }
}
