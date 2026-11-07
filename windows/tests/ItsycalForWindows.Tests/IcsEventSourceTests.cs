using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Models;
using ItsycalForWindows.Services;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for <see cref="IcsEventSource"/>: folder discovery, lazy folder creation, range filtering,
/// the last-write-time/length cache, deterministic ordering and tolerance for unreadable or
/// malformed files.
/// </summary>
public sealed class IcsEventSourceTests
{
    [Fact]
    public async Task GetEventsAsync_ReadsIcsFilesFromFolder()
    {
        using var folder = new TempIcsFolder();
        folder.Write("a.ics", IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:A"));
        folder.Write("b.ics", IcsTestData.Event("DTSTART:20261109T090000Z", "SUMMARY:B"));

        var source = new IcsEventSource(folder.Path);
        var events = await source.GetEventsAsync(TestDates.Day(2026, 11, 1), TestDates.Day(2026, 12, 1));

        Assert.Equal(2, events.Count);
        Assert.Equal(new[] { "B", "A" }, events.Select(calendarEvent => calendarEvent.Subject).ToArray());
    }

    [Fact]
    public async Task GetEventsAsync_MissingFolderIsCreatedLazily()
    {
        using var folder = new TempIcsFolder();
        var nested = folder.Combine("nested", "calendars");
        var source = new IcsEventSource(nested);

        Assert.False(Directory.Exists(nested));

        var events = await source.GetEventsAsync(TestDates.Day(2026, 11, 1), TestDates.Day(2026, 12, 1));

        Assert.Empty(events);
        Assert.True(Directory.Exists(nested));
    }

    [Fact]
    public async Task GetEventsAsync_EmptyRangeDoesNotTouchTheFolder()
    {
        using var folder = new TempIcsFolder();
        var nested = folder.Combine("nested");
        var source = new IcsEventSource(nested);
        var day = TestDates.Day(2026, 11, 10);

        var events = await source.GetEventsAsync(day, day);

        Assert.Empty(events);
        Assert.False(Directory.Exists(nested));
    }

    [Fact]
    public async Task GetEventsAsync_FiltersEventsToTheRequestedRange()
    {
        using var folder = new TempIcsFolder();
        folder.Write("cal.ics", IcsTestData.CalendarWithEvents(
        [
            ["DTSTART;VALUE=DATE:20261101", "SUMMARY:Before"],
            ["DTSTART;VALUE=DATE:20261110", "SUMMARY:Inside"],
            ["DTSTART;VALUE=DATE:20261120", "SUMMARY:After"],
        ]));

        var source = new IcsEventSource(folder.Path);
        var events = await source.GetEventsAsync(TestDates.Day(2026, 11, 5), TestDates.Day(2026, 11, 15));

        Assert.Equal(1, events.Count);
        Assert.Equal("Inside", events[0].Subject);
    }

    [Fact]
    public async Task GetEventsAsync_ExpandsRecurrenceInsideTheRange()
    {
        using var folder = new TempIcsFolder();
        folder.Write("recurring.ics", IcsTestData.Event(
            "DTSTART;VALUE=DATE:20261110",
            "RRULE:FREQ=DAILY;COUNT=10",
            "SUMMARY:Daily"));

        var source = new IcsEventSource(folder.Path);
        var events = await source.GetEventsAsync(TestDates.Day(2026, 11, 12), TestDates.Day(2026, 11, 16));

        Assert.Equal(4, events.Count);
        Assert.Equal(TestDates.Day(2026, 11, 12), events[0].StartDate);
        Assert.Equal(TestDates.Day(2026, 11, 15), events[^1].StartDate);
    }

    [Fact]
    public async Task GetEventsAsync_PicksUpFileChangesAndCachesUnchangedFiles()
    {
        using var folder = new TempIcsFolder();
        var path = folder.Write("cal.ics", IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:First"));
        var source = new IcsEventSource(folder.Path);
        var rangeStart = TestDates.Day(2026, 11, 1);
        var rangeEnd = TestDates.Day(2026, 12, 1);

        var firstRead = await source.GetEventsAsync(rangeStart, rangeEnd);
        Assert.Equal(1, firstRead.Count);

        var cachedRead = await source.GetEventsAsync(rangeStart, rangeEnd);
        Assert.Equal(1, cachedRead.Count);

        folder.Write("cal.ics", IcsTestData.CalendarWithEvents(
        [
            ["DTSTART:20261110T090000Z", "SUMMARY:First"],
            ["DTSTART:20261111T090000Z", "SUMMARY:Second"],
        ]));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        var updatedRead = await source.GetEventsAsync(rangeStart, rangeEnd);
        Assert.Equal(2, updatedRead.Count);
        Assert.Equal(new[] { "First", "Second" }, updatedRead.Select(calendarEvent => calendarEvent.Subject).ToArray());
    }

    [Fact]
    public async Task GetEventsAsync_IgnoresFilesThatAreNotIcs()
    {
        using var folder = new TempIcsFolder();
        folder.Write("notes.txt", IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:Not an ics file"));

        var source = new IcsEventSource(folder.Path);
        var events = await source.GetEventsAsync(TestDates.Day(2026, 11, 1), TestDates.Day(2026, 12, 1));

        Assert.Empty(events);
    }

    [Fact]
    public async Task GetEventsAsync_MalformedFileIsIgnoredAndOtherFilesStillRead()
    {
        using var folder = new TempIcsFolder();
        folder.Write("bad.ics", "this is not an iCalendar file");
        folder.Write("good.ics", IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:Good"));

        var source = new IcsEventSource(folder.Path);
        var events = await source.GetEventsAsync(TestDates.Day(2026, 11, 1), TestDates.Day(2026, 12, 1));

        Assert.Equal(1, events.Count);
        Assert.Equal("Good", events[0].Subject);
    }

    [Fact]
    public async Task GetEventsAsync_UnreadableFileIsSkipped()
    {
        using var folder = new TempIcsFolder();
        var lockedPath = folder.Write("locked.ics", IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:Locked"));
        folder.Write("good.ics", IcsTestData.Event("DTSTART:20261111T090000Z", "SUMMARY:Good"));

        using var stream = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);
        var source = new IcsEventSource(folder.Path);
        var events = await source.GetEventsAsync(TestDates.Day(2026, 11, 1), TestDates.Day(2026, 12, 1));

        Assert.Equal(1, events.Count);
        Assert.Equal("Good", events[0].Subject);
    }

    [Fact]
    public async Task GetEventsAsync_PreCancelledTokenReturnsEmpty()
    {
        using var folder = new TempIcsFolder();
        folder.Write("cal.ics", IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:A"));
        var source = new IcsEventSource(folder.Path);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var events = await source.GetEventsAsync(
            TestDates.Day(2026, 11, 1),
            TestDates.Day(2026, 12, 1),
            cancellation.Token);

        Assert.Empty(events);
    }

    [Fact]
    public async Task GetEventsAsync_EmptyFolderReturnsEmpty()
    {
        using var folder = new TempIcsFolder();
        var source = new IcsEventSource(folder.Path);

        var events = await source.GetEventsAsync(TestDates.Day(2026, 11, 1), TestDates.Day(2026, 12, 1));

        Assert.Empty(events);
    }

    [Fact]
    public void DefaultDirectory_IsTheLocalAppDataCalendarsFolder()
    {
        var directory = IcsEventSource.DefaultDirectory;

        Assert.Contains("ItsycalForWindows", directory);
        Assert.True(directory.EndsWith(IcsEventSource.FolderName, StringComparison.OrdinalIgnoreCase));
    }
}
