using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace ItsycalForWindows.Flyout;

public sealed partial class MainWindow : Window
{
    private const int BaseWidthDip = 360;
    private const int BaseHeightDip = 600;

    private readonly TrayIcon _tray;
    private readonly Dictionary<DateOnly, List<SpikeEvent>> _events = new();
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateOnly _selected = DateOnly.FromDateTime(DateTime.Today);
    private SpikeEvent? _selectedEvent;
    private bool _exiting;
    private bool _hidden;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Itsycal";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new DesktopAcrylicBackdrop();

        ConfigureWindow();
        SeedFakeEvents();
        BuildWeekdayHeader();
        BuildCalendar();
        ShowSelectedDay(_selected);

        PreviousMonthButton.Click += (_, _) => ChangeMonth(-1);
        NextMonthButton.Click += (_, _) => ChangeMonth(1);
        TodayButton.Click += (_, _) => GoToToday();
        JoinButton.Click += async (_, _) =>
        {
            if (_selectedEvent?.MeetingLink is { Length: > 0 } link && Uri.TryCreate(link, UriKind.Absolute, out var uri))
            {
                await Launcher.LaunchUriAsync(uri);
            }
        };
        CopyButton.Click += (_, _) => CopySelectedLink();
        CloseDetailsButton.Click += (_, _) => CollapseDetails();

