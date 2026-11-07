// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using ItsycalForWindows.Pages;
using ItsycalForWindows.Services;

namespace ItsycalForWindows;

/// <summary>
/// Root command provider: a top-level command that opens the calendar, update commands, and
/// one dock band whose button shows the current date and expands into the calendar flyout.
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
    private readonly UpdateSettings _updateSettings = new();
    private readonly UpdateService _updateService;
    private readonly IEventSource _eventSource =
        new CompositeEventSource(new AppointmentsEventSource(), new IcsEventSource());
    private readonly CalendarPage _calendarPage;
    private readonly DockDateItem _dockItem;

    public ItsycalForWindowsCommandsProvider()
    {
        Id = ExtensionId;
        DisplayName = "Itsycal for Windows";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");

        _updateService = new UpdateService(_updateSettings);
        _calendarPage = new CalendarPage(_settingsManager, _eventSource);
        _dockItem = new DockDateItem(_calendarPage);

        _commands =
        [
            new CommandItem(_calendarPage)
            {
                Title = DisplayName,
                Subtitle = "Month calendar and events",
                MoreCommands =
                [
                    new CommandContextItem(_settingsManager.Settings.SettingsPage),
                    new CommandContextItem(_updateSettings.SettingsPage),
                ],
            },
            new CommandItem(new UpdateActionCommand("Itsycal: check for updates", _updateService, install: false))
            {
                Title = "Itsycal: check for updates",
                Subtitle = "Ask GitHub whether a newer version exists",
                Icon = new IconInfo("\uE895"),
            },
            new CommandItem(new UpdateActionCommand("Itsycal: install update", _updateService, install: true))
            {
                Title = "Itsycal: install update",
                Subtitle = "Download the newest release, verify it and restart the palette",
                Icon = new IconInfo("\uE896"),
            },
        ];

#if DEBUG
        // DEBUG-only verification helpers. Release builds expose only the calendar and update commands.
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

        DemoAppointments.AutoSeedIfRequested();
#endif

        Settings = _settingsManager.Settings;
        StartAutoUpdateCheck();
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

    /// <summary>
    /// Background update check, gated by the update settings and the 24-hour throttle in
    /// <see cref="UpdateService.ShouldAutoCheck"/>. When auto-install is enabled the update is
    /// downloaded, hash-verified and handed to the detached updater, which stops the host,
    /// installs and restarts it. Every outcome is appended to the local update log.
    /// </summary>
    private void StartAutoUpdateCheck()
    {
        if (!_updateSettings.AutoCheck || !_updateService.ShouldAutoCheck())
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var check = await _updateService.CheckAsync().ConfigureAwait(false);
                AppendUpdateLog($"auto-check: {check.Status}: {check.Message}");
                if (!check.HasUpdate)
                {
                    return;
                }

                if (!_updateSettings.AutoInstall)
                {
                    AppendUpdateLog("auto-check: update available; auto-install is disabled.");
                    return;
                }

                var download = await UpdateService
                    .DownloadAsync(check, UpdateService.CurrentArchitecture)
                    .ConfigureAwait(false);
                AppendUpdateLog($"auto-download: success={download.Success}: {download.Message}");
                if (!download.Success || string.IsNullOrEmpty(download.MsixPath))
                {
                    return;
                }

                var apply = await UpdateService.ApplyAsync(download.MsixPath).ConfigureAwait(false);
                AppendUpdateLog($"auto-apply: success={apply.Success}: {apply.Message}");
            }
            catch (Exception exception)
            {
                AppendUpdateLog($"auto-check failed: {exception.GetType().Name} 0x{exception.HResult:X8}");
            }
        });
    }

    private static void AppendUpdateLog(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ItsycalForWindows");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "update.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics only: never break the provider because a log write failed.
        }
    }

    /// <summary>
    /// Runs the update check, optionally downloads and applies the update, and reports the
    /// outcome in a toast. The check/install APIs never throw; this wrapper only guards the
    /// synchronous bridge into the host's command callback.
    /// </summary>
    private sealed class UpdateActionCommand : InvokableCommand
    {
        private readonly string _name;
        private readonly UpdateService _updateService;
        private readonly bool _install;

        public UpdateActionCommand(string name, UpdateService updateService, bool install)
        {
            _name = name;
            _updateService = updateService;
            _install = install;
        }

        public override string Name => _name;

        public override CommandResult Invoke()
        {
            try
            {
                return CommandResult.ShowToast(RunAsync().GetAwaiter().GetResult());
            }
            catch (Exception exception)
            {
                return CommandResult.ShowToast(
                    $"Update failed: {exception.GetType().Name} 0x{exception.HResult:X8}");
            }
        }

        private async Task<string> RunAsync()
        {
            var check = await _updateService.CheckAsync().ConfigureAwait(false);
            AppendUpdateLog($"manual-check: {check.Status}: {check.Message}");
            if (!check.HasUpdate)
            {
                return check.Message;
            }

            if (!_install)
            {
                return $"{check.Message} Use \"Itsycal: install update\" to install it.";
            }

            var download = await UpdateService
                .DownloadAsync(check, UpdateService.CurrentArchitecture)
                .ConfigureAwait(false);
            AppendUpdateLog($"manual-download: success={download.Success}: {download.Message}");
            if (!download.Success || string.IsNullOrEmpty(download.MsixPath))
            {
                return download.Message;
            }

            var apply = await UpdateService.ApplyAsync(download.MsixPath).ConfigureAwait(false);
            AppendUpdateLog($"manual-apply: success={apply.Success}: {apply.Message}");
            return apply.Message;
        }
    }

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
