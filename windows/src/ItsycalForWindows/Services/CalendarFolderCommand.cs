using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ItsycalForWindows.Services;

/// <summary>
/// Palette command that makes the local calendar folder discoverable on first run: it creates
/// <c>%LOCALAPPDATA%\ItsycalForWindows\calendars</c> when it is missing and opens it in File
/// Explorer, so the user can drop an <c>.ics</c> export into the folder the ICS provider reads.
/// Every outcome is a toast; the command logs nothing and never touches event content.
/// </summary>
internal sealed class CalendarFolderCommand : InvokableCommand
{
    /// <inheritdoc />
    public override string Name => CalendarOnboarding.CommandTitle;

    /// <inheritdoc />
    public override CommandResult Invoke()
    {
        if (!CalendarFolder.TryGetDefaultPath(out var path))
        {
            // A profile with no local application-data folder has no folder to create or open.
            return CommandResult.ShowToast(CalendarOnboarding.UnavailableFolderToast);
        }

        if (!CalendarFolder.EnsureCreated(path))
        {
            return CommandResult.ShowToast(CalendarOnboarding.CreateFailedToast(path));
        }

        return CalendarFolder.TryOpenInExplorer(path)
            ? CommandResult.ShowToast(CalendarOnboarding.OpenedToast(path))
            : CommandResult.ShowToast(CalendarOnboarding.OpenFailedToast(path));
    }
}
