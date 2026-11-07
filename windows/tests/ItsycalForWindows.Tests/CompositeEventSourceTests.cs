using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Models;
using ItsycalForWindows.Services;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for <see cref="CompositeEventSource"/>: merging, de-duplication, deterministic ordering,
/// isolation of a failing source, and the public <see cref="CompositeEventSource.Merge"/> helper.
/// </summary>
public sealed class CompositeEventSourceTests
{
    private static readonly DateOnly RangeStart = TestDates.Day(2026, 11, 1);
    private static readonly DateOnly RangeEnd = TestDates.Day(2026, 12, 1);

    [Fact]
    public async Task GetEventsAsync_MergesSourcesAndSortsByStart()
    {
        var first = new StubEventSource([Event("Second", 10, 11)]);
        var second = new StubEventSource([Event("First", 10, 9), Event("Third", 11, 9)]);
        var composite = new CompositeEventSource(first, second);

        var events = await composite.GetEventsAsync(RangeStart, RangeEnd);

        Assert.Equal(new[] { "First", "Second", "Third" }, events.Select(calendarEvent => calendarEvent.Subject).ToArray());
    }

    [Fact]
    public async Task GetEventsAsync_DeduplicatesSameSubjectStartAndEndIgnoringCase()
    {
        var first = new StubEventSource([Event("Design review", 10, 9)]);
        var second = new StubEventSource([Event("design REVIEW", 10, 9)]);
        var composite = new CompositeEventSource(first, second);

        var events = await composite.GetEventsAsync(RangeStart, RangeEnd);

        Assert.Equal(1, events.Count);
        Assert.Equal("Design review", events[0].Subject);
    }

    [Fact]
    public async Task GetEventsAsync_KeepsEventsThatDifferByStart()
    {
        var first = new StubEventSource([Event("Standup", 10, 9)]);
        var second = new StubEventSource([Event("Standup", 10, 10)]);
        var composite = new CompositeEventSource(first, second);

        var events = await composite.GetEventsAsync(RangeStart, RangeEnd);

        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task GetEventsAsync_IsolatesAFailingSource()
    {
        var failing = new StubEventSource(throws: true);
        var working = new StubEventSource([Event("Survivor", 10, 9)]);
        var composite = new CompositeEventSource(failing, working);

        var events = await composite.GetEventsAsync(RangeStart, RangeEnd);

        Assert.Equal(1, events.Count);
        Assert.Equal("Survivor", events[0].Subject);
    }

    [Fact]
    public async Task GetEventsAsync_SourceReturningNullIsTreatedAsEmpty()
    {
        var composite = new CompositeEventSource(new StubEventSource(events: null));

        var events = await composite.GetEventsAsync(RangeStart, RangeEnd);

        Assert.Empty(events);
    }

    [Fact]
    public async Task GetEventsAsync_EmptyAndInvertedRangesDoNotQuerySources()
    {
        var stub = new StubEventSource([Event("Never", 10, 9)]);
        var composite = new CompositeEventSource(stub);

        Assert.Empty(await composite.GetEventsAsync(RangeStart, RangeStart));
        Assert.Empty(await composite.GetEventsAsync(RangeEnd, RangeStart));
        Assert.Equal(0, stub.CallCount);
    }

    [Fact]
    public async Task GetEventsAsync_PreCancelledTokenDoesNotQuerySources()
    {
        var stub = new StubEventSource([Event("Never", 10, 9)]);
        var composite = new CompositeEventSource(stub);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var events = await composite.GetEventsAsync(RangeStart, RangeEnd, cancellation.Token);

        Assert.Empty(events);
        Assert.Equal(0, stub.CallCount);
    }

    [Fact]
    public void Constructor_IgnoresNullSources()
    {
        var stub = new StubEventSource([]);
        var composite = new CompositeEventSource(null!, stub);

        Assert.Equal(1, composite.Sources.Count);
        Assert.Equal(stub, composite.Sources[0]);
    }

    [Fact]
    public void Constructor_HandlesNullSourceArray()
    {
        var composite = new CompositeEventSource((IEventSource[])null!);

        Assert.Empty(composite.Sources);
    }

    [Fact]
    public void Sources_ExposesRegistrationOrder()
    {
        var first = new StubEventSource([]);
        var second = new StubEventSource([]);
        var composite = new CompositeEventSource(first, second);

        Assert.Equal(2, composite.Sources.Count);
        Assert.Equal(first, composite.Sources[0]);
        Assert.Equal(second, composite.Sources[1]);
    }

    [Fact]
    public void Merge_DeduplicatesAndSortsBatches()
    {
        IReadOnlyList<IReadOnlyList<CalendarEvent>> batches =
        [
            [Event("Later", 11, 9)],
            [Event("Earlier", 9, 9), Event("later", 11, 9)],
        ];

        var merged = CompositeEventSource.Merge(batches);

        Assert.Equal(new[] { "Earlier", "Later" }, merged.Select(calendarEvent => calendarEvent.Subject).ToArray());
    }

    [Fact]
    public void Merge_KeepsTheFirstOccurrenceContent()
    {
        var first = TestDates.Timed(
            "Standup",
            TestDates.LocalMidnight(TestDates.Day(2026, 11, 10)).AddHours(9),
            TestDates.LocalMidnight(TestDates.Day(2026, 11, 10)).AddHours(10)) with
        { Details = "from first source" };
        var duplicate = first with { Details = "from second source" };

        var merged = CompositeEventSource.Merge(new[] { new[] { first }, new[] { duplicate } });

        Assert.Equal(1, merged.Count);
        Assert.Equal("from first source", merged[0].Details);
    }

    [Fact]
    public void Merge_HandlesNullBatchesAndNullEvents()
    {
        Assert.Empty(CompositeEventSource.Merge(null));

#pragma warning disable CS8620 // Nullability of the test fixture is intentional.
        IReadOnlyList<IReadOnlyList<CalendarEvent>> batches = [null!, [Event("Kept", 10, 9)]];
#pragma warning restore CS8620
        var merged = CompositeEventSource.Merge(batches);

        Assert.Equal(1, merged.Count);
        Assert.Equal("Kept", merged[0].Subject);
    }

    private static CalendarEvent Event(string subject, int day, int hour) => TestDates.Timed(
        subject,
        TestDates.LocalMidnight(TestDates.Day(2026, 11, day)).AddHours(hour),
        TestDates.LocalMidnight(TestDates.Day(2026, 11, day)).AddHours(hour + 1));

    private sealed class StubEventSource : IEventSource
    {
        private readonly IReadOnlyList<CalendarEvent>? _events;
        private readonly bool _throws;

        public StubEventSource(IReadOnlyList<CalendarEvent>? events = null, bool throws = false)
        {
            _events = events;
            _throws = throws;
        }

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
            DateOnly fromInclusive,
            DateOnly toExclusive,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_throws)
            {
                throw new InvalidOperationException("stub source failure");
            }

            return Task.FromResult(_events ?? Array.Empty<CalendarEvent>());
        }
    }
}
