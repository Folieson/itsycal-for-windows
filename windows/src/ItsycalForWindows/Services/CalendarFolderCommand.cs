using System;
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
        string path;
#pragma warning disable CA1031 // A failure must become a toast, not an exception across the COM boundary.
        try
        {
            path = CalendarFolder.DefaultPath;
        }
        catch (Exception)
        {
            // Resolving the profile path is normally a pure environment read; if it fails there is
            // no path to create or open, and the user still gets an answer.
            return CommandResult.ShowToast(CalendarOnboarding.UnavailableFolderToast);
        }
#pragma warning restore CA1031

        if (!CalendarFolder.EnsureCreated(path))
        {
            return CommandResult.ShowToast(CalendarOnboarding.CreateFailedToast(path));
        }

        return CalendarFolder.TryOpenInExplorer(path)
            ? CommandResult.ShowToast(CalendarOnboarding.OpenedToast(path))
            : CommandResult.ShowToast(CalendarOnboarding.OpenFailedToast(path));
    }
}
