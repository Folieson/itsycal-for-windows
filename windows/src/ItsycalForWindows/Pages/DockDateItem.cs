using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Timers;
using ItsycalForWindows.Models;
using ItsycalForWindows.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ItsycalForWindows.Pages;

/// <summary>
/// Dock band button: shows the current date and opens the calendar flyout when activated. The
/// title carries the glanceable date in the configured short format; the subtitle carries today's
/// event context while the event indicator setting is on: the next event as "13:00 Lunch with the
/// team" (time + title, ellipsized when long) or the quiet "1 event"/"N events"/"No events" count.
/// The text refreshes once a minute so the Dock stays correct across midnight and as events start,
/// and the events are re-read from the shared source every few minutes.
/// </summary>
internal sealed partial class DockDateItem : ListItem, IDisposable
{
    private static readonly TimeSpan DateRefreshInterval = TimeSpan.FromMinutes(1);

    // The appointment store is queried at most this often; a reopened flyout refreshes it
    // anyway, so the dock text only needs to follow events being added or removed.
    private static readonly TimeSpan EventRefreshInterval = TimeSpan.FromMinutes(5);

    private const string SingleEventLabel = "1 event";
    private const string MultipleEventsLabel = " events";
    private const string NoEventsLabel = "No events";

    // The dock subtitle is rendered with character ellipsis at ~100 DIP; keep the payload short
    // enough that the time and the beginning of the title stay readable.
    private const int MaxSubtitleLength = 34;

    private readonly object _sync = new();
    private readonly SettingsManager _settings;
    private readonly IEventSource _eventSource;
    private readonly Timer _timer;

    private string? _renderedTitle;
    private string? _renderedSubtitle;
    private bool _eventsLoaded;
    private bool _isLoadingEvents;
    private DateOnly _eventsDate;
    private IReadOnlyList<CalendarEvent> _todayEvents = Array.Empty<CalendarEvent>();
    private DateTime _eventsLoadedAtUtc;
    private volatile bool _disposed;

    public DockDateItem(CalendarPage flyout)
        : base(flyout)
    {
        _settings = flyout.Settings;
        _eventSource = flyout.EventSource;

        // Branded icon instead of a Segoe glyph: the dock band shows the same identity as the
        // top-level command and the package tile.
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        // The provider wiring has no hook that disposes dock items, so the timer must not
        // keep a discarded item alive: the tick holds only a weak reference and retires the
        // timer once the item has been collected.
        var self = new WeakReference<DockDateItem>(this);
        var timer = new Timer(DateRefreshInterval.TotalMilliseconds) { AutoReset = true };
        _timer = timer;
        timer.Elapsed += (_, _) =>
        {
            if (self.TryGetTarget(out var item) && !item._disposed)
            {
                item.OnTimerTick();
                return;
            }

            timer.Dispose();
        };

        // The settings page can change the title format or turn the event indicator on and
        // off while the dock band is on screen; both are re-applied on the change notification.
        _settings.Settings.SettingsChanged += OnSettingsChanged;

        UpdateText();
        timer.Start();
        EnsureTodayEventsLoaded();
    }

