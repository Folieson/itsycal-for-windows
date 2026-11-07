using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Models;

namespace ItsycalForWindows.Services;

/// <summary>
/// Merges any number of <see cref="IEventSource"/> instances into one event stream (for example
/// the Windows appointment store plus one or more local calendars).
/// </summary>
/// <remarks>
/// <para>
/// Sources are queried concurrently and a source that throws - or violates the
/// <see cref="IEventSource"/> contract - is isolated: its failure contributes no events while the
/// other sources keep working. Results are de-duplicated by subject (case-insensitive), start and
/// end, then sorted by start so the calendar grid sees a deterministic list.
/// </para>
/// <para>
/// The merge is exposed as a static helper so it can be reused (and tested) without constructing a
/// composite source.
/// </para>
/// </remarks>
public sealed class CompositeEventSource : IEventSource
{
    private readonly IReadOnlyList<IEventSource> _sources;

    /// <summary>Creates a composite over <paramref name="sources"/>; null entries are ignored.</summary>
    public CompositeEventSource(params IEventSource[] sources)
        : this((IEnumerable<IEventSource>?)sources)
    {
    }

    /// <summary>Creates a composite over <paramref name="sources"/>; null entries are ignored.</summary>
    public CompositeEventSource(IEnumerable<IEventSource>? sources)
    {
        _sources = sources is null
            ? []
            : sources.Where(source => source is not null).ToArray();
    }

    /// <summary>The sources this composite queries, in registration order.</summary>
    public IReadOnlyList<IEventSource> Sources => _sources;

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

        var batches = await Task
            .WhenAll(_sources.Select(source => SafeGetAsync(source, fromInclusive, toExclusive, cancellationToken)))
            .ConfigureAwait(false);

        return Merge(batches);
    }

    /// <summary>
    /// Merges event batches: de-duplicates by (subject case-insensitive, start, end) keeping the
    /// first occurrence, drops null entries, and sorts by start then subject.
    /// </summary>
    public static IReadOnlyList<CalendarEvent> Merge(IEnumerable<IReadOnlyList<CalendarEvent>>? batches)
    {
        if (batches is null)
        {
            return Array.Empty<CalendarEvent>();
        }

        var merged = new List<CalendarEvent>();
        var seen = new HashSet<EventKey>();
        foreach (var batch in batches)
        {
            if (batch is null)
            {
                continue;
            }

            foreach (var calendarEvent in batch)
            {
                if (calendarEvent is null || !seen.Add(new EventKey(calendarEvent)))
                {
                    continue;
                }

                merged.Add(calendarEvent);
            }
        }

        merged.Sort(static (left, right) =>
        {
            var byStart = left.Start.CompareTo(right.Start);
            if (byStart != 0)
            {
                return byStart;
            }

            var bySubject = string.Compare(left.Subject, right.Subject, StringComparison.OrdinalIgnoreCase);
            return bySubject != 0 ? bySubject : left.End.CompareTo(right.End);
        });

        return merged;
    }

    private static async Task<IReadOnlyList<CalendarEvent>> SafeGetAsync(
        IEventSource source,
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken)
    {
#pragma warning disable CA1031 // One failing source must not take the composite down.
        try
        {
            var events = await source
                .GetEventsAsync(fromInclusive, toExclusive, cancellationToken)
                .ConfigureAwait(false);

            return events ?? Array.Empty<CalendarEvent>();
        }
        catch (Exception)
        {
            return Array.Empty<CalendarEvent>();
        }
#pragma warning restore CA1031
    }

    private readonly struct EventKey : IEquatable<EventKey>
    {
        private readonly string _subject;
        private readonly DateTimeOffset _start;
        private readonly DateTimeOffset _end;

        public EventKey(CalendarEvent calendarEvent)
        {
            _subject = calendarEvent.Subject ?? string.Empty;
            _start = calendarEvent.Start;
            _end = calendarEvent.End;
        }

        public bool Equals(EventKey other) =>
            _start == other._start &&
            _end == other._end &&
            string.Equals(_subject, other._subject, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => obj is EventKey other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(_start, _end, StringComparer.OrdinalIgnoreCase.GetHashCode(_subject));
    }
}
