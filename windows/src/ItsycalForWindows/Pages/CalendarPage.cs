using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ItsycalForWindows.Models;
using ItsycalForWindows.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;

namespace ItsycalForWindows.Pages;

/// <summary>
/// Calendar flyout: a minimalist Adaptive Card with the month grid (today marked by the accent
/// day number, the selected day by a rounded accent surface, adjacent-month days subtle, optional
/// ISO week numbers), compact month navigation, and the events of the selected day. The page
/// never blocks: <see cref="GetContent"/> returns the card immediately and the events load in the
/// background, after which the card is refreshed.
/// </summary>
/// <remarks>
/// The visible month and the selected date are separate pieces of state. Month navigation moves
/// the selection to the same day-of-month (clamped to the target month's length) so the events
/// section keeps referring to an existing day, while selecting a day in an adjacent-month cell
/// also switches the displayed month.
/// <para>
/// The card's hierarchy is expressed with type scale and whitespace rather than rules: one large
/// month heading, small subtle weekday labels, uniform day numbers, and a single subtle separator
/// before the events list.
/// </para>
/// </remarks>
internal sealed partial class CalendarPage : ContentPage
{
    // Re-reading the event store on every flyout open keeps the month current, while the
    // short lifetime prevents a render/load feedback loop when the card refreshes itself.
    private static readonly TimeSpan EventsRefreshInterval = TimeSpan.FromSeconds(30);

    private const string PreviousMonthAction = "prev";
    private const string TodayAction = "today";
    private const string NextMonthAction = "next";

    // Action.Submit data keys. "nav" is the month-navigation key kept from the first version of
    // the card; "day" carries the date of a clicked day cell; "event" carries the stable key of a
    // clicked event row and "eventAction" selects a details command on that event.
    private const string NavigationPayloadKey = "nav";
    private const string DayPayloadKey = "day";
    private const string DayPayloadFormat = "yyyy-MM-dd";
    private const string EventPayloadKey = "event";
    private const string EventActionPayloadKey = "eventAction";
    private const string JoinEventAction = "join";
    private const string CopyEventAction = "copy";
    private const string CloseEventAction = "close";

    // Event details commands and notices.
    private const string JoinMeetingLabel = "Join meeting";
    private const string CopyLinkLabel = "Copy link";
    private const string CloseDetailsLabel = "Close details";
    private const string CopiedNotice = "Meeting link copied.";
    private const string NoMeetingToast = "This event has no meeting link.";
    private const string LaunchFailedToast = "Could not open the meeting link.";
    private const string CopyFailedToast = "Could not copy the meeting link.";
    private const int DetailsPreviewLength = 480;

    // The extension ships English UI strings only (windows/README.md, Limitations).
    private const string WeekNumberHeader = "Wk";
    private const string EventsHeading = "Events";
    private const string LoadingEventsLabel = "Loading events\u2026";
    private const string PreviousMonthLabel = "Previous month";
    private const string TodayLabel = "Today";
    private const string NextMonthLabel = "Next month";
    private const string PreviousMonthGlyph = "\u2039";
    private const string NextMonthGlyph = "\u203A";
    private const string AllDayLabel = "All day";
    private const string MultiDayLabel = "Multi-day";
    private const string EventSeparator = " \u00b7 ";
    private const char EventDot = '\u2022';
    private const string SelectedDayContainerStyle = "accent";

    // The card's vertical rhythm. The host maps spacing tokens to 4/8/16/24 px, so the layout
    // separates the heading, the grid and the events with whitespace instead of rule lines.
    private const string GridSpacing = "medium";
    private const string GridRowSpacing = "none";
    private const string SectionSpacing = "medium";
    private const string SegmentSpacing = "small";
    private const string RowSpacing = "small";

    private readonly object _sync = new();
    private readonly SettingsManager _settings;
    private readonly IEventSource _eventSource;

    private CalendarMonth _month;
    private DateOnly _selectedDate;
    private DateOnly _todayAtLastRender;
    private IReadOnlyList<CalendarEvent> _events = Array.Empty<CalendarEvent>();
    private DateOnly? _loadedFrom;
    private DateOnly? _loadedToExclusive;
    private DateTime _loadedAtUtc;
    private DateOnly? _loadingFrom;
    private DateOnly? _loadingToExclusive;
    private bool _isLoading;
    private int _loadGeneration;

    // The form the host is currently showing (if any) and the card JSON it must show.
    private CalendarForm? _currentForm;
    private string _renderedTemplate = string.Empty;

    // The card is only rebuilt when something that affects it changed.
    private CalendarMonth? _renderedMonth;
    private DateOnly? _renderedSelectedDate;
    private string? _renderedSelectedEventKey;
    private string? _renderedDetailsNotice;
    private IReadOnlyList<CalendarEvent>? _renderedEvents;
    private bool _renderedLoading;
    private bool _renderedWeekNumbers;

    // The event whose details block is expanded under the agenda, and a transient line (for
    // example the copy confirmation) shown inside that block. Both are cleared whenever the
    // day or month selection moves.
    private CalendarEvent? _selectedEvent;
    private string? _detailsNotice;

    public CalendarPage(SettingsManager settings, IEventSource eventSource)
        : this(settings, eventSource, CalendarMonth.ForToday(settings.WeekStart))
    {
    }

    public CalendarPage(SettingsManager settings, IEventSource eventSource, CalendarMonth month)
    {
        _settings = settings;
        _eventSource = eventSource;
        _month = month;
        _selectedDate = DefaultSelection(month);
        _todayAtLastRender = DateOnly.FromDateTime(DateTime.Now);

        Name = "Open";
        Title = month.Title;
        Icon = new IconInfo("\uE787"); // Segoe Fluent: Calendar

        // The settings page is a different page; rebuilding the card from this event keeps the
        // grid and the cached events in sync when the week start or the week-number column
        // changes, without the host having to recreate the page.
        _settings.Settings.SettingsChanged += (_, _) => OnSettingsChanged();

        Render(startEventLoad: false);
    }

