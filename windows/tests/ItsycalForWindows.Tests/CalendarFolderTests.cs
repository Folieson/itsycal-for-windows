using System;
using System.IO;
using ItsycalForWindows.Services;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for the calendar-folder onboarding helpers: the folder the palette command creates is
/// exactly the folder <see cref="IcsEventSource"/> reads, the empty state keeps the two-part
/// contract from UI specs 0003/0004, and every failure path that can be made pure returns a value
/// instead of throwing. The command itself talks to CmdPal and the shell, so only its pure inputs
/// are covered here; the toast mapping is checked in review.
/// </summary>
public sealed class CalendarFolderTests
{
    [Fact]
    public void DefaultPath_IsTheFolderIcsEventSourceReads()
    {
        Assert.Equal(IcsEventSource.DefaultDirectory, CalendarFolder.DefaultPath);
    }

    [Fact]
    public void DefaultPath_IsUnderLocalAppData()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, CalendarFolder.DefaultPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(IcsEventSource.FolderName, CalendarFolder.DefaultPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetPath_ResolvesTheCalendarsFolderUnderLocalAppData()
    {
        using var folder = new TempIcsFolder();
        var expected = Path.Combine(folder.Path, CalendarFolder.AppFolderName, IcsEventSource.FolderName);

        Assert.Equal(expected, CalendarFolder.GetPath(folder.Path));
    }

    [Fact]
    public void GetPath_ReturnsAnEmptyPathForAnInvalidRoot()
    {
        Assert.Equal(string.Empty, CalendarFolder.GetPath(" "));
        Assert.Equal(string.Empty, CalendarFolder.GetPath(null));
    }

    [Fact]
    public void TryGetDefaultPath_ResolvesTheProfileFolder()
    {
        Assert.True(CalendarFolder.TryGetDefaultPath(out var path));
        Assert.Equal(CalendarFolder.DefaultPath, path);
        Assert.True(Path.IsPathFullyQualified(path), $"Expected an absolute folder path, got '{path}'.");
    }

    [Fact]
    public void EnsureCreated_CreatesMissingFolder()
    {
        using var folder = new TempIcsFolder();
        var path = folder.Combine("ItsycalForWindows", "calendars");
        Assert.False(Directory.Exists(path));

        Assert.True(CalendarFolder.EnsureCreated(path));
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void EnsureCreated_IsIdempotentForAnExistingFolder()
    {
        using var folder = new TempIcsFolder();
        var path = folder.Combine("calendars");

        Assert.True(CalendarFolder.EnsureCreated(path));
        Assert.True(CalendarFolder.EnsureCreated(path));
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void EnsureCreated_ReturnsFalseInsteadOfThrowingWhenThePathIsAFile()
    {
        using var folder = new TempIcsFolder();
        var path = folder.Write("calendars", "not a folder");

        Assert.False(CalendarFolder.EnsureCreated(path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void EnsureCreated_ReturnsFalseWhenAParentIsAFile()
    {
        using var folder = new TempIcsFolder();
        var blocker = folder.Write("blocker", "not a folder");
        var path = Path.Combine(blocker, "calendars");

        Assert.False(CalendarFolder.EnsureCreated(path));
        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void EnsureCreated_ReturnsFalseForAnInvalidPath()
    {
        Assert.False(CalendarFolder.EnsureCreated(" "));
        Assert.False(CalendarFolder.EnsureCreated(null));
    }

    [Fact]
    public void TryOpenInExplorer_ReturnsFalseForAnInvalidPath()
    {
        // No shell process may be started for an invalid path; this is the pure failure half of the
        // Explorer boundary. A real launch failure is a documented manual check in spec 0005.
        Assert.False(CalendarFolder.TryOpenInExplorer(" "));
        Assert.False(CalendarFolder.TryOpenInExplorer(null));
    }

    [Fact]
    public void CommandTitle_IsThePaletteCommandName()
    {
        Assert.Equal("Itsycal: open calendar folder", CalendarOnboarding.CommandTitle);
        Assert.StartsWith("Itsycal: ", CalendarOnboarding.CommandTitle, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandSubtitle_IsASingleLine()
    {
        Assert.False(string.IsNullOrWhiteSpace(CalendarOnboarding.CommandSubtitle));
        Assert.DoesNotContain("\n", CalendarOnboarding.CommandSubtitle);
        Assert.DoesNotContain("\r", CalendarOnboarding.CommandSubtitle);
    }

    [Fact]
    public void EmptyState_IsTheTwoPartContract()
    {
        // The exact contract from UI specs 0003 (AC-5) and 0004 (AC-7): a sentence plus a folder
        // pointer, rendered as two TextBlocks. Whether each part stays on one visual line is an
        // installed-build manual check, so this test does not claim it.
        Assert.Equal("No events on this day.", CalendarOnboarding.EmptyStateTitle);
        Assert.Equal(
            "Calendars folder: %LOCALAPPDATA%\\ItsycalForWindows\\calendars",
            CalendarOnboarding.EmptyStatePointer);

        Assert.Contains(IcsEventSource.FolderName, CalendarOnboarding.EmptyStatePointer, StringComparison.Ordinal);
        Assert.Contains("LOCALAPPDATA", CalendarOnboarding.EmptyStatePointer, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", CalendarOnboarding.EmptyStateTitle);
        Assert.DoesNotContain("\r", CalendarOnboarding.EmptyStateTitle);
        Assert.DoesNotContain("\n", CalendarOnboarding.EmptyStatePointer);
        Assert.DoesNotContain("\r", CalendarOnboarding.EmptyStatePointer);
        Assert.True(
            CalendarOnboarding.EmptyStatePointer.Length <= 160,
            $"The folder pointer is {CalendarOnboarding.EmptyStatePointer.Length} characters; keep it short.");
    }

    [Fact]
    public void EmptyState_DoesNotUseTheOldCombinedSentence()
    {
        const string oldCombined =
            "No events on this day. Add an .ics file to %LOCALAPPDATA%\\ItsycalForWindows\\calendars or run \"Itsycal: open calendar folder\".";

        Assert.NotEqual(oldCombined, CalendarOnboarding.EmptyStateTitle);
        Assert.NotEqual(oldCombined, CalendarOnboarding.EmptyStatePointer);
        Assert.DoesNotContain("Add an .ics file", CalendarOnboarding.EmptyStatePointer, StringComparison.Ordinal);
        Assert.DoesNotContain("Add an .ics file", CalendarOnboarding.EmptyStateTitle, StringComparison.Ordinal);
        Assert.NotEqual(CalendarOnboarding.EmptyStateTitle, CalendarOnboarding.EmptyStatePointer);
    }

    [Fact]
    public void Toasts_NameTheFolder()
    {
        var path = Path.Combine("C:", "Users", "user", "AppData", "Local", "ItsycalForWindows", "calendars");

        Assert.Contains(path, CalendarOnboarding.OpenedToast(path), StringComparison.Ordinal);
        Assert.Contains(path, CalendarOnboarding.CreateFailedToast(path), StringComparison.Ordinal);
        Assert.Contains(path, CalendarOnboarding.OpenFailedToast(path), StringComparison.Ordinal);
    }
}
