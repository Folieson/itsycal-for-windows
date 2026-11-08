using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace ItsycalForWindows.Flyout;

public sealed partial class MainWindow : Window, IDisposable
{
    private const double BaseWidthDip = 360;

    // Spec 0004 AC-1: content-hugging client height clamped to 448-600 DIP. The floor keeps
    // the month block at or below 50% of the client height; the ceiling is absolute, so an
    // expanded details card larger than the window leaves the agenda ScrollViewer to scroll.
    private const double MinHeightDip = 448;
    private const double MaxHeightDip = 600;
    private const double MonthBlockBudgetDip = 224;
    private const double MonthBlockBudgetShare = 0.50;

    private readonly TrayIcon _tray;
    private readonly Dictionary<DateOnly, List<SpikeEvent>> _events = new();
    private readonly List<(SpikeEvent Event, Button Button)> _rows = new();
    private readonly SolidColorBrush _transparent = new(Colors.Transparent);

    // Screenshot/verification hook: keeps the flyout visible when it loses focus.
    private readonly bool _stayVisible =
        Environment.GetEnvironmentVariable("ITSYCAL_FLYOUT_STAY_VISIBLE") == "1";
    private readonly bool _logLayout =
        Environment.GetEnvironmentVariable("ITSYCAL_FLYOUT_LAYOUT_LOG") == "1";
    private readonly string _captureState =
        Environment.GetEnvironmentVariable("ITSYCAL_FLYOUT_STATE")?.Trim().ToLowerInvariant() ?? "default";
    private readonly string _fixture =
        Environment.GetEnvironmentVariable("ITSYCAL_FLYOUT_FIXTURE")?.Trim().ToLowerInvariant() ?? "ordinary";
    private readonly string _scrollMode =
        Environment.GetEnvironmentVariable("ITSYCAL_FLYOUT_SCROLL")?.Trim().ToLowerInvariant() ?? "none";
    private readonly double _uiScale = ReadUiScale();

    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateOnly _selected = DateOnly.FromDateTime(DateTime.Today);
    private SpikeEvent? _selectedEvent;
    private Button? _selectedRow;
    private Border? _detailsCard;
    private TextBlock? _statusText;
    private Button? _joinButton;
    private Button? _copyButton;
    private StackPanel? _actionsPanel;
    private Func<bool>? _focusOnActivate;
    private DisplayAreaWatcher? _displayWatcher;
    private XamlRoot? _xamlRoot;
    private double _lastRasterizationScale = 1.0;
    private long _lightDismissHiddenAtTicks;
    private int _focusAttempts;
    private int _lightDismissGeneration;
    private bool _windowActive;
    private bool _focusVerifyQueued;
    private bool _readyToLog;
    private bool _exiting;
    private bool _hidden;
    private bool _lightDismissPending;
    private int _relayoutQueued;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Itsycal";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new DesktopAcrylicBackdrop();

        ConfigureWindow();

        // The tray must exist before the first ResizeToContent call: positioning asks it for the
        // icon rectangle and falls back to the cursor monitor while it is unavailable.
        _tray = new TrayIcon(DispatcherQueue, ToggleFromTray, ToggleFromHotkey, ExitApplication);
        _tray.IconAvailabilityChanged += (_, available) =>
        {
            RefreshStatusLine();

            // If Explorer restarted and the icon could not be re-added, a hidden flyout with
            // no hotkey would be unreachable. Surface it (and its Ctrl+Q route) instead.
            if (!available && !_tray.HotkeyRegistered)
            {
                ShowFlyout();
            }
        };
        StartDisplayWatcher();

        SeedFakeEvents();
        BuildWeekdayHeader();
        BuildCalendar();
        ShowSelectedDay(_selected);
        ApplyCaptureState();
        SetUpKeyboard();
        RefreshStatusLine();