    /// <summary>The month currently displayed; "Today" resets it to the current month.</summary>
    public CalendarMonth Month
    {
        get
        {
            lock (_sync)
            {
                return _month;
            }
        }
    }

    /// <summary>The day whose events the card is showing.</summary>
    public DateOnly SelectedDate
    {
        get
        {
            lock (_sync)
            {
                return _selectedDate;
            }
        }
    }

    /// <summary>
    /// Settings shared with the dock band so both surfaces read the same persisted values and
    /// react to the same change notification.
    /// </summary>
    internal SettingsManager Settings => _settings;

    /// <summary>
    /// Event source shared with the dock band, so today's event count and the calendar flyout
    /// read through the same read-only cache instead of opening a second store connection.
    /// </summary>
    internal IEventSource EventSource => _eventSource;

    public override IContent[] GetContent()
    {
        Render(startEventLoad: true);

        // A fresh form object per fetch: the host subscribes to a form's property changes
        // when it builds the card view model, so a discarded form becomes collectable with
        // its subscription instead of accumulating subscriptions on one long-lived object
        // every time the dock flyout is reopened.
        CalendarForm form;
        lock (_sync)
        {
            form = new CalendarForm(HandleFormSubmit) { TemplateJson = _renderedTemplate };
            _currentForm = form;
        }

        return [form];
    }

    /// <summary>
    /// Handles the card's Action.Submit actions: the month buttons, the day cells, the event rows
    /// and the event-details commands. The host passes the action's <c>data</c> object as the
    /// second argument (the installed FormContent API exposes <c>SubmitForm(inputs, data)</c>);
    /// reading the same keys from <paramref name="inputs"/> as a fallback keeps the interactions
    /// working if a host merges the action data into the input payload instead. Invoked by
    /// <see cref="CalendarForm"/>.
    /// </summary>
    private CommandResult HandleFormSubmit(string inputs, string? data)
    {
        var action = ReadCalendarAction(data) ?? ReadCalendarAction(inputs);
        if (action is not { } requested)
        {
            return CommandResult.KeepOpen();
        }

        if (requested.Day is { } day)
        {
            SelectDay(day);
            return CommandResult.KeepOpen();
        }

        if (requested.EventAction is { } eventAction)
        {
            switch (eventAction)
            {
                case JoinEventAction:
                    return JoinMeeting(requested.EventKey);
                case CopyEventAction:
                    return CopyMeetingLink(requested.EventKey);
                case CloseEventAction:
                    ClearSelectedEvent();
                    return CommandResult.KeepOpen();
            }
        }

        if (requested.EventKey is { } eventKey)
        {
            SelectEvent(eventKey);
            return CommandResult.KeepOpen();
        }

        var current = Month;
        switch (requested.Navigation)
        {
            case PreviousMonthAction:
                Navigate(current.Previous());
                break;
            case TodayAction:
                ShowToday();
                break;
            case NextMonthAction:
                Navigate(current.Next());
                break;
        }

        return CommandResult.KeepOpen();
    }

    /// <summary>
    /// Expands the details block for the event with <paramref name="eventKey"/>, or collapses it
    /// when the same row is activated again. The events are already cached, so this is a pure
    /// re-render with no event-store query.
    /// </summary>
    private void SelectEvent(string eventKey)
    {
        lock (_sync)
        {
            var match = FindEventByKeyLocked(eventKey);
            var isSameEvent = match is not null
                && _selectedEvent is not null
                && string.Equals(EventKey(_selectedEvent), eventKey, StringComparison.Ordinal);
            _selectedEvent = isSameEvent ? null : match;
            _detailsNotice = null;
        }

        Render(startEventLoad: false);
    }

    private void ClearSelectedEvent()
    {
        lock (_sync)
        {
            _selectedEvent = null;
            _detailsNotice = null;
        }

        Render(startEventLoad: false);
    }

    private void SetDetailsNotice(string notice)
    {
        lock (_sync)
        {
            _detailsNotice = notice;
        }

        Render(startEventLoad: false);
    }

    /// <summary>
    /// Launches the event's online-meeting URI. The host invokes SubmitForm on a background thread,
    /// so waiting for the shell to accept the URI is safe and lets the result decide between
    /// keeping the flyout open and reporting the failure as a toast.
    /// </summary>
    private CommandResult JoinMeeting(string? eventKey)
    {
        if (!TryGetMeetingUri(eventKey, out var uri) || uri is null)
        {
            return CommandResult.ShowToast(NoMeetingToast);
        }

        try
        {
            if (Launcher.LaunchUriAsync(uri).AsTask().GetAwaiter().GetResult())
            {
                return CommandResult.KeepOpen();
            }
        }
        catch (Exception)
        {
            // Fall through to the toast: a failed launch must never crash the flyout.
        }

        return CommandResult.ShowToast(LaunchFailedToast);
    }

    /// <summary>Copies the event's online-meeting URI to the clipboard.</summary>
    private CommandResult CopyMeetingLink(string? eventKey)
    {
        if (!TryGetMeetingUri(eventKey, out var uri) || uri is null)
        {
            return CommandResult.ShowToast(NoMeetingToast);
        }

        try
        {
            ClipboardHelper.SetText(uri.AbsoluteUri);
        }
        catch (Exception)
        {
            return CommandResult.ShowToast(CopyFailedToast);
        }

        SetDetailsNotice(CopiedNotice);
        return CommandResult.KeepOpen();
    }

    private bool TryGetMeetingUri(string? eventKey, out Uri? uri)
    {
        uri = null;
        CalendarEvent? meeting;
        lock (_sync)
        {
            meeting = FindEventByKeyLocked(eventKey);
        }

        return meeting is not null && meeting.TryGetOnlineMeetingUri(out uri) && uri is not null;
    }