    /// <summary>
    /// Stops and releases the minute timer and the settings subscription. The extension dispose
    /// path reaches this item through the provider; the timer also retires itself once this
    /// item is collected.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.Settings.SettingsChanged -= OnSettingsChanged;
        _timer.Stop();
        _timer.Dispose();
    }

    private void OnTimerTick()
    {
        UpdateText();
        EnsureTodayEventsLoaded();
    }

    private void OnSettingsChanged(object sender, Settings args)
    {
        if (_disposed)
        {
            return;
        }

        UpdateText();
        EnsureTodayEventsLoaded();
    }

    /// <summary>
    /// Starts a background event refresh when the indicator is on and the cached events are for a
    /// different day or older than <see cref="EventRefreshInterval"/>. At most one refresh runs at
    /// a time, so the timer and the settings notification cannot stack up queries.
    /// </summary>
    private void EnsureTodayEventsLoaded()
    {
        DateOnly today;
        lock (_sync)
        {
            if (_disposed || _isLoadingEvents || !_settings.ShowEventIndicator)
            {
                return;
            }

            today = DateOnly.FromDateTime(DateTime.Now);
            var isFresh = _eventsLoaded
                && _eventsDate == today
                && DateTime.UtcNow - _eventsLoadedAtUtc < EventRefreshInterval;
            if (isFresh)
            {
                return;
            }

            _isLoadingEvents = true;
        }

        _ = LoadTodayEventsAsync(today);
    }

    private async Task LoadTodayEventsAsync(DateOnly today)
    {
        IReadOnlyList<CalendarEvent> events;
        try
        {
            events = await _eventSource.GetEventsAsync(today, today.AddDays(1)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The event source is expected to return an empty list for an unavailable
            // calendar; the dock must keep showing the date if an implementation throws.
            events = Array.Empty<CalendarEvent>();
        }

        bool applied;
        lock (_sync)
        {
            _isLoadingEvents = false;
            applied = !_disposed && DateOnly.FromDateTime(DateTime.Now) == today;
            if (applied)
            {
                _todayEvents = events;
                _eventsDate = today;
                _eventsLoaded = true;
                _eventsLoadedAtUtc = DateTime.UtcNow;
            }
        }

        if (applied)
        {
            UpdateText();
        }
    }

    private void UpdateText()
    {
        if (_disposed)
        {
            return;
        }

        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;

        // The Dock button keeps the shortest readable form: day number and month abbreviation
        // come from the configured format. The subtitle carries only the event context: the next
        // event, or the quiet count (including "No events"), so the date is never duplicated.
        var title = now.ToString(_settings.DockDateFormat, culture);
        var subtitle = string.Empty;
        lock (_sync)
        {
            if (_settings.ShowEventIndicator
                && _eventsLoaded
                && _eventsDate == DateOnly.FromDateTime(now))
            {
                subtitle = ComposeEventSubtitle(now, _todayEvents, culture);
            }
        }

        bool titleChanged;
        bool subtitleChanged;
        lock (_sync)
        {
            titleChanged = !string.Equals(title, _renderedTitle, StringComparison.Ordinal);
            subtitleChanged = !string.Equals(subtitle, _renderedSubtitle, StringComparison.Ordinal);
            if (titleChanged)
            {
                _renderedTitle = title;
            }

            if (subtitleChanged)
            {
                _renderedSubtitle = subtitle;
            }
        }

        // Property changes go to the host outside the lock so a host callback can never block
        // the events refresh.
        if (titleChanged)
        {
            Title = title;
        }

        if (subtitleChanged)
        {
            Subtitle = subtitle;
        }
    }

    /// <summary>
    /// Prefers the next timed event of the day ("13:00 Lunch with the team"); falls back to the
    /// quiet count so the band still says something useful when only all-day or past events
    /// exist. The count follows the same half-open day semantics as the calendar grid.
    /// </summary>
    private static string ComposeEventSubtitle(DateTime now, IReadOnlyList<CalendarEvent> todayEvents, CultureInfo culture)
    {
        var nextEvent = FindNextEvent(todayEvents, now);
        if (nextEvent is not null)
        {
            var time = nextEvent.Start.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
            return Truncate(string.Concat(time, " ", nextEvent.DisplayTitle));
        }

        var today = DateOnly.FromDateTime(now);
        var counts = EventRange.CountPerDay(todayEvents, today, today.AddDays(1));
        var count = counts.TryGetValue(today, out var value) ? value : 0;
        return count switch
        {
            0 => NoEventsLabel,
            1 => SingleEventLabel,
            _ => count.ToString(culture) + MultipleEventsLabel,
        };
    }

    /// <summary>
    /// The first timed event of today that has not finished yet (an ongoing meeting still counts
    /// as "next"); all-day events have no time to show and are skipped.
    /// </summary>
    private static CalendarEvent? FindNextEvent(IReadOnlyList<CalendarEvent> events, DateTime now)
    {
        CalendarEvent? next = null;
        foreach (var item in events)
        {
            if (item.IsAllDay)
            {
                continue;
            }

            var start = item.Start.LocalDateTime;
            if (start.Date != now.Date || item.End.LocalDateTime <= now)
            {
                continue;
            }

            if (next is null || start < next.Start.LocalDateTime)
            {
                next = item;
            }
        }

        return next;
    }

    private static string Truncate(string text) => text.Length <= MaxSubtitleLength
        ? text
        : text[..(MaxSubtitleLength - 1)].TrimEnd() + "\u2026";
}
