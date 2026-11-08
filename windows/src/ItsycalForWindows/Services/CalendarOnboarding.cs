namespace ItsycalForWindows.Services;

/// <summary>
/// First-run discovery strings for the local calendar folder, kept in one place so the palette
/// command and the selected-day empty state cannot drift apart. Public so the unit tests can
/// assert the wording without instantiating Command Palette or WinUI types.
/// </summary>
public static class CalendarOnboarding
{
    /// <summary>Palette command title; also the phrase the empty state tells the user to run.</summary>
    public const string CommandTitle = "Itsycal: open calendar folder";

    /// <summary>Palette command subtitle: what invoking the command does.</summary>
    public const string CommandSubtitle = "Create the calendar folder and open it in File Explorer";

    /// <summary>
    /// The selected-day empty state: one short line that names the folder (by its documented
    /// <c>%LOCALAPPDATA%</c> location) and the palette command that opens it.
    /// </summary>
    public const string EmptyStateHint =
        "No events on this day. Add an .ics file to %LOCALAPPDATA%\\ItsycalForWindows\\calendars or run \"Itsycal: open calendar folder\".";

    /// <summary>Toast shown after the folder was created (if needed) and opened.</summary>
    /// <param name="path">Folder that was opened.</param>
    public static string OpenedToast(string path) => $"Opened {path}";

    /// <summary>Toast shown when the folder could not be created.</summary>
    /// <param name="path">Folder that could not be created.</param>
    public static string CreateFailedToast(string path) => $"Could not create the calendar folder {path}";

    /// <summary>Toast shown when File Explorer could not be started.</summary>
    /// <param name="path">Folder that could not be opened.</param>
    public static string OpenFailedToast(string path) => $"Could not open {path} in File Explorer";

    /// <summary>Toast shown when the user's local folder path itself cannot be resolved.</summary>
    public const string UnavailableFolderToast = "Itsycal: could not resolve the local calendar folder.";
}
