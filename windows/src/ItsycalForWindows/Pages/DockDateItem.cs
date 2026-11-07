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
/// title carries the glanceable date in the configured short format; the subtitle is used only
/// for today's event count while the event indicator setting is on, so the date is never
/// repeated next to itself. The text refreshes once a minute so the Dock stays correct across
/// midnight, and the count is re-read from the shared event source every few minutes.
/// </summary>
internal sealed partial class DockDateItem : ListItem, IDisposable
{
    private static readonly TimeSpan DateRefreshInterval = TimeSpan.FromMinutes(1);

    // The appointment store is queried at most this often; a reopened flyout refreshes it
    // anyway, so the dock count only needs to follow events being added or removed.
    private static readonly TimeSpan EventCountRefreshInterval = TimeSpan.FromMinutes(5);

    private const string SingleEventLabel = "1 event";
    private const string MultipleEventsLabel = " events";

    private readonly object _sync = new();
    private readonly SettingsManager _settings;
    private readonly IEventSource _eventSource;
    private readonly Timer _timer;

    private string? _renderedTitle;
    private string? _renderedSubtitle;
    private bool _eventCountLoaded;
    private bool _isLoadingEventCount;
    private DateOnly _eventCountDate;
    private int _todayEventCount;
    private DateTime _eventCountLoadedAtUtc;
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
        EnsureEventCountLoaded();
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
        EnsureEventCountLoaded();
    }

    private void OnSettingsChanged(object sender, Settings args)
    {
        if (_disposed)
        {
            return;
        }

        UpdateText();
        EnsureEventCountLoaded();
    }

    /// <summary>
    /// Starts a background count refresh when the indicator is on and the cached count is for a
    /// different day or older than <see cref="EventCountRefreshInterval"/>. At most one refresh
    /// runs at a time, so the timer and the settings notification cannot stack up queries.
    /// </summary>
    private void EnsureEventCountLoaded()
    {
        DateOnly today;
        lock (_sync)
        {
            if (_disposed || _isLoadingEventCount || !_settings.ShowEventIndicator)
            {
                return;
            }

            today = DateOnly.FromDateTime(DateTime.Now);
            var isFresh = _eventCountLoaded
                && _eventCountDate == today
                && DateTime.UtcNow - _eventCountLoadedAtUtc < EventCountRefreshInterval;
            if (isFresh)
            {
                return;
            }

            _isLoadingEventCount = true;
        }

        _ = LoadTodayEventCountAsync(today);
    }

    private async Task LoadTodayEventCountAsync(DateOnly today)
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

        // CountPerDay applies the same half-open day semantics as the calendar grid, so the dot
        // on today's cell and the dock count agree.
        var counts = EventRange.CountPerDay(events, today, today.AddDays(1));
        var count = counts.TryGetValue(today, out var value) ? value : 0;

        bool applied;
        lock (_sync)
        {
            _isLoadingEventCount = false;
            applied = !_disposed && DateOnly.FromDateTime(DateTime.Now) == today;
            if (applied)
            {
                _todayEventCount = count;
                _eventCountDate = today;
                _eventCountLoaded = true;
                _eventCountLoadedAtUtc = DateTime.UtcNow;
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
        // come from the configured format. The subtitle is reserved for the event context, so
        // the full date is not duplicated underneath the formatted title.
        var title = now.ToString(_settings.DockDateFormat, culture);
        var subtitle = string.Empty;
        lock (_sync)
        {
            if (_settings.ShowEventIndicator
                && _eventCountLoaded
                && _eventCountDate == DateOnly.FromDateTime(now)
                && _todayEventCount > 0)
            {
                subtitle = FormatEventCount(_todayEventCount);
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
        // the count refresh.
        if (titleChanged)
        {
            Title = title;
        }

        if (subtitleChanged)
        {
            Subtitle = subtitle;
        }
    }

    private static string FormatEventCount(int count) => count == 1
        ? SingleEventLabel
        : count.ToString(CultureInfo.CurrentCulture) + MultipleEventsLabel;
}
