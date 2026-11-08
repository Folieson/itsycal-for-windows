using System;
using System.Diagnostics;
using System.IO;

namespace ItsycalForWindows.Services;

/// <summary>
/// The local calendar folder: the one folder <see cref="IcsEventSource"/> reads and the one the
/// "open calendar folder" command creates and opens. Keeping the path here gives the command, the
/// empty state and the provider a single source of truth instead of a second copy of the path.
/// </summary>
public static class CalendarFolder
{
    /// <summary>Folder under <c>%LOCALAPPDATA%</c> that holds the extension's local data.</summary>
    public const string AppFolderName = "ItsycalForWindows";

    /// <summary>
    /// The default folder, <c>%LOCALAPPDATA%\ItsycalForWindows\calendars</c>; identical to the
    /// folder <see cref="IcsEventSource"/> reads.
    /// </summary>
    public static string DefaultPath =>
        GetPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>
    /// Builds the folder path below <paramref name="localAppData"/>. Pure, so tests can resolve it
    /// against a temporary directory instead of the real profile.
    /// </summary>
    public static string GetPath(string localAppData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppData);

        return Path.Combine(localAppData, AppFolderName, IcsEventSource.FolderName);
    }

    /// <summary>
    /// Creates the folder when it is missing. Returns <see langword="true"/> when the folder exists
    /// afterwards; an I/O failure is reported as <see langword="false"/> so the caller can show a
    /// toast instead of letting the exception cross the extension's COM boundary.
    /// </summary>
    public static bool EnsureCreated(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

#pragma warning disable CA1031 // A folder that cannot be created must become a toast, not a crash.
        try
        {
            Directory.CreateDirectory(path);
            return Directory.Exists(path);
        }
        catch (Exception)
        {
            return false;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Opens <paramref name="path"/> in File Explorer. Returns <see langword="false"/> when the
    /// shell refuses to start, again so the command can report a toast.
    /// </summary>
    public static bool TryOpenInExplorer(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

#pragma warning disable CA1031 // The shell is an external boundary; report a failure, do not throw.
        try
        {
            // Quote the path: %LOCALAPPDATA% can contain spaces (a user name with a space).
            using var process = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
            {
                UseShellExecute = true,
            });

            return process is not null;
        }
        catch (Exception)
        {
            return false;
        }
#pragma warning restore CA1031
    }
}