        _tray = new TrayIcon(ToggleOverlay, ExitApplication);
        // The shell sometimes rejects the icon while the XAML window is still being created;
        // retry once the dispatcher loop is pumping (after OnLaunched returns).
        DispatcherQueue.TryEnqueue(() => _tray.RetryAddIcon());

        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && !_exiting)
            {
                _hidden = true;
                AppWindow.Hide();
            }
            else
            {
                _hidden = false;
            }
        };
        AppWindow.Closing += (_, args) =>
        {
            if (!_exiting)
            {
                args.Cancel = true;
                _hidden = true;
                AppWindow.Hide();
            }
        };
        Closed += (_, _) => _tray.Dispose();
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

        var hwnd = WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi / 96.0;
        var width = (int)Math.Round(BaseWidthDip * scale);
        var height = (int)Math.Round(BaseHeightDip * scale);
        AppWindow.Resize(new SizeInt32(width, height));
        PositionNearTray(width, height, scale);

        var corner = DwmWindowCornerPreference.Round;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowAttribute.WindowCornerPreference, ref corner, sizeof(int));
    }

    private void PositionNearTray(int width, int height, double scale)
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var work = area.WorkArea;
        var margin = (int)Math.Round(12 * scale);
        var x = work.X + work.Width - width - margin;
        var y = work.Y + work.Height - height - margin;
        AppWindow.Move(new PointInt32(x, y));
    }

    private void ToggleOverlay()
    {
        if (_hidden || !AppWindow.IsVisible)
        {
            PositionNearTray(
                (int)Math.Round(BaseWidthDip * (GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0)),
                (int)Math.Round(BaseHeightDip * (GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0)),
                GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0);
            AppWindow.Show();
            Activate();
            _hidden = false;
        }
        else
        {
            _hidden = true;
            AppWindow.Hide();
        }
    }

    private void ExitApplication()
    {
        _exiting = true;
        _tray.Dispose();
        Close();
    }

    private void SeedFakeEvents()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        _events[today] =
        [
            new SpikeEvent(new TimeOnly(9, 30), new TimeOnly(10, 0), "Design review", "Teams", "https://teams.microsoft.com/l/meetup-join/demo",
                "Review the compact flyout: calendar as quick navigation, agenda below, details and Join for the selected meeting."),
            new SpikeEvent(new TimeOnly(11, 30), new TimeOnly(12, 15), "Sprint planning with the platform team", "Room A", null,
                "Plan the shared core extraction and the standalone surface spike."),
            new SpikeEvent(new TimeOnly(15, 0), new TimeOnly(15, 30), "Focus time", null, null, null),
        ];
        _events[today.AddDays(9)] = [new SpikeEvent(new TimeOnly(10, 0), new TimeOnly(11, 0), "1:1 with PO", null, null, null)];
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
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
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

        for (var r = 0; r < 6; r++)
        {
            DayGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        }

        var first = _month;
        var offset = ((int)first.DayOfWeek + 6) % 7; // Monday-first
        var start = first.AddDays(-offset);
        var today = DateOnly.FromDateTime(DateTime.Today);

        for (var i = 0; i < 42; i++)
        {
            var date = DateOnly.FromDateTime(start.AddDays(i));
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                FontSize = 11,
                Content = BuildDayContent(date, today),
            };
            AutomationProperties.SetName(button, date.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture));
            button.Click += (_, _) =>
            {
                _selected = date;
                if (date.Month != _month.Month || date.Year != _month.Year)
                {
                    _month = new DateTime(date.Year, date.Month, 1);
                    BuildCalendar();
                }
                ShowSelectedDay(date);
            };
            Grid.SetRow(button, i / 7);
            Grid.SetColumn(button, i % 7);
            DayGrid.Children.Add(button);
        }
    }

    private UIElement BuildDayContent(DateOnly date, DateOnly today)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 1 };
        var number = new TextBlock
        {
            Text = date.Day.ToString(CultureInfo.CurrentCulture),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = date == _selected || date == today ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = date == _selected
                ? (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
                : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
        };
        panel.Children.Add(number);
        if (date != _selected && date == today)
        {
            panel.Children.Add(new Border
            {
                Height = 2,
                Width = 10,
                CornerRadius = new CornerRadius(1),
                Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            });
        }
        else if (_events.ContainsKey(date))
        {
            panel.Children.Add(new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
        }

        return panel;
    }

    private void ChangeMonth(int delta)
    {
        _month = _month.AddMonths(delta);
        BuildCalendar();
    }

    private void GoToToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        _month = new DateTime(today.Year, today.Month, 1);
        _selected = today;
        BuildCalendar();
        ShowSelectedDay(today);
    }

    private void ShowSelectedDay(DateOnly date)
    {
        _selected = date;
        AgendaHeader.Text = date == DateOnly.FromDateTime(DateTime.Today)
            ? "Today"
            : date.ToString("dddd", CultureInfo.CurrentCulture);
        AgendaDate.Text = date.ToString("MMM d", CultureInfo.CurrentCulture);
        AgendaPanel.Children.Clear();
        CollapseDetails();

        var events = _events.TryGetValue(date, out var list) ? list : new List<SpikeEvent>();
        if (events.Count == 0)
        {
            AgendaPanel.Children.Add(new TextBlock
            {
                Text = "No events on this day.",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(2, 6, 0, 0),
            });
            return;
        }

        foreach (var item in events)
        {
            AgendaPanel.Children.Add(BuildAgendaRow(item));
        }
    }

    private UIElement BuildAgendaRow(SpikeEvent item)
    {
        var row = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(4, 4, 4, 4),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
        };
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        var dot = new Border
        {
            Width = 4,
            Height = 22,
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            Background = item.MeetingLink is null
                ? (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                : (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
        };
        Grid.SetColumn(dot, 0);
        content.Children.Add(dot);
        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock { Text = item.Title, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2 });
        if (!string.IsNullOrWhiteSpace(item.Location))
        {
            text.Children.Add(new TextBlock
            {
                Text = item.Location,
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        var time = new TextBlock
        {
            Text = $"{item.Start:HH:mm}\n{item.End:HH:mm}",
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        Grid.SetColumn(time, 2);
        content.Children.Add(time);
        if (item.MeetingLink is not null)
        {
            var icon = new FontIcon
            {
                Glyph = "\uE714",
                FontSize = 13,
                Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(icon, 3);
            content.Children.Add(icon);
        }

        row.Content = content;
        row.Click += (_, _) => ShowDetails(item);
        return row;
    }

    private void ShowDetails(SpikeEvent item)
    {
        _selectedEvent = item;
        DetailsPanel.Visibility = Visibility.Visible;
        DetailsWhen.Text = $"{item.Start:HH:mm} – {item.End:HH:mm}";
        DetailsLocation.Text = item.Location ?? string.Empty;
        DetailsLocation.Visibility = string.IsNullOrWhiteSpace(item.Location) ? Visibility.Collapsed : Visibility.Visible;
        DetailsNotes.Text = item.Notes ?? string.Empty;
        DetailsNotes.Visibility = string.IsNullOrWhiteSpace(item.Notes) ? Visibility.Collapsed : Visibility.Visible;
        JoinButton.Visibility = item.MeetingLink is null ? Visibility.Collapsed : Visibility.Visible;
        CopyButton.Visibility = item.MeetingLink is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CollapseDetails()
    {
        _selectedEvent = null;
        DetailsPanel.Visibility = Visibility.Collapsed;
    }

    private void CopySelectedLink()
    {
        if (_selectedEvent?.MeetingLink is not { Length: > 0 } link)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(link);
        Clipboard.SetContent(package);
    }

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

    private sealed record SpikeEvent(
        TimeOnly Start,
        TimeOnly End,
        string Title,
        string? Location,
        string? MeetingLink,
        string? Notes);
}