    private CalendarEvent? FindEventByKeyLocked(string? eventKey)
    {
        if (string.IsNullOrWhiteSpace(eventKey))
        {
            return null;
        }

        foreach (var candidate in GetEventsForDay(_events, _month, _selectedDate))
        {
            if (string.Equals(EventKey(candidate), eventKey, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Stable identity of an event within the current process: its UTC start and end ticks plus a
    /// subject hash, so two events that share a time slot still get distinct payload keys.
    /// </summary>
    private static string EventKey(CalendarEvent item) => string.Concat(
        item.Start.UtcTicks.ToString(CultureInfo.InvariantCulture),
        ":",
        item.End.UtcTicks.ToString(CultureInfo.InvariantCulture),
        ":",
        ((uint)StringComparer.Ordinal.GetHashCode(item.Subject)).ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Shows <paramref name="month"/> and keeps the selection on the same day-of-month, clamped
    /// to the target month's length (for example 31 January в†’ 28 February). The card reloads its
    /// events because the visible range moved.
    /// </summary>
    private void Navigate(CalendarMonth month)
    {
        DateOnly selection;
        lock (_sync)
        {
            selection = ClampToMonth(_selectedDate, month);
        }

        Show(month, selection);
    }

    /// <summary>Shows the current month and selects today.</summary>
    private void ShowToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        Show(CalendarMonth.ForToday(_settings.WeekStart), today);
    }

    /// <summary>
    /// Selects <paramref name="date"/>. A date from an adjacent-month cell switches the displayed
    /// month first; a date inside the displayed month only rebuilds the card from the cached
    /// events, so no second event-store query is needed.
    /// </summary>
    private void SelectDay(DateOnly date)
    {
        CalendarMonth month;
        bool monthChanged;
        lock (_sync)
        {
            monthChanged = _month.Year != date.Year || _month.Month != date.Month;
            if (monthChanged)
            {
                _month = new CalendarMonth(date.Year, date.Month, _settings.WeekStart);
                ResetEventsLocked();
            }

            _selectedDate = date;

            // The expanded details describe the agenda that was on screen; any day selection
            // (including one inside the same month) collapses them.
            _selectedEvent = null;
            _detailsNotice = null;
            month = _month;
        }

        if (monthChanged && !string.Equals(Title, month.Title, StringComparison.Ordinal))
        {
            Title = month.Title;
        }

        Render(startEventLoad: true);
    }

    private void Show(CalendarMonth month, DateOnly selection)
    {
        lock (_sync)
        {
            _month = month;
            _selectedDate = selection;
            ResetEventsLocked();
        }

        if (!string.Equals(Title, month.Title, StringComparison.Ordinal))
        {
            Title = month.Title;
        }

        Render(startEventLoad: true);
    }

    /// <summary>
    /// Rebuilds the card when the displayed month, the selected day, the cached events, the
    /// loading state or a grid setting changed, and starts a background event load when the
    /// visible range is not cached or is stale. Cheap when nothing changed: no I/O, no JSON build.
    /// </summary>
    private void Render(bool startEventLoad)
    {
        CalendarMonth month;
        DateOnly selectedDate;
        CalendarEvent? selectedEvent;
        string? detailsNotice;
        IReadOnlyList<CalendarEvent> events;
        bool isLoading;
        bool startLoad = false;
        DateOnly loadFrom;
        DateOnly loadTo;
        var loadGeneration = 0;
        CalendarForm? formToRefresh = null;
        string? templateToPublish = null;

        lock (_sync)
        {
            EnsureCurrentDayLocked();
            EnsureWeekStartLocked();

            loadFrom = _month.VisibleStart;
            loadTo = _month.VisibleEndExclusive;
            if (startEventLoad && NeedsEventLoadLocked(loadFrom, loadTo))
            {
                _isLoading = true;
                _loadingFrom = loadFrom;
                _loadingToExclusive = loadTo;
                _loadGeneration++;
                loadGeneration = _loadGeneration;
                startLoad = true;
            }

            month = _month;
            selectedDate = _selectedDate;
            selectedEvent = _selectedEvent;
            detailsNotice = _detailsNotice;
            events = _events;
            isLoading = _isLoading;

            var selectedEventKey = selectedEvent is null ? null : EventKey(selectedEvent);
            var showWeekNumbers = _settings.ShowWeekNumbers;
            var showLoading = isLoading && events.Count == 0;
            if (!ReferenceEquals(_renderedMonth, month)
                || _renderedSelectedDate != selectedDate
                || !string.Equals(selectedEventKey, _renderedSelectedEventKey, StringComparison.Ordinal)
                || !string.Equals(detailsNotice, _renderedDetailsNotice, StringComparison.Ordinal)
                || !ReferenceEquals(_renderedEvents, events)
                || _renderedLoading != showLoading
                || _renderedWeekNumbers != showWeekNumbers)
            {
                _renderedMonth = month;
                _renderedSelectedDate = selectedDate;
                _renderedSelectedEventKey = selectedEventKey;
                _renderedDetailsNotice = detailsNotice;
                _renderedEvents = events;
                _renderedLoading = showLoading;
                _renderedWeekNumbers = showWeekNumbers;
                _renderedTemplate = BuildTemplateJson(
                    month,
                    events,
                    showWeekNumbers,
                    showLoading,
                    selectedDate,
                    selectedEvent,
                    detailsNotice);
                templateToPublish = _renderedTemplate;
                formToRefresh = _currentForm;
            }
        }

        if (templateToPublish is not null && formToRefresh is not null)
        {
            // FormContent raises a property change from the TemplateJson setter, which makes
            // the host re-render the card without fetching the page content again.
            formToRefresh.TemplateJson = templateToPublish;
        }

        if (!string.Equals(Title, month.Title, StringComparison.Ordinal))
        {
            Title = month.Title;
        }

        if (IsLoading != isLoading)
        {
            IsLoading = isLoading;
        }

        if (startLoad)
        {
            _ = LoadEventsAsync(loadFrom, loadTo, loadGeneration);
        }
    }

    private void OnSettingsChanged()
    {
        try
        {
            Render(startEventLoad: true);
        }
        catch (Exception)
        {
            // The host may have torn the page down while the settings were saved.
        }
    }

    private async Task LoadEventsAsync(DateOnly fromInclusive, DateOnly toExclusive, int generation)
    {
        IReadOnlyList<CalendarEvent> events;
        try
        {
            events = await _eventSource.GetEventsAsync(fromInclusive, toExclusive).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // IEventSource must not throw for an unavailable calendar, but the calendar UI
            // must still work if an implementation does: fall back to no events.
            events = Array.Empty<CalendarEvent>();
        }

        lock (_sync)
        {
            if (generation != _loadGeneration)
            {
                // The month changed while this load was in flight; its result is stale.
                return;
            }

            _events = SortEvents(events);
            if (_selectedEvent is not null)
            {
                // A refreshed snapshot replaces the event instances; re-resolve the expanded
                // event from the new list and drop it when the store no longer reports it.
                _selectedEvent = FindEventByKeyLocked(EventKey(_selectedEvent));
                if (_selectedEvent is null)
                {
                    _detailsNotice = null;
                }
            }

            _loadedFrom = fromInclusive;
            _loadedToExclusive = toExclusive;
            _loadedAtUtc = DateTime.UtcNow;
            _isLoading = false;
            _loadingFrom = null;
            _loadingToExclusive = null;
        }

        try
        {
            Render(startEventLoad: false);
        }
        catch (Exception)
        {
            // The host may have torn the page down while the events were loading; there is
            // nothing left to refresh in that case.
        }
    }

    /// <summary>
    /// The page instance outlives a day boundary, so the "today" grid flags and the default
    /// selection must follow the clock. When the selection was sitting on the previous today it
    /// moves to the new today; a selection the user made deliberately is kept.
    /// </summary>
    private void EnsureCurrentDayLocked()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today == _todayAtLastRender)
        {
            return;
        }

        var previousToday = _todayAtLastRender;
        _todayAtLastRender = today;

        // Rebuild the grid so IsToday flags follow the new local date, and collapse the event
        // details: the agenda they described belongs to the previous day.
        _month = new CalendarMonth(_month.Year, _month.Month, _month.WeekStart);
        _selectedEvent = null;
        _detailsNotice = null;

        if (_selectedDate == previousToday)
        {
            _selectedDate = today;
            if (_month.Year != today.Year || _month.Month != today.Month)
            {
                _month = new CalendarMonth(today.Year, today.Month, _month.WeekStart);
                ResetEventsLocked();
            }
        }
    }

    private void EnsureWeekStartLocked()
    {
        var weekStart = _settings.WeekStart;
        if (_month.WeekStart == weekStart)
        {
            return;
        }

        // The settings page can change the first day of the week while this page is alive;
        // rebuild the grid and reload events because the visible range moves by one day.
        _month = new CalendarMonth(_month.Year, _month.Month, weekStart);
        ResetEventsLocked();
    }

    private void ResetEventsLocked()
    {
        _events = Array.Empty<CalendarEvent>();
        _loadedFrom = null;
        _loadedToExclusive = null;
        _loadingFrom = null;
        _loadingToExclusive = null;
        _isLoading = false;
        _loadGeneration++;

        // A new day or month selection drops the expanded event details; they only make sense
        // for the agenda that was on screen.
        _selectedEvent = null;
        _detailsNotice = null;
    }

    private bool NeedsEventLoadLocked(DateOnly fromInclusive, DateOnly toExclusive)
    {
        if (_isLoading && _loadingFrom == fromInclusive && _loadingToExclusive == toExclusive)
        {
            return false;
        }

        if (_loadedFrom == fromInclusive
            && _loadedToExclusive == toExclusive
            && DateTime.UtcNow - _loadedAtUtc < EventsRefreshInterval)
        {
            return false;
        }

        return true;
    }

    private static IReadOnlyList<CalendarEvent> SortEvents(IReadOnlyList<CalendarEvent> events)
    {
        if (events.Count <= 1)
        {
            return events;
        }

        return events
            .OrderBy(item => item.Start)
            .ThenBy(item => item.DisplayTitle, StringComparer.CurrentCulture)
            .ToArray();
    }

    private static DateOnly DefaultSelection(CalendarMonth month)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return today.Year == month.Year && today.Month == month.Month
            ? today
            : new DateOnly(month.Year, month.Month, 1);
    }

    private static DateOnly ClampToMonth(DateOnly date, CalendarMonth month)
    {
        var lastDay = DateTime.DaysInMonth(month.Year, month.Month);
        return new DateOnly(month.Year, month.Month, Math.Min(date.Day, lastDay));
    }

    /// <summary>
    /// Reads the card action payload. Both the action's <c>data</c> and the submitted inputs are
    /// parsed with this method, so the caller can try them in order.
    /// </summary>
    private static CalendarAction? ReadCalendarAction(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(payload) is not JsonObject json)
            {
                return null;
            }

            string? navigation = ReadString(json, NavigationPayloadKey);
            string? eventKey = ReadString(json, EventPayloadKey);
            string? eventAction = ReadString(json, EventActionPayloadKey);

            DateOnly? day = ReadString(json, DayPayloadKey) is { } dayText
                && DateOnly.TryParseExact(
                    dayText,
                    DayPayloadFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedDay)
                ? parsedDay
                : null;

            return navigation is null && day is null && eventKey is null && eventAction is null
                ? null
                : new CalendarAction(navigation, day, eventKey, eventAction);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonObject json, string key) =>
        json[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string BuildTemplateJson(
        CalendarMonth month,
        IReadOnlyList<CalendarEvent> events,
        bool showWeekNumbers,
        bool showLoading,
        DateOnly selectedDate,
        CalendarEvent? selectedEvent,
        string? detailsNotice)
    {
        var eventCounts = EventRange.CountPerDay(events, month.VisibleStart, month.VisibleEndExclusive);
        var gridMonth = eventCounts.Count == 0 ? month : month.WithEventCounts(eventCounts);
        var selectedEvents = GetEventsForDay(events, month, selectedDate);

        var body = new JsonArray
        {
            (JsonNode)BuildAgendaHeader(gridMonth, selectedDate),
            (JsonNode)BuildMonthPanel(gridMonth, showWeekNumbers, selectedDate),
        };

        // The event source may return events that started before the visible range (its query
        // uses a lookback for multi-day events). GroupByDay projects them onto the visible days
        // and clips the projection, so the section only needs the selected day's group.
        AddEventsSection(body, selectedDate, selectedEvents, showLoading, selectedEvent, detailsNotice);

        var card = new JsonObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            // 1.5 is the version the host renders for its own cards. Everything used here
            // (TextBlock/ColumnSet/Column with selectAction, container style, spacing, separator,
            // isSubtle, Action.Submit, tooltip) is documented for 1.5; `roundedCorners` is the
            // host's documented corner treatment for Container/ColumnSet/Column surfaces.
            ["version"] = "1.5",
            ["body"] = body,
            ["actions"] = new JsonArray
            {
                (JsonNode)BuildNavigationAction(PreviousMonthGlyph, PreviousMonthAction, "nav-previous", PreviousMonthLabel),
                (JsonNode)BuildNavigationAction(TodayLabel, TodayAction, "nav-today", tooltip: null),
                (JsonNode)BuildNavigationAction(NextMonthGlyph, NextMonthAction, "nav-next", NextMonthLabel),
            },
        };

        return card.ToJsonString();
    }

