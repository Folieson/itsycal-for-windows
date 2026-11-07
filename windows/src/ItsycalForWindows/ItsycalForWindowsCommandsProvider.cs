// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if DEBUG
using System;
using System.Threading.Tasks;
#endif
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using ItsycalForWindows.Pages;
using ItsycalForWindows.Services;

namespace ItsycalForWindows;

/// <summary>
/// Root command provider: a top-level command that opens the calendar, and one dock band
/// whose button shows the current date and expands into the calendar flyout.
/// </summary>
/// <remarks>
/// The calendar page and the dock item are single cached instances. The host may query
/// the dock bands repeatedly; creating a new page each time would reset the selected
/// month, and creating a new <see cref="DockDateItem"/> each time would leave its refresh
/// timer running for an object the host no longer shows.
/// </remarks>
public partial class ItsycalForWindowsCommandsProvider : CommandProvider
{
    private const string ExtensionId = "com.folieson.itsycalforwindows";

    private readonly ICommandItem[] _commands;
    private readonly SettingsManager _settingsManager = new();
    private readonly IEventSource _eventSource = new AppointmentsEventSource();
    private readonly CalendarPage _calendarPage;
    private readonly DockDateItem _dockItem;

    public ItsycalForWindowsCommandsProvider()
    {
        Id = ExtensionId;
        DisplayName = "Itsycal for Windows";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        _calendarPage = new CalendarPage(_settingsManager, _eventSource);
        _dockItem = new DockDateItem(_calendarPage);

        _commands =
        [
            new CommandItem(_calendarPage)
            {
                Title = DisplayName,
                Subtitle = "Month calendar and events",
                MoreCommands = [new CommandContextItem(_settingsManager.Settings.SettingsPage)],
            },
        ];

#if DEBUG
        // DEBUG-only verification helpers. Release builds expose only the calendar command.
        _commands =
        [
            .. _commands,
            new CommandItem(new DebugDemoCommand("Itsycal: add demo events", DemoAppointments.CreateAsync))
            {
                Title = "Itsycal: add demo events",
                Subtitle = "DEBUG: create five demo appointments in the Windows appointment store",
                Icon = new IconInfo("\uE710"),
            },
            new CommandItem(new DebugDemoCommand("Itsycal: remove demo events", DemoAppointments.RemoveAsync))
            {
                Title = "Itsycal: remove demo events",
                Subtitle = "DEBUG: delete the demo calendar, its appointments and the recorded list",
                Icon = new IconInfo("\uE74D"),
            },
            new CommandItem(new DebugDemoCommand("Itsycal: probe calendar access", CalendarAccessProbe.DescribeAsync))
            {
                Title = "Itsycal: probe calendar access",
                Subtitle = "DEBUG: log which appointment-store access levels are granted",
                Icon = new IconInfo("\uE9D9"),
            },
        ];
#endif

        Settings = _settingsManager.Settings;
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    /// <summary>
    /// The Dock band: one expandable button (a content page) that opens the calendar.
    /// </summary>
    public override ICommandItem[]? GetDockBands() => [_dockItem];

    /// <summary>
    /// Releases resources owned by the provider. Called from the extension's
    /// <see cref="ItsycalForWindows.Dispose"/> because the host offers no disposal callback
    /// for command providers.
    /// </summary>
    public void DisposeResources() => _dockItem.Dispose();

#if DEBUG
    /// <summary>
    /// DEBUG-only command that runs one of the demo diagnostics and reports its short result in a
    /// toast. The detailed outcome is written to the demo-access.log by the diagnostics themselves.
    /// </summary>
    private sealed class DebugDemoCommand : InvokableCommand
    {
        private readonly string _name;
        private readonly Func<Task<string>> _action;

        public DebugDemoCommand(string name, Func<Task<string>> action)
        {
            _name = name;
            _action = action;
        }

        public override string Name => _name;

        public override CommandResult Invoke()
        {
            try
            {
                return CommandResult.ShowToast(_action().GetAwaiter().GetResult());
            }
            catch (Exception exception)
            {
                return CommandResult.ShowToast(
                    $"Demo command failed: {exception.GetType().Name} 0x{exception.HResult:X8}");
            }
        }
    }
#endif
}