        Activated += (_, args) =>
        {
            _windowActive = args.WindowActivationState != WindowActivationState.Deactivated;
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                // Light dismiss is deferred by one dispatcher turn so the tray callback that
                // caused it can cancel it. Without the deferral the sequence is
                // deactivate -> hide -> tray callback -> re-show (the toggle race).
                if (!_exiting && !_stayVisible)
                {
                    QueueLightDismiss();
                }
            }
            else
            {
                CancelPendingLightDismiss();
                _hidden = false;
                if (_windowActive)
                {
                    _focusAttempts = 0;
                    _focusVerifyQueued = false;
                    DispatcherQueue.TryEnqueue(TryApplyFocus);
                }

                QueueLayoutLog();
            }
        };
        RootHost.Loaded += (_, _) =>
        {
            _readyToLog = true;
            AttachXamlRoot();
            // The first ShowSelectedDay runs before the window is loaded; settle the real
            // actuals now so the initial client height hugs the five-row fixture.
            ResizeToContent();
            DispatcherQueue.TryEnqueue(() =>
            {
                LogLayout();
                TryApplyFocus();
            });
        };
        AppWindow.Closing += (_, args) =>
        {
            if (_exiting)
            {
                return;
            }

            if (!_tray.IsIconVisible)
            {
                // No tray icon: the flyout is the only remaining surface, so Close exits
                // instead of hiding an app the user could no longer reach.
                _exiting = true;
                return;
            }

            args.Cancel = true;
            HideFlyout();
        };
        Closed += (_, _) => Dispose();
    }

    private static double ReadUiScale()
    {
        var raw = Environment.GetEnvironmentVariable("ITSYCAL_FLYOUT_UI_SCALE");
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)
               && scale > 0.5 && scale <= 3.0
            ? scale
            : 1.0;
    }

    private void ConfigureWindow()
    {
        AppWindow.Title = "Itsycal";
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        // The flyout is a transient tray surface, not a document window. Keep it out of the
        // taskbar and Alt+Tab; the tray icon and hotkey are its entry points.
        AppWindow.IsShownInSwitchers = false;

        var corner = DwmWindowCornerPreference.Round;
        _ = DwmSetWindowAttribute(WindowNative.GetWindowHandle(this), DwmWindowAttribute.WindowCornerPreference, ref corner, sizeof(int));
    }

    /// <summary>
    /// Content-hugging resize: composes the client height from the laid-out parts and sets the
    /// window to it. It is clamped to 448-600 DIP (spec 0004 AC-1): the floor keeps the month
    /// block within its 50% share, the ceiling leaves an oversized details card to the agenda
    /// ScrollViewer. Measuring the actual panel avoids a stale ScrollViewer viewport that made
    /// an infinite-height measure undershoot the five-row fixture.
    /// </summary>
    private void ResizeToContent()
    {
        RootGrid.ClearValue(FrameworkElement.HeightProperty);
        RootHost.UpdateLayout();

        double contentHeight;
        try
        {
            var monthTop = MonthHeader.TransformToVisual(RootGrid).TransformPoint(new Point(0, 0)).Y;
            var gridTop = DayGrid.TransformToVisual(RootGrid).TransformPoint(new Point(0, 0)).Y;
            var monthBlock = gridTop + DayGrid.ActualHeight - monthTop;
            var chrome = Card.Margin.Top + Card.Margin.Bottom
                         + Card.BorderThickness.Top + Card.BorderThickness.Bottom
                         + CardContent.Padding.Top + CardContent.Padding.Bottom;
            contentHeight = chrome
                            + monthBlock
                            + DayGrid.Margin.Bottom
                            + AgendaHeaderRow.ActualHeight
                            + AgendaHeaderRow.Margin.Bottom
                            + AgendaPanel.ActualHeight
                            + (SystemStatusText.Visibility == Visibility.Visible
                                ? SystemStatusText.ActualHeight + SystemStatusText.Margin.Top
                                : 0);
            DiagnosticLog.Write(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"MEASURE chrome={chrome:0.#} month={monthBlock:0.#} header={AgendaHeaderRow.ActualHeight:0.#} panel={AgendaPanel.ActualHeight:0.#} content={contentHeight:0.#} rows={_rows.Count}"));
        }
        catch (Exception ex)
        {
            // The constructor calls ShowSelectedDay before the content is in the visual tree,
            // where TransformToVisual is not available yet. The Loaded handler re-runs this
            // method once the actuals exist; until then fall back to a plain measure.
            DiagnosticLog.Write("MEASURE fallback kind=" + ex.GetType().Name);
            RootGrid.Measure(new Size(BaseWidthDip, double.PositiveInfinity));
            contentHeight = RootGrid.DesiredSize.Height;
        }

        if (contentHeight <= 0 || AgendaPanel.ActualHeight <= 0)
        {
            RootGrid.Measure(new Size(BaseWidthDip, double.PositiveInfinity));
            contentHeight = RootGrid.DesiredSize.Height;
        }

        // +1 DIP of rounding headroom: the client rect and the XAML layout root can differ by a
        // fraction of a DIP, which otherwise leaves a 1 DIP scrollable gap and a scrollbar on
        // the five-row collapsed fixture.
        var heightDip = Math.Clamp(Math.Ceiling(contentHeight) + 1, MinHeightDip, MaxHeightDip);

        var hwnd = WindowNative.GetWindowHandle(this);
        var dpiScale = GetDpiForWindow(hwnd) / 96.0;
        var widthPx = (int)Math.Round(BaseWidthDip * dpiScale * _uiScale);
        var heightPx = (int)Math.Round(heightDip * dpiScale * _uiScale);

        // AppWindow.Resize sizes the outer window; the borderless window still carries a
        // 3 px non-client resize frame per side. Compensate so the XAML client area is the
        // recorded 360 x height DIP (the review measures the client).
        _ = GetWindowRect(hwnd, out var windowRect);
        _ = GetClientRect(hwnd, out var clientRect);
        var frameWidth = (windowRect.Right - windowRect.Left) - (clientRect.Right - clientRect.Left);
        var frameHeight = (windowRect.Bottom - windowRect.Top) - (clientRect.Bottom - clientRect.Top);
        if (frameWidth < 0 || frameWidth > 40) frameWidth = 0;
        if (frameHeight < 0 || frameHeight > 40) frameHeight = 0;

        if (_uiScale != 1.0)
        {
            // The host runs at 96 DPI; lay out at 360 DIP and scale the visual so the capture
            // shows the same composition a 150% monitor would render.
            RootGrid.Width = BaseWidthDip;
            RootGrid.Height = heightDip;
            RootGrid.HorizontalAlignment = HorizontalAlignment.Left;
            RootGrid.VerticalAlignment = VerticalAlignment.Top;
            RootGrid.RenderTransform = new ScaleTransform { ScaleX = _uiScale, ScaleY = _uiScale };
        }

        AppWindow.Resize(new SizeInt32(widthPx + frameWidth, heightPx + frameHeight));
        PositionFlyout(widthPx + frameWidth, heightPx + frameHeight, dpiScale);
        QueueLayoutLog();
    }

    /// <summary>
    /// Production start: hidden when the tray icon gives us a way back, visible for the
    /// screenshot workflow and whenever the tray icon is unavailable (the flyout then carries
    /// the visible quit route).
    /// </summary>
    public bool ShouldStartVisible => _stayVisible || !_tray.IsIconVisible;

    public void ShowFlyout()
    {
        if (_disposed)
        {
            return;
        }

        CancelPendingLightDismiss();
        _lightDismissHiddenAtTicks = 0;
        _hidden = false;
        DiagnosticLog.Write("FLYOUT show");

        // Reposition before the window becomes visible so it never flashes at a stale spot.
        RepositionCurrentWindow();
        AppWindow.Show();
        Activate();
    }

    private void HideFlyout()
    {
        CancelPendingLightDismiss();
        HideFlyoutCore();
    }

    private void HideFlyoutCore()
    {
        _hidden = true;
        DiagnosticLog.Write("FLYOUT hide");
        AppWindow.Hide();
    }

    private void QueueLightDismiss()
    {
        if (_disposed || _hidden || _stayVisible || _exiting)
        {
            return;
        }

        // With neither tray icon nor hotkey there is no way to bring a hidden flyout back.
        // Keep it on screen so its Close/Ctrl+Q route stays reachable.
        if (!_tray.IsIconVisible && !_tray.HotkeyRegistered)
        {
            return;
        }

        _lightDismissPending = true;
        var generation = ++_lightDismissGeneration;
        DiagnosticLog.Write("LIGHTDISMISS queued");
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (_disposed || !_lightDismissPending || generation != _lightDismissGeneration)
                {
                    return;
                }

                _lightDismissPending = false;
                _lightDismissHiddenAtTicks = Environment.TickCount64;
                DiagnosticLog.Write("LIGHTDISMISS hide");
                HideFlyoutCore();
            }))
        {
            _lightDismissPending = false;
        }
    }

    private void CancelPendingLightDismiss()
    {
        _lightDismissPending = false;
        _lightDismissGeneration++;
    }

    /// <summary>
    /// Tray click toggle. When the click's activation change is still pending, the click owns
    /// the action: close immediately and cancel the deferred light-dismiss callback. If the
    /// deferred hide won the race, the same click has already closed the flyout, so it must not
    /// instantly re-open it.
    /// </summary>
    private void ToggleFromTray()
    {
        if (_disposed)
        {
            return;
        }

        DiagnosticLog.Write($"TOGGLE source=tray pending={_lightDismissPending} visible={!_hidden && AppWindow.IsVisible}");

        if (_lightDismissPending)
        {
            HideFlyout();
            return;
        }

        if (!_hidden && AppWindow.IsVisible)
        {
            HideFlyout();
            return;
        }

        var lightDismissedRecently = _lightDismissHiddenAtTicks != 0
                                     && Environment.TickCount64 - _lightDismissHiddenAtTicks <= 400;
        _lightDismissHiddenAtTicks = 0;
        if (lightDismissedRecently)
        {
            return;
        }

        ShowFlyout();
    }

    private void ToggleFromHotkey()
    {
        if (_disposed)
        {
            return;
        }

        DiagnosticLog.Write($"TOGGLE source=hotkey pending={_lightDismissPending} visible={!_hidden && AppWindow.IsVisible}");

        if (!_hidden && AppWindow.IsVisible)
        {
            HideFlyout();
        }
        else
        {
            ShowFlyout();
        }
    }

    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        // Closed disposes the tray, hotkey, display watcher and single-instance objects.
        Close();
    }

    /// <summary>
    /// Physical-pixel placement. Preferred anchor is the notification icon rectangle, which
    /// keeps the flyout above the correct taskbar edge on multi-monitor and non-bottom taskbars.
    /// Falls back to the cursor's monitor, then the primary monitor; always clamps into a work
    /// area. Called again on every show and after display/DPI changes.
    /// </summary>
    private void PositionFlyout(int width, int height, double scale)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var margin = (int)Math.Round(12 * scale);
        DisplayArea? area = null;
        var x = 0;
        var y = 0;
        var anchoredToTray = false;

        if (_tray.TryGetIconRect(out var iconRect))
        {
            try
            {
                area = DisplayArea.GetFromPoint(
                    new PointInt32((iconRect.Left + iconRect.Right) / 2, (iconRect.Top + iconRect.Bottom) / 2),
                    DisplayAreaFallback.Nearest);
            }
            catch (Exception)
            {
                area = null;
            }

            if (area is not null)
            {
                var work = area.WorkArea;
                x = iconRect.Right - width;
                y = iconRect.Top - height - margin;
                if (y < work.Y)
                {
                    // Taskbar at the top: open below the icon instead of off-screen.
                    y = iconRect.Bottom + margin;
                }

                anchoredToTray = true;
            }
        }

        if (!anchoredToTray)
        {
            area = DisplayAreaFromCursor();
            if (area is null)
            {
                try
                {
                    area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
                }
                catch (Exception)
                {
                    area = null;
                }
            }

            area ??= DisplayArea.Primary;
            var work = area.WorkArea;
            x = work.X + work.Width - width - margin;
            y = work.Y + work.Height - height - margin;
        }

        var workArea = (area ?? DisplayArea.Primary).WorkArea;
        var maxX = Math.Max(workArea.X, workArea.X + workArea.Width - width);
        var maxY = Math.Max(workArea.Y, workArea.Y + workArea.Height - height);
        x = Math.Clamp(x, workArea.X, maxX);
        y = Math.Clamp(y, workArea.Y, maxY);
        AppWindow.Move(new PointInt32(x, y));
    }

    private void RepositionCurrentWindow()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var scale = GetDpiForWindow(hwnd) / 96.0;
            var size = AppWindow.Size;
            PositionFlyout(size.Width, size.Height, scale);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("POSITION failed kind=" + ex.GetType().Name);
        }
    }

    private static DisplayArea? DisplayAreaFromCursor()
    {
        try
        {
            if (GetCursorPos(out var point))
            {
                return DisplayArea.GetFromPoint(
                    new PointInt32(point.X, point.Y),
                    DisplayAreaFallback.Nearest);
            }
        }
        catch (Exception)
        {
            // Fall back to the primary display.
        }

        return null;
    }

    private void StartDisplayWatcher()
    {
        try
        {
            _displayWatcher = DisplayArea.CreateWatcher();
            _displayWatcher.Added += (_, _) => QueueRelayout();
            _displayWatcher.Removed += (_, _) => QueueRelayout();
            _displayWatcher.Updated += (_, _) => QueueRelayout();
            _displayWatcher.Start();
        }
        catch (Exception ex)
        {
            // Re-layout also happens through XamlRoot.Changed and on every show; this watcher
            // is the early warning for resolution/DPI/work-area changes.
            DiagnosticLog.Write("DISPLAY watcher unavailable kind=" + ex.GetType().Name);
        }
    }

    private void QueueRelayout()
    {
        if (_disposed)
        {
            return;
        }

        // DisplayAreaWatcher callbacks are not guaranteed to arrive on the UI thread; the
        // interlocked gate keeps concurrent display events from queuing duplicate layouts.
        if (Interlocked.Exchange(ref _relayoutQueued, 1) == 1)
        {
            return;
        }

        if (!DispatcherQueue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _relayoutQueued, 0);
                if (_disposed)
                {
                    return;
                }

                ResizeToContent();
            }))
        {
            Interlocked.Exchange(ref _relayoutQueued, 0);
        }
    }

    private void AttachXamlRoot()
    {
        var root = RootHost.XamlRoot;
        if (root is null || ReferenceEquals(_xamlRoot, root))
        {
            return;
        }

        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed -= OnXamlRootChanged;
        }

        _xamlRoot = root;
        _lastRasterizationScale = root.RasterizationScale;
        root.Changed += OnXamlRootChanged;
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        // Size changes caused by our own ResizeToContent must not queue another resize; only a
        // rasterization-scale (DPI) change needs a new physical-pixel layout.
        if (Math.Abs(sender.RasterizationScale - _lastRasterizationScale) < 0.001)
        {
            return;
        }

        _lastRasterizationScale = sender.RasterizationScale;
        QueueRelayout();
    }

    private void RefreshStatusLine()
    {
        var trayAvailable = _tray.IsIconVisible;
        var hotkeyAvailable = _tray.HotkeyRegistered;

        // The capture workflow forces the window visible in a low-integrity sandbox where the
        // shell refuses notification icons. Those sandbox-only warnings are not part of the UI
        // under review, so they must not change the captured pixels.
        if (_stayVisible || (trayAvailable && hotkeyAvailable))
        {
            if (SystemStatusText.Visibility != Visibility.Collapsed)
            {
                SystemStatusText.Text = string.Empty;
                SystemStatusText.Visibility = Visibility.Collapsed;
                ResizeToContent();
            }

            return;
        }

        var text = !trayAvailable && !hotkeyAvailable
            ? "Tray icon and Ctrl+Alt+C are unavailable. Press Ctrl+Q to quit."
            : !trayAvailable
                ? "Tray icon unavailable. Press Ctrl+Alt+C to reopen, or Ctrl+Q to quit."
                : "Ctrl+Alt+C is unavailable. Open Itsycal from the tray icon; press Ctrl+Q to quit.";

        if (!string.Equals(SystemStatusText.Text, text, StringComparison.Ordinal))
        {
            SystemStatusText.Text = text;
        }

        SystemStatusText.Visibility = Visibility.Visible;
        ResizeToContent();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelPendingLightDismiss();

        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed -= OnXamlRootChanged;
            _xamlRoot = null;
        }

        try
        {
            _displayWatcher?.Stop();
        }
        catch (Exception)
        {
            // The watcher may already be stopped by the OS.
        }

        _displayWatcher = null;
        _tray.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SeedFakeEvents()
    {
        // Deterministic fixture. The ordinary day is exactly the five-row ordinary fixture of
        // spec 0004 (one three-day all-day row, one single all-day row and three timed rows,
        // one meeting link and four no-link rows). ITSYCAL_FLYOUT_FIXTURE=long swaps in the
        // one-event long fixture used for the grow-then-scroll evidence.
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (_fixture == "long")
        {
            _events[today] =
            [
                new SpikeEvent(
                    new TimeOnly(14, 0),
                    new TimeOnly(15, 30),
                    "Quarterly planning review with platform, design, operations and support",
                    "Building 7, Conference Room 12, North Campus",
                    "https://teams.microsoft.com/l/meetup-join/19%3ameeting_compactfixture%40thread.v2/0",
                    "Agenda: quarterly roadmap, platform dependencies, accessibility review results, and the remaining launch blockers. Decisions needed on the Windows 11 packaging, the tray flyout telemetry-free update path, the shared core extraction, and the federated provider roadmap. Please read the attached brief before the meeting and bring the open questions for the operations, support, and design representatives. This paragraph is intentionally longer than the 480-character preview so the expanded details exercise truncation and scrolling."),
            ];
            _events[today.AddDays(11)] = [];
            return;
        }

        _events[today] =
        [
            new SpikeEvent(
                default,
                default,
                "Release freeze",
                "Building 7",
                null,
                null,
                IsAllDay: true,
                DayIndex: 2,
                DayCount: 3),
            new SpikeEvent(
                new TimeOnly(9, 0),
                new TimeOnly(9, 30),
                "Design review",
                "Google Meet",
                "https://meet.google.com/abc-defg-hij",
                null),
            new SpikeEvent(
                new TimeOnly(11, 0),
                new TimeOnly(12, 0),
                "Lunch with the team",
                "Cafeteria",
                null,
                null),
            new SpikeEvent(
                new TimeOnly(15, 0),
                new TimeOnly(15, 30),
                "Focus block",
                null,
                null,
                null),
            new SpikeEvent(
                default,
                default,
                "Company offsite",
                "Campus",
                null,
                null,
                IsAllDay: true),
        ];

        // selected != today state: an ordinary day with two readable rows.
        var secondDay = today.AddDays(4);
        _events[secondDay] =
        [
            new SpikeEvent(new TimeOnly(14, 0), new TimeOnly(15, 0), "Team retro", "Room B", null, "What went well, what to change."),
            new SpikeEvent(new TimeOnly(16, 0), new TimeOnly(16, 30), "Focus block", null, null, null),
        ];

        _events[today.AddDays(11)] = [];
    }

    private void BuildWeekdayHeader()
    {
        WeekdayHeader.ColumnDefinitions.Clear();
        WeekdayHeader.Children.Clear();
        for (var i = 0; i < 7; i++)
        {
            WeekdayHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var text = new TextBlock
            {
                Text = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames[(i + 1) % 7],
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            Grid.SetColumn(text, i);
            WeekdayHeader.Children.Add(text);
        }
    }

    private void BuildCalendar()
    {
        MonthTitle.Text = _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        DayGrid.RowDefinitions.Clear();
        DayGrid.ColumnDefinitions.Clear();
        DayGrid.Children.Clear();
        for (var c = 0; c < 7; c++)
        {
            DayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        // 27 DIP rows keep the whole month block (header + weekday + six rows + spacing)
        // at 220 DIP, inside the 224-DIP absolute budget of spec 0004 AC-1a. The 1 DIP
        // RowSpacing still separates rows; a 30 DIP row put the block at 238 DIP.
        for (var r = 0; r < 6; r++)
        {
            DayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(27) });
        }

        var first = _month;
        var offset = ((int)first.DayOfWeek + 6) % 7; // Monday-first
        var start = first.AddDays(-offset);
        var today = DateOnly.FromDateTime(DateTime.Today);

        for (var i = 0; i < 42; i++)
        {
            var date = DateOnly.FromDateTime(start.AddDays(i));
            var isSelected = date == _selected;
            var count = _events.TryGetValue(date, out var list) ? list.Count : 0;
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Background = isSelected ? ThemeBrush("SubtleFillColorSecondaryBrush") : _transparent,
                BorderBrush = isSelected ? ThemeBrush("AccentFillColorDefaultBrush") : _transparent,
                BorderThickness = new Thickness(isSelected ? 1 : 0),
                CornerRadius = new CornerRadius(6),
                FontSize = 12,
                UseSystemFocusVisuals = true,
                Content = BuildDayContent(date, today, isSelected),
            };
            AutomationProperties.SetName(
                button,
                date.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture)
                + (date == today ? ", today" : string.Empty)
                + (count == 0 ? ", no events" : count == 1 ? ", 1 event" : $", {count} events"));
            button.Click += (_, _) => SelectDay(date);
            Grid.SetRow(button, i / 7);
            Grid.SetColumn(button, i % 7);
            DayGrid.Children.Add(button);
        }
    }

    private StackPanel BuildDayContent(DateOnly date, DateOnly today, bool isSelected)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 1,
        };

        var isAdjacent = date.Month != _month.Month || date.Year != _month.Year;
        var number = new TextBlock
        {
            Text = date.Day.ToString(CultureInfo.CurrentCulture),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 12,
            FontWeight = date == _selected || date == today ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = isAdjacent
                ? ThemeBrush("TextFillColorTertiaryBrush")
                : ThemeBrush("TextFillColorPrimaryBrush"),
        };
        panel.Children.Add(number);

        if (date == today)
        {
            // The today marker is independent of selection so it stays visible when
            // selected == today (finding 15).
            panel.Children.Add(new Border
            {
                Height = 2,
                Width = 10,
                CornerRadius = new CornerRadius(1),
                Background = ThemeBrush("AccentFillColorDefaultBrush"),
            });
        }
        else if (_events.TryGetValue(date, out var dayEvents) && dayEvents.Count > 0)
        {
            // Month event marker stays neutral; accent is reserved for selection/today.
            panel.Children.Add(new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = ThemeBrush("TextFillColorSecondaryBrush"),
            });
        }

        return panel;
    }

    private void SelectDay(DateOnly date)
    {
        _selected = date;
        if (date.Month != _month.Month || date.Year != _month.Year)
        {
            _month = new DateTime(date.Year, date.Month, 1);
        }

        BuildCalendar();
        ShowSelectedDay(date);
    }

    private void ChangeMonth(int delta)
    {
        _month = _month.AddMonths(delta);
        BuildCalendar();
    }

    private void GoToToday() => SelectDay(DateOnly.FromDateTime(DateTime.Today));

    private void ShowSelectedDay(DateOnly date)
    {
        _selected = date;
        AgendaHeader.Text = date == DateOnly.FromDateTime(DateTime.Today)
            ? "Today"
            : date.ToString("dddd", CultureInfo.CurrentCulture);
        AgendaDate.Text = date.ToString("MMM d", CultureInfo.CurrentCulture);
        ClearDetails();
        _rows.Clear();
        AgendaPanel.Children.Clear();

        var events = _events.TryGetValue(date, out var list) ? list : new List<SpikeEvent>();
        if (events.Count == 0)
        {
            AgendaPanel.Children.Add(new TextBlock
            {
                Text = "No events on this day.",
                FontSize = 12,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                Margin = new Thickness(2, 10, 0, 0),
            });
        }
        else
        {
            foreach (var item in events)
            {
                var row = BuildAgendaRow(item);
                _rows.Add((item, row));
                AgendaPanel.Children.Add(row);
            }
        }

        ResizeToContent();
    }

    private Button BuildAgendaRow(SpikeEvent item)
    {
        var row = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            MinWidth = 0,
            MinHeight = 40,
            Padding = new Thickness(8, 6, 8, 6),
            Background = _transparent,
            BorderBrush = _transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            UseSystemFocusVisuals = true,
        };
        var rowMeta = RowMeta(item);
        var hasLink = MeetingLinks.IsUsable(item.MeetingLink);
        AutomationProperties.SetName(
            row,
            $"{TimeText(item)}, {item.Title}"
            + (rowMeta is null ? string.Empty : $", {rowMeta.Replace(" · ", ", ", StringComparison.Ordinal)}")
            + (hasLink ? ", has video meeting" : string.Empty));
        AutomationProperties.SetHelpText(row, "Select to show event details");

        var content = new Grid { ColumnSpacing = 6 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });

        // Neutral 4 px dot: no accent, no thick bar (finding 2).
        var dot = new Border
        {
            Width = 4,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 7, 0, 0),
            Background = ThemeBrush("TextFillColorSecondaryBrush"),
        };
        Grid.SetColumn(dot, 0);
        content.Children.Add(dot);

        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 13,
            MaxLines = 2,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (rowMeta is not null)
        {
            // Multi-day rows prefix the location with "Day N of M" (spec 0004 AC-3).
            text.Children.Add(new TextBlock
            {
                Text = rowMeta,
                FontSize = 12,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                MaxLines = 1,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        Grid.SetColumn(text, 1);
        content.Children.Add(text);

        var time = new TextBlock
        {
            Text = TimeText(item),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
        };
        Grid.SetColumn(time, 2);
        content.Children.Add(time);

        if (hasLink)
        {
            // Non-interactive video glyph, neutral: accent is reserved for selection/today.
            var glyph = new FontIcon
            {
                Glyph = "\uE714",
                FontSize = 14,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
            };
            AutomationProperties.SetName(glyph, "Video meeting");
            Grid.SetColumn(glyph, 3);
            content.Children.Add(glyph);
        }

        row.Content = content;
        row.Click += (_, _) => ToggleDetails(item, row);
        return row;
    }

    private static string TimeText(SpikeEvent item) =>
        item.IsAllDay ? "All day" : $"{item.Start:HH:mm} \u2013 {item.End:HH:mm}";

    /// <summary>
    /// Agenda secondary line: a multi-day all-day row prefixes its location with `Day N of M`
    /// (spec 0004 AC-3); ordinary rows show the location when present.
    /// </summary>
    private static string? RowMeta(SpikeEvent item)
    {
        var parts = new List<string>();
        if (item.DayCount > 1)
        {
            parts.Add($"Day {item.DayIndex} of {item.DayCount}");
        }

        if (!string.IsNullOrWhiteSpace(item.Location))
        {
            parts.Add(item.Location);
        }

        return parts.Count == 0 ? null : string.Join(" \u00b7 ", parts);
    }

    /// <summary>
    /// Full details when-line relative to the selected day (spec 0004 AC-3): timed range, plain
    /// `All day`, or `All day · Day 2 of 3 · Nov 7–Nov 9` for a multi-day all-day row.
    /// </summary>
    private static string WhenText(SpikeEvent item, DateOnly selected)
    {
        if (!item.IsAllDay)
        {
            return $"{item.Start:HH:mm} \u2013 {item.End:HH:mm}";
        }

        if (item.DayCount <= 1)
        {
            return "All day";
        }

        var start = selected.AddDays(-(item.DayIndex - 1));
        var end = start.AddDays(item.DayCount - 1);
        var range = start.Month == end.Month && start.Year == end.Year
            ? $"{start.Day}\u2013{end.ToString("d MMM", CultureInfo.CurrentCulture)}"
            : start.ToString("d MMM", CultureInfo.CurrentCulture)
              + " \u2013 "
              + end.ToString("d MMM", CultureInfo.CurrentCulture);
        return $"All day \u00b7 Day {item.DayIndex} of {item.DayCount} \u00b7 {range}";
    }

    private void ToggleDetails(SpikeEvent item, Button row)
    {
        if (ReferenceEquals(_selectedEvent, item))
        {
            CollapseDetails();
            return;
        }

        ClearDetails();
        _selectedEvent = item;
        _selectedRow = row;
        ApplyRowSelection(row, true);

        var card = BuildDetailsCard(item);
        _detailsCard = card;
        var index = AgendaPanel.Children.IndexOf(row);
        AgendaPanel.Children.Insert(index + 1, card);
        DiagnosticLog.Write(
            $"DETAILS open index={index} hasLink={MeetingLinks.IsUsable(item.MeetingLink)} hasLocation={!string.IsNullOrWhiteSpace(item.Location)} actions='{ActionsText(item)}'");

        ResizeToContent();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_detailsCard is not null)
            {
                _detailsCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
            }
        });
    }

    private static string ActionsText(SpikeEvent item) =>
        MeetingLinks.IsUsable(item.MeetingLink)
            ? "Join meeting | Copy link | Close details"
            : "Close details";

    private Border BuildDetailsCard(SpikeEvent item)
    {
        var stack = new StackPanel { Spacing = 0 };

        // Visible details heading is the full event title (finding 5).
        stack.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        stack.Children.Add(new TextBlock
        {
            Text = WhenText(item, _selected),
            FontSize = 12,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            Margin = new Thickness(0, 4, 0, 0),
        });

        if (!string.IsNullOrWhiteSpace(item.Location))
        {
            stack.Children.Add(new TextBlock
            {
                Text = item.Location,
                FontSize = 12,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        if (!string.IsNullOrWhiteSpace(item.Notes))
        {
            stack.Children.Add(new TextBlock
            {
                Text = item.Notes,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 4,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }

        var status = new TextBlock
        {
            FontSize = 12,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        _statusText = status;
        stack.Children.Add(status);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 10, 0, 0),
        };

        if (MeetingLinks.TryGetUsableUri(item.MeetingLink, out var meetingUri))
        {
            var target = meetingUri.AbsoluteUri;
            var joinContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            joinContent.Children.Add(new FontIcon
            {
                Glyph = "\uE714",
                FontSize = 14,
                Foreground = ThemeBrush("TextOnAccentFillColorPrimaryBrush"),
            });
            joinContent.Children.Add(new TextBlock
            {
                Text = "Join meeting",
                FontSize = 12,
                Foreground = ThemeBrush("TextOnAccentFillColorPrimaryBrush"),
            });

            var join = new Button
            {
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                MinHeight = 32,
                MinWidth = 0,
                Height = 32,
                Padding = new Thickness(8, 0, 10, 0),
                Content = joinContent,
                UseSystemFocusVisuals = true,
            };
            AutomationProperties.SetName(join, "Join meeting");
            AutomationProperties.SetAutomationId(join, "JoinMeetingButton");
            join.Click += async (_, _) =>
            {
                try
                {
                    // AbsoluteUri, not the raw provider string: only http/https reached this
                    // point and the canonical URI is what the shell should launch.
                    var launched = await Launcher.LaunchUriAsync(new Uri(target));
                    DiagnosticLog.Write($"JOIN result={(launched ? "ok" : "fail")}");
                    if (!launched)
                    {
                        SetCopyStatus("Could not open the meeting link.", isError: true);
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Write("JOIN result=fail kind=" + ex.GetType().Name);
                    SetCopyStatus("Could not open the meeting link.", isError: true);
                }
            };
            _joinButton = join;
            actions.Children.Add(join);

            var copy = new Button
            {
                MinHeight = 32,
                MinWidth = 0,
                Height = 32,
                Padding = new Thickness(8, 0, 8, 0),
                Content = "Copy link",
                UseSystemFocusVisuals = true,
            };
            AutomationProperties.SetName(copy, "Copy link");
            AutomationProperties.SetAutomationId(copy, "CopyLinkButton");
            copy.Click += (_, _) => CopySelectedLink();
            _copyButton = copy;
            actions.Children.Add(copy);
        }

        var close = new Button
        {
            MinHeight = 32,
            MinWidth = 0,
            Height = 32,
            Padding = new Thickness(8, 0, 8, 0),
            Content = "Close details",
            UseSystemFocusVisuals = true,
        };
        AutomationProperties.SetName(close, "Close details");
        AutomationProperties.SetAutomationId(close, "CloseDetailsButton");
        close.Click += (_, _) => CollapseDetails();
        actions.Children.Add(close);

        stack.Children.Add(actions);
        _actionsPanel = actions;

        var card = new Border
        {
            Padding = new Thickness(10),
            Background = ThemeBrush("CardBackgroundFillColorSecondaryBrush"),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = stack,
        };
        AutomationProperties.SetName(card, $"Details for {item.Title}");
        return card;
    }

    private void ApplyRowSelection(Button row, bool selected)
    {
        row.Background = selected ? ThemeBrush("SubtleFillColorSecondaryBrush") : _transparent;
        row.BorderBrush = selected ? ThemeBrush("ControlStrokeColorDefaultBrush") : _transparent;
        row.BorderThickness = new Thickness(selected ? 1 : 0);
    }

    /// <summary>Removes the expanded details card and any row emphasis without resizing.</summary>
    private void ClearDetails()
    {
        if (_detailsCard is not null)
        {
            AgendaPanel.Children.Remove(_detailsCard);
            _detailsCard = null;
            DiagnosticLog.Write($"DETAILS close hadEvent={_selectedEvent is not null}");
        }

        if (_selectedRow is not null)
        {
            ApplyRowSelection(_selectedRow, false);
        }

        _selectedRow = null;
        _selectedEvent = null;
        _statusText = null;
        _joinButton = null;
        _copyButton = null;
        _actionsPanel = null;
    }

    private void CollapseDetails()
    {
        if (_detailsCard is null)
        {
            return;
        }

        ClearDetails();
        ResizeToContent();
        // The ScrollViewer can report a slightly stale desired height on the same pass as the
        // removal; re-measure once the dispatcher has processed the layout.
        DispatcherQueue.TryEnqueue(ResizeToContent);
    }

    private void CopySelectedLink()
    {
        if (_selectedEvent is null || !MeetingLinks.TryGetUsableUri(_selectedEvent.MeetingLink, out var uri))
        {
            return;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(uri.AbsoluteUri);
            Clipboard.SetContent(package);
            // Flush publishes the data to the clipboard service now, so it survives this app
            // exiting or the DataPackage being collected.
            Clipboard.Flush();
            DiagnosticLog.Write("COPY result=ok");
            SetCopyStatus("Meeting link copied.", isError: false);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("COPY result=fail kind=" + ex.GetType().Name);
            SetCopyStatus("Could not copy the meeting link.", isError: true);
        }
    }

    /// <summary>
    /// Capture hook: invoke Copy link through its UIA Invoke pattern, so the screenshot proves
    /// the actual button works rather than a helper called behind it.
    /// </summary>
    private void InvokeCopyButton()
    {
        if (_copyButton is null)
        {
            DiagnosticLog.Write("COPY invoke skipped: no Copy link button");
            return;
        }

        var peer = new ButtonAutomationPeer(_copyButton);
        if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
        {
            DiagnosticLog.Write("COPY invoke pattern=UIA");
            invoke.Invoke();
        }
        else
        {
            DiagnosticLog.Write("COPY invoke pattern=fallback");
            CopySelectedLink();
        }
    }

    private void SetCopyStatus(string text, bool isError)
    {
        if (_statusText is null)
        {
            return;
        }

        _statusText.Text = text;
        _statusText.Foreground = isError
            ? ThemeBrush("SystemFillColorCriticalBrush")
            : ThemeBrush("TextFillColorSecondaryBrush");
        _statusText.Visibility = Visibility.Visible;
        ResizeToContent();
    }

    private void SetUpKeyboard()
    {
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, args) =>
        {
            args.Handled = true;
            if (_detailsCard is not null)
            {
                CollapseDetails();
            }
            else if (!_tray.IsIconVisible && !_tray.HotkeyRegistered)
            {
                // Hiding would leave no way back to an app with neither tray icon nor hotkey.
                ExitApplication();
            }
            else
            {
                HideFlyout();
            }
        };
        RootHost.KeyboardAccelerators.Add(escape);

        // A known visible quit route, independent of the tray icon. It works whenever the
        // flyout has keyboard focus, including the tray-unavailable fallback.
        var quit = new KeyboardAccelerator
        {
            Key = VirtualKey.Q,
            Modifiers = VirtualKeyModifiers.Control,
        };
        quit.Invoked += (_, args) =>
        {
            args.Handled = true;
            ExitApplication();
        };
        RootHost.KeyboardAccelerators.Add(quit);
    }

    private void ApplyCaptureState()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var events = _events.TryGetValue(today, out var list) ? list : new List<SpikeEvent>();
        var meeting = events.FirstOrDefault(e => MeetingLinks.IsUsable(e.MeetingLink));

        switch (_captureState)
        {
            case "details-meeting":
            case "details-copied":
            case "focus-details":
            case "details-inline-end":
                SelectDay(today);
                if (meeting is not null)
                {
                    OpenDetailsFor(meeting);
                }

                if (_captureState == "details-copied")
                {
                    InvokeCopyButton();
                }
                else if (_captureState == "focus-details")
                {
                    _focusOnActivate = () => _joinButton?.Focus(FocusState.Keyboard) ?? false;
                }
                break;

            case "details-nolink":
                SelectDay(today);
                // Spec 0004 event 3: ordinary no-link row with a location.
                if (events.Count > 2)
                {
                    OpenDetailsFor(events[2]);
                }
                break;

            case "details-long":
                SelectDay(today);
                if (events.Count > 0)
                {
                    OpenDetailsFor(events[0]);
                }
                break;

            case "details-switch":
                // Open the meeting first, then switch to the multi-day all-day row: exactly one
                // details card may remain, and the final image must differ from details-meeting.
                SelectDay(today);
                if (meeting is not null)
                {
                    OpenDetailsFor(meeting);
                }

                if (events.Count > 0)
                {
                    DiagnosticLog.Write($"SWITCH fromIndex={(meeting is null ? -1 : events.IndexOf(meeting))} toIndex=0 count={events.Count}");
                    OpenDetailsFor(events[0]);
                }
                break;

            case "selected-not-today":
                SelectDay(today.AddDays(4));
                break;

            case "empty-day":
                SelectDay(today.AddDays(11));
                break;

            case "focus-row":
                SelectDay(today);
                _focusOnActivate = () =>
                {
                    if (_rows.Count > 2)
                    {
                        return _rows[2].Button.Focus(FocusState.Keyboard);
                    }

                    return false;
                };
                break;
        }

        if (_scrollMode is "end" or "actions")
        {
            QueueScroll(_scrollMode);
        }
    }

    private void OpenDetailsFor(SpikeEvent item)
    {
        var match = _rows.FirstOrDefault(r => ReferenceEquals(r.Event, item));
        if (match.Button is not null)
        {
            ToggleDetails(match.Event, match.Button);
        }
    }

    private void TryApplyFocus()
    {
        if (_focusOnActivate is null)
        {
            return;
        }

        // Focus can only be proven while this window is active; attempts before that are
        // expected to fail and are retried from the Activated handler.
        var ok = _focusOnActivate();
        LogFocus("attempt", ok);
        if (ok && _windowActive)
        {
            QueueFocusVerify();
            return;
        }

        QueueFocusRetry();
    }

    private void QueueFocusRetry()
    {
        if (_focusAttempts >= 20)
        {
            LogFocus("gave-up", false);
            return;
        }

        _focusAttempts++;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(150);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            TryApplyFocus();
        };
        timer.Start();
    }

    private void QueueFocusVerify()
    {
        if (_focusVerifyQueued)
        {
            return;
        }

        _focusVerifyQueued = true;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            LogFocus("verify", true);
        };
        timer.Start();
    }

    private void QueueLayoutLog()
    {
        if (!_logLayout)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(LogLayout);
    }

    private void LogFocus(string kind, bool ok)
    {
        if (!_logLayout)
        {
            return;
        }

        var root = RootGrid.XamlRoot;
        var element = root is null ? null : FocusManager.GetFocusedElement(root);
        // Never log AutomationProperties.Name here: agenda row names contain the event title.
        var automationId = element is DependencyObject dependencyObject
            ? AutomationProperties.GetAutomationId(dependencyObject)
            : string.Empty;
        var hasName = element is DependencyObject namedElement
                      && !string.IsNullOrEmpty(AutomationProperties.GetName(namedElement));
        DiagnosticLog.Write(
            $"FOCUS {kind} ok={ok} active={_windowActive} element={element?.GetType().Name ?? "null"} id='{automationId}' hasName={hasName}");
    }

    /// <summary>
    /// Screenshot hook for AC-1b/AC-1c scroll evidence. `end` scrolls the agenda to its maximum
    /// offset; `actions` aligns the complete action row to the bottom of the viewport. The
    /// resulting offset and action-row bounds are logged, so the PNG is checkable.
    /// </summary>
    private void QueueScroll(string mode)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(500);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ApplyScroll(mode);
        };
        timer.Start();
    }

    private void ApplyScroll(string mode)
    {
        try
        {
            RootHost.UpdateLayout();
            if (mode == "end")
            {
                var target = AgendaScroll.ScrollableHeight;
                var changed = AgendaScroll.ChangeView(null, target, null, disableAnimation: true);
                DiagnosticLog.Write($"SCROLL request=end target={target:0.#} changed={changed}");
            }
            else if (mode == "actions" && _actionsPanel is not null)
            {
                _actionsPanel.StartBringIntoView(
                    new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 1.0 });
                DiagnosticLog.Write("SCROLL request=actions");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("SCROLL failed kind=" + ex.GetType().Name);
        }

        var settle = DispatcherQueue.CreateTimer();
        settle.Interval = TimeSpan.FromMilliseconds(350);
        settle.IsRepeating = false;
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            LogScroll(mode);
            QueueLayoutLog();
        };
        settle.Start();
    }

    private void LogScroll(string mode)
    {
        if (!_logLayout)
        {
            return;
        }

        try
        {
            RootHost.UpdateLayout();
            var actions = ActionsBounds();
            DiagnosticLog.Write(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"SCROLL mode={mode} offset={AgendaScroll.VerticalOffset:0.#} scrollable={AgendaScroll.ScrollableHeight:0.#} viewport={AgendaScroll.ViewportHeight:0.#} extent={AgendaScroll.ExtentHeight:0.#} actionsTop={actions.Top:0.#} actionsBottom={actions.Bottom:0.#} actionsFullyVisible={(actions.Top >= -0.5 && actions.Bottom <= AgendaScroll.ViewportHeight + 0.5 ? "yes" : "no")}"));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("SCROLL log failed kind=" + ex.GetType().Name);
        }
    }

    private (double Top, double Bottom) ActionsBounds()
    {
        if (_actionsPanel is null || _detailsCard is null)
        {
            return (double.NaN, double.NaN);
        }

        var origin = _actionsPanel.TransformToVisual(AgendaScroll).TransformPoint(new Point(0, 0));
        return (origin.Y, origin.Y + _actionsPanel.ActualHeight);
    }

    private void LogLayout()
    {
        if (!_readyToLog)
        {
            return;
        }

        try
        {
            RootHost.UpdateLayout();

            // AC-1a basis: top of the month header (row) to the bottom of the last week row.
            var headerTop = MonthHeader.TransformToVisual(RootGrid).TransformPoint(new Point(0, 0)).Y;
            var gridTop = DayGrid.TransformToVisual(RootGrid).TransformPoint(new Point(0, 0)).Y;
            var monthBottom = gridTop + DayGrid.ActualHeight;
            var monthHeight = monthBottom - headerTop;

            // Composition height in layout DIP. At 100% this equals the client height; at the
            // simulated 150% capture the 360 DIP composition is scaled by uiScale.
            var layoutHeight = RootGrid.ActualHeight;
            var layoutWidth = RootGrid.ActualWidth;
            var size = AppWindow.Size;
            var hwnd = WindowNative.GetWindowHandle(this);
            var dpiScale = GetDpiForWindow(hwnd) / 96.0;
            _ = GetClientRect(hwnd, out var clientRect);
            var clientWidthDip = (clientRect.Right - clientRect.Left) / dpiScale;
            var clientHeightDip = (clientRect.Bottom - clientRect.Top) / dpiScale;
            // The XAML layout root can round one DIP below GetClientRect. The spec measures the
            // client, so use the physical client converted to the composition's DIP space.
            var equivalentClientHeight = clientHeightDip / _uiScale;
            var equivalentClientWidth = clientWidthDip / _uiScale;
            var share = equivalentClientHeight > 0 ? monthHeight / equivalentClientHeight * 100 : 0;

            var monthCap = monthHeight <= MonthBlockBudgetDip + 0.5;
            var shareCap = share <= MonthBlockBudgetShare * 100 + 0.05;
            var heightContract = equivalentClientHeight >= MinHeightDip - 0.5
                                 && equivalentClientHeight <= MaxHeightDip + 0.5;
            var budget = monthCap && shareCap && heightContract;
            var rows = string.Join(
                ",",
                _rows.Select(row => row.Button.ActualHeight.ToString("0.#", CultureInfo.InvariantCulture)));
            var actions = ActionsBounds();

            DiagnosticLog.Write(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"LAYOUT state={_captureState} fixture={_fixture} layoutDip={layoutWidth:0.#}x{layoutHeight:0.#} clientDip={clientWidthDip:0.#}x{clientHeightDip:0.#} equivClientDip={equivalentClientWidth:0.#}x{equivalentClientHeight:0.#} windowPx={size.Width}x{size.Height} dpi={dpiScale:0.##} uiScale={_uiScale:0.##} month={monthHeight:0.#} headerTop={headerTop:0.#} monthBottom={monthBottom:0.#} share={share:0.0}% monthCap224={(monthCap ? "PASS" : "FAIL")} share50={(shareCap ? "PASS" : "FAIL")} height448to600={(heightContract ? "PASS" : "FAIL")} budget={(budget ? "PASS" : "FAIL")} rows='{rows}'"));
            DiagnosticLog.Write(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"AGENDA panel={AgendaPanel.ActualHeight:0.#} viewport={AgendaScroll.ViewportHeight:0.#} extent={AgendaScroll.ExtentHeight:0.#} scrollable={AgendaScroll.ScrollableHeight:0.#} offset={AgendaScroll.VerticalOffset:0.#} actionsTop={actions.Top:0.#} actionsBottom={actions.Bottom:0.#} actionsFullyVisible={(actions.Top >= -0.5 && actions.Bottom <= AgendaScroll.ViewportHeight + 0.5 ? "yes" : "no")}"));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("LAYOUT failed kind=" + ex.GetType().Name);
        }
    }

    private static Brush ThemeBrush(string key) => (Brush)Application.Current.Resources[key];

    private enum DwmWindowAttribute
    {
        WindowCornerPreference = 33,
    }

    private enum DwmWindowCornerPreference
    {
        Round = 2,
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, DwmWindowAttribute attribute, ref DwmWindowCornerPreference value, int size);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

    private sealed record SpikeEvent(
        TimeOnly Start,
        TimeOnly End,
        string Title,
        string? Location,
        string? MeetingLink,
        string? Notes,
        bool IsAllDay = false,
        int DayIndex = 0,
        int DayCount = 0);
}