    /// <summary>
    /// The card's visual anchor: the selected day as a large accent number beside the month and
    /// the selected weekday. This is the one deliberately loud element; everything below is quiet
    /// so the eye lands on "which day am I looking at" first.
    /// </summary>
    private static JsonObject BuildAgendaHeader(CalendarMonth month, DateOnly selectedDate)
    {
        var culture = CultureInfo.CurrentCulture;
        var weekday = culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetDayName(selectedDate.DayOfWeek));
        var subtitle = weekday;

        return new JsonObject
        {
            ["type"] = "ColumnSet",
            ["verticalContentAlignment"] = "center",
            ["columns"] = new JsonArray
            {
                (JsonNode)BuildColumn(
                    "auto",
                    TextBlock(
                        selectedDate.Day.ToString(culture),
                        size: "extraLarge",
                        color: "accent",
                        weight: "bolder",
                        alignment: "center")),
                (JsonNode)BuildColumn(
                    "stretch",
                    new JsonArray
                    {
                        (JsonNode)TextBlock(month.Title, size: "medium", weight: "bolder", wrap: true),
                        (JsonNode)TextBlock(subtitle, size: "small", subtle: true, spacing: "none", wrap: true),
                    }),
            },
        };
    }

    /// <summary>
    /// The month grid sits on its own rounded emphasis surface, which gives the card depth without
    /// introducing borders: one filled panel for the calendar, one card per event.
    /// </summary>
    private static JsonObject BuildMonthPanel(CalendarMonth month, bool showWeekNumbers, DateOnly selectedDate)
    {
        var content = new JsonArray
        {
            (JsonNode)BuildWeekdayHeader(month.WeekStart, showWeekNumbers),
        };

        foreach (var week in month.Weeks)
        {
            content.Add((JsonNode)BuildWeekRow(week, showWeekNumbers, selectedDate));
        }

        return new JsonObject
        {
            ["type"] = "Container",
            ["style"] = "emphasis",
            ["roundedCorners"] = true,
            ["items"] = content,
        };
    }

    private static JsonObject BuildWeekdayHeader(DayOfWeek weekStart, bool showWeekNumbers)
    {
        var columns = new JsonArray();
        if (showWeekNumbers)
        {
            columns.Add((JsonNode)BuildColumn("auto", TextBlock(WeekNumberHeader, size: "small", subtle: true, alignment: "center")));
        }

        // Weekday labels are the quietest step of the hierarchy: small, subtle and not bold.
        var shortestDayNames = CultureInfo.CurrentCulture.DateTimeFormat.ShortestDayNames;
        for (var column = 0; column < WeekRow.DayCount; column++)
        {
            var dayOfWeek = (DayOfWeek)(((int)weekStart + column) % WeekRow.DayCount);
            columns.Add((JsonNode)BuildColumn("stretch", TextBlock(shortestDayNames[(int)dayOfWeek], subtle: true, weight: "bolder", alignment: "center")));
        }

        return new JsonObject
        {
            ["type"] = "ColumnSet",
            // No explicit spacing here: the host collapses the weekday label row to a clipped
            // sliver when a ColumnSet carries a spacing token. The default body spacing already
            // separates the heading from the labels.
            ["columns"] = columns,
        };
    }

    private static JsonObject BuildWeekRow(WeekRow week, bool showWeekNumbers, DateOnly selectedDate)
    {
        var columns = new JsonArray();
        if (showWeekNumbers)
        {
            columns.Add((JsonNode)BuildColumn(
                "auto",
                TextBlock(week.IsoWeekNumber.ToString(CultureInfo.CurrentCulture), size: "small", subtle: true, alignment: "center")));
        }

        foreach (var day in week.Days)
        {
            columns.Add((JsonNode)BuildDayCell(day, day.Date == selectedDate));
        }

        return new JsonObject
        {
            ["type"] = "ColumnSet",
            // No spacing token here either: the host maps the compact token on a ColumnSet to a
            // negative offset and the rows then overlap the weekday labels above them.
            ["columns"] = columns,
        };
    }

    /// <summary>
    /// Builds one clickable day cell. Today keeps the accent-coloured, bold day number; the
    /// selected day adds a rounded accent surface behind its number, so the two states stay
    /// distinguishable even when today is the selected day. <see cref="JsonObject"/>
    /// <c>selectAction</c> makes the whole cell submit the day's date, which the page reads back
    /// in <see cref="HandleFormSubmit"/>.
    /// </summary>
    private static JsonObject BuildDayCell(CalendarDay day, bool isSelected)
    {
        string? color = null;
        string? weight = null;
        var subtle = false;
        if (day.IsToday)
        {
            color = "accent";
            weight = "bolder";
        }
        else if (!day.IsCurrentMonth)
        {
            subtle = true;
        }

        if (isSelected)
        {
            // The accent surface supplies the contrast colours, so the day number keeps the
            // default text colour instead of painting accent-on-accent.
            color = null;
            weight = "bolder";
        }

        var number = day.Date.Day.ToString(CultureInfo.CurrentCulture);
        var text = day.HasEvents ? number + EventDot : number;

        var cell = new JsonObject
        {
            ["type"] = "Column",
            ["width"] = "stretch",
            ["items"] = new JsonArray
            {
                (JsonNode)TextBlock(text, color: color, weight: weight, subtle: subtle, alignment: "center"),
            },
            ["selectAction"] = new JsonObject
            {
                ["type"] = "Action.Submit",
                ["data"] = new JsonObject
                {
                    [DayPayloadKey] = day.Date.ToString(DayPayloadFormat, CultureInfo.InvariantCulture),
                },
            },
        };

        if (isSelected)
        {
            // The rounded accent surface is the card's only filled surface; `roundedCorners` is
            // the host's documented corner treatment for Container/ColumnSet/Column elements.
            cell["style"] = SelectedDayContainerStyle;
            cell["roundedCorners"] = true;
        }

        return cell;
    }

    /// <summary>
    /// Returns the events overlapping <paramref name="date"/>, ordered by start time. The
    /// per-day projection and ordering come from <see cref="EventRange.GroupByDay"/> so the page
    /// does not duplicate the day-span arithmetic.
    /// </summary>
    private static IReadOnlyList<CalendarEvent> GetEventsForDay(
        IReadOnlyList<CalendarEvent> events,
        CalendarMonth month,
        DateOnly date)
    {
        if (events.Count == 0)
        {
            return Array.Empty<CalendarEvent>();
        }

        var eventsByDay = EventRange.GroupByDay(events, month.VisibleStart, month.VisibleEndExclusive);
        return eventsByDay.TryGetValue(date, out var dayEvents) ? dayEvents : Array.Empty<CalendarEvent>();
    }

    private static void AddEventsSection(
        JsonArray body,
        DateOnly selectedDate,
        IReadOnlyList<CalendarEvent> events,
        bool showLoading,
        CalendarEvent? selectedEvent,
        string? detailsNotice)
    {
        // One quiet caption with the single subtle separator of the card; the separator plus the
        // large spacing is what marks the transition from the grid to the events.
        body.Add((JsonNode)TextBlock(
            FormatEventsHeading(selectedDate),
            size: "small",
            weight: "bolder",
            subtle: true,
            spacing: SectionSpacing,
            separator: true,
            wrap: true));

        if (showLoading)
        {
            body.Add((JsonNode)TextBlock(LoadingEventsLabel, size: "small", subtle: true, spacing: RowSpacing, wrap: true));
            return;
        }

        if (events.Count == 0)
        {
            // Two-part empty state defined by specs 0003 (AC-5) and 0004 (AC-7): the exact sentence
            // on the first line and the calendars-folder pointer as its own TextBlock on the second.
            // Whether each line stays visually unwrapped is an installed-build manual check.
            body.Add((JsonNode)TextBlock(
                CalendarOnboarding.EmptyStateTitle,
                size: "small",
                subtle: true,
                spacing: RowSpacing,
                wrap: true));
            body.Add((JsonNode)TextBlock(
                CalendarOnboarding.EmptyStatePointer,
                size: "small",
                subtle: true,
                spacing: "none",
                wrap: true));
            return;
        }

        var selectedEventKey = selectedEvent is null ? null : EventKey(selectedEvent);
        foreach (var item in events)
        {
            body.Add((JsonNode)BuildEventCard(item, selectedDate, selectedEventKey));
        }

        if (selectedEvent is not null)
        {
            AddEventDetails(body, selectedEvent, selectedDate, detailsNotice);
        }
    }

    private static string FormatEventsHeading(DateOnly selectedDate)
    {
        var heading = string.Concat(
            EventsHeading,
            EventSeparator,
            selectedDate.ToString("ddd d MMM", CultureInfo.CurrentCulture));

        if (selectedDate == DateOnly.FromDateTime(DateTime.Now))
        {
            heading = string.Concat(heading, EventSeparator, TodayLabel);
        }

        return heading;
    }

    /// <summary>
    /// One clickable event card: an accent time gutter and the title column. The times describe
    /// the slice of the event that falls on <paramref name="date"/>, so a multi-day event shows
    /// the part that belongs to the selected day. <c>selectAction</c> expands the event details;
    /// the expanded row is marked by the accent title and the details block below the agenda.
    /// </summary>
    private static JsonObject BuildEventCard(CalendarEvent item, DateOnly date, string? selectedEventKey)
    {
        var slice = EventRange.ClipToRange(item, date, date.AddDays(1)) ?? item;
        var timeText = item.IsAllDay ? AllDayLabel : FormatTimeRange(slice);
        var metaText = FormatEventMeta(item, date);
        var itemKey = EventKey(item);
        var isSelected = string.Equals(itemKey, selectedEventKey, StringComparison.Ordinal);

        var details = new JsonArray
        {
            (JsonNode)TextBlock(
                item.DisplayTitle,
                color: isSelected ? "accent" : null,
                weight: "bolder",
                wrap: true),
        };

        if (!string.IsNullOrEmpty(metaText))
        {
            // Location and multi-day context stay a second, quieter line under the title.
            details.Add((JsonNode)TextBlock(metaText, size: "small", subtle: true, spacing: "none", wrap: true));
        }

        return new JsonObject
        {
            ["type"] = "ColumnSet",
            ["verticalContentAlignment"] = "center",
            ["spacing"] = SegmentSpacing,
            ["selectAction"] = new JsonObject
            {
                ["type"] = "Action.Submit",
                ["data"] = new JsonObject
                {
                    [EventPayloadKey] = itemKey,
                },
            },
            ["columns"] = new JsonArray
            {
                // Accent time gutter: the only coloured text in an idle event row.
                (JsonNode)BuildColumn(
                    "auto",
                    TextBlock(timeText, size: "small", color: "accent", weight: "bolder")),
                (JsonNode)BuildColumn("stretch", details),
            },
        };
    }

    /// <summary>
    /// The expanded details block: transparent (no filled surface), opened by one subtle
    /// separator, with the event title in the accent colour. It repeats the day and the full
    /// time range, then the location, the notes and the meeting commands.
    /// </summary>
    private static void AddEventDetails(JsonArray body, CalendarEvent item, DateOnly date, string? notice)
    {
        var eventKey = EventKey(item);

        body.Add((JsonNode)TextBlock(
            item.DisplayTitle,
            size: "medium",
            weight: "bolder",
            color: "accent",
            spacing: SectionSpacing,
            separator: true,
            wrap: true));

        body.Add((JsonNode)TextBlock(
            FormatEventWhenLine(item, date),
            size: "small",
            subtle: true,
            spacing: RowSpacing,
            wrap: true));

        if (!string.IsNullOrWhiteSpace(item.Location))
        {
            body.Add((JsonNode)TextBlock(
                item.Location.Trim(),
                size: "small",
                subtle: true,
                spacing: "none",
                wrap: true));
        }

        if (!string.IsNullOrWhiteSpace(item.Details))
        {
            body.Add((JsonNode)TextBlock(
                FormatDetailsText(item.Details),
                size: "small",
                subtle: true,
                spacing: RowSpacing,
                wrap: true));
        }

        if (!string.IsNullOrEmpty(notice))
        {
            body.Add((JsonNode)TextBlock(notice, size: "small", color: "accent", spacing: RowSpacing, wrap: true));
        }

        body.Add((JsonNode)BuildDetailsActions(item, eventKey));
    }

    /// <summary>
    /// The full "when" line of the expanded event: "Saturday, 7 November 2026 · 10:00–10:45",
    /// with "All day" for all-day events and "Day 2 of 3" for multi-day ones.
    /// </summary>
    private static string FormatEventWhenLine(CalendarEvent item, DateOnly date)
    {
        var culture = CultureInfo.CurrentCulture;
        var dayText = date.ToDateTime(TimeOnly.MinValue).ToString("dddd, d MMMM yyyy", culture);
        var slice = EventRange.ClipToRange(item, date, date.AddDays(1)) ?? item;
        var timeText = item.IsAllDay ? AllDayLabel : FormatTimeRange(slice);

        var parts = new List<string>(3)
        {
            dayText,
            timeText,
        };

        var span = EventRange.GetLocalDaySpan(item);
        if (span is not null && span.Value.LastDay > span.Value.FirstDay)
        {
            var dayNumber = date.DayNumber - span.Value.FirstDay.DayNumber + 1;
            var dayCount = span.Value.LastDay.DayNumber - span.Value.FirstDay.DayNumber + 1;
            parts.Add($"Day {dayNumber} of {dayCount}");
        }

        return string.Join(EventSeparator, parts);
    }

    /// <summary>
    /// Flattens and shortens the notes paragraph so a long description cannot make the card
    /// unreadable; the full text stays available in the calendar app.
    /// </summary>
    private static string FormatDetailsText(string details)
    {
        var flattened = string.Join(
            ' ',
            details.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flattened.Length <= DetailsPreviewLength)
        {
            return flattened;
        }

        var cut = flattened.LastIndexOf(' ', DetailsPreviewLength);
        if (cut < DetailsPreviewLength / 2)
        {
            cut = DetailsPreviewLength;
        }

        return flattened[..cut].TrimEnd() + "\u2026";
    }

    private static JsonObject BuildDetailsActions(CalendarEvent item, string eventKey)
    {
        var actions = new JsonArray();

        if (item.HasOnlineMeetingLink)
        {
            // "positive" is the Adaptive Cards action style for the primary, constructive
            // command; a renderer that does not paint it still shows a default button.
            actions.Add((JsonNode)BuildSubmitAction(
                JoinMeetingLabel,
                "details-join",
                EventActionData(eventKey, JoinEventAction),
                style: "positive"));
            actions.Add((JsonNode)BuildSubmitAction(
                CopyLinkLabel,
                "details-copy",
                EventActionData(eventKey, CopyEventAction)));
        }

        actions.Add((JsonNode)BuildSubmitAction(
            CloseDetailsLabel,
            "details-close",
            new JsonObject
            {
                [EventActionPayloadKey] = CloseEventAction,
            }));

        return new JsonObject
        {
            ["type"] = "ActionSet",
            ["spacing"] = RowSpacing,
            ["actions"] = actions,
        };
    }

    private static JsonObject EventActionData(string eventKey, string eventAction) => new()
    {
        [EventPayloadKey] = eventKey,
        [EventActionPayloadKey] = eventAction,
    };

    private static string FormatTimeRange(CalendarEvent item)
    {
        var start = item.Start.LocalDateTime;
        var end = item.End.LocalDateTime;
        if (end <= start)
        {
            return start.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        // A slice that reaches the end of the day is reported as 24:00 instead of 00:00 of the
        // following day, which would read like a second day.
        var dayEnd = start.Date.AddDays(1);
        var endText = end == dayEnd
            ? "24:00"
            : end.ToString("HH:mm", CultureInfo.InvariantCulture);

        return string.Concat(
            start.ToString("HH:mm", CultureInfo.InvariantCulture),
            "\u2013",
            endText);
    }

    private static string FormatEventMeta(CalendarEvent item, DateOnly date)
    {
        var parts = new List<string>(2);
        var span = EventRange.GetLocalDaySpan(item);
        if (span is not null && span.Value.LastDay > span.Value.FirstDay)
        {
            var dayNumber = date.DayNumber - span.Value.FirstDay.DayNumber + 1;
            var dayCount = span.Value.LastDay.DayNumber - span.Value.FirstDay.DayNumber + 1;
            parts.Add($"{MultiDayLabel}, day {dayNumber} of {dayCount}");
        }

        if (!string.IsNullOrWhiteSpace(item.Location))
        {
            parts.Add(item.Location.Trim());
        }

        return string.Join(EventSeparator, parts);
    }

    /// <summary>
    /// Builds an Action.Submit button. The visible title can be a compact glyph while the tooltip
    /// carries the full label; the id and the <c>data.nav</c> payload are unchanged from the
    /// previous layout so the command handling is identical.
    /// </summary>
    private static JsonObject BuildNavigationAction(string title, string action, string id, string? tooltip) =>
        BuildSubmitAction(
            title,
            id,
            new JsonObject
            {
                [NavigationPayloadKey] = action,
            },
            tooltip: tooltip);

    private static JsonObject BuildSubmitAction(
        string title,
        string id,
        JsonObject data,
        string? style = null,
        string? tooltip = null)
    {
        var button = new JsonObject
        {
            ["type"] = "Action.Submit",
            ["id"] = id,
            ["title"] = title,
            ["data"] = data,
        };

        if (style is not null)
        {
            button["style"] = style;
        }

        if (tooltip is not null)
        {
            button["tooltip"] = tooltip;
        }

        return button;
    }

    private static JsonObject BuildColumn(string width, JsonObject content) =>
        BuildColumn(width, new JsonArray { (JsonNode)content });

    private static JsonObject BuildColumn(string width, JsonArray items) => new()
    {
        ["type"] = "Column",
        ["width"] = width,
        ["items"] = items,
    };

    private static JsonObject TextBlock(
        string text,
        string? color = null,
        string? weight = null,
        string? size = null,
        string? alignment = null,
        string? spacing = null,
        bool subtle = false,
        bool separator = false,
        bool wrap = false)
    {
        var block = new JsonObject
        {
            ["type"] = "TextBlock",
            ["text"] = Literal(text),
            ["wrap"] = wrap,
        };

        if (color is not null)
        {
            block["color"] = color;
        }

        if (subtle)
        {
            // The host maps isSubtle to the theme-aware secondary text color; the explicit
            // "light" palette color would be invisible on a light card.
            block["isSubtle"] = true;
        }

        if (weight is not null)
        {
            block["weight"] = weight;
        }

        if (size is not null)
        {
            block["size"] = size;
        }

        if (alignment is not null)
        {
            block["horizontalAlignment"] = alignment;
        }

        if (spacing is not null)
        {
            block["spacing"] = spacing;
        }

        if (separator)
        {
            block["separator"] = true;
        }

        return block;
    }

    /// <summary>
    /// Neutralizes Adaptive Card template placeholders (<c>${...}</c>) inside dynamic text.
    /// The host expands the card with the Adaptive Cards template engine, so an event title
    /// that happens to contain <c>${</c> must be treated as data, not as a binding.
    /// </summary>
    private static string Literal(string text) => text.Replace("${", "$ {", StringComparison.Ordinal);

    /// <summary>One card action decoded from the submit payload.</summary>
    /// <param name="Navigation">Month navigation action key, when the payload came from a button.</param>
    /// <param name="Day">Selected date, when the payload came from a day cell.</param>
    /// <param name="EventKey">Stable event key, when the payload came from an event row or command.</param>
    /// <param name="EventAction">Event details command key ("join", "copy" or "close").</param>
    private readonly record struct CalendarAction(
        string? Navigation,
        DateOnly? Day,
        string? EventKey,
        string? EventAction);
}

/// <summary>
/// The Adaptive Card the calendar page hands to the host. <see cref="FormContent"/> owns the
/// <c>SubmitForm</c> callbacks for the card's Action.Submit actions, so the form forwards
/// them to the page and the page owns month navigation and day selection. The host re-renders
/// the card when <see cref="FormContent.TemplateJson"/> changes, so background event loading
/// only needs to update the template.
/// </summary>
internal sealed partial class CalendarForm : FormContent
{
    private readonly Func<string, string?, CommandResult> _submitForm;

    public CalendarForm(Func<string, string?, CommandResult> submitForm)
    {
        _submitForm = submitForm;

        // Binding data is unused; an empty object keeps the host's template expansion well
        // defined even though the card contains no ${...} placeholders.
        DataJson = "{}";
    }

    public override ICommandResult SubmitForm(string inputs) => _submitForm(inputs, null);

    public override ICommandResult SubmitForm(string inputs, string data) => _submitForm(inputs, data);
}
