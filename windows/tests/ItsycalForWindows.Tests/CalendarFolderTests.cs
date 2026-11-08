using System;
using System.IO;
using ItsycalForWindows.Services;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for the calendar-folder onboarding helpers: the folder the palette command creates is
/// exactly the folder <see cref="IcsEventSource"/> reads, and the discovery strings stay short
/// enough for the card's single-line empty state. The command itself talks to CmdPal and the
/// shell, so only its pure inputs are covered here; the toast mapping is checked in review.
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
    public void GetPath_RejectsAnEmptyRoot()
    {
        Assert.Throws<ArgumentException>(() => CalendarFolder.GetPath(" "));
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
    public void EmptyState_MentionsTheFolderAndTheCommandOnOneLine()
    {
        var hint = CalendarOnboarding.EmptyStateHint;

        Assert.Contains(CalendarOnboarding.CommandTitle, hint, StringComparison.Ordinal);
        Assert.Contains(IcsEventSource.FolderName, hint, StringComparison.Ordinal);
        Assert.Contains("LOCALAPPDATA", hint, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", hint);
        Assert.DoesNotContain("\r", hint);
        Assert.True(hint.Length <= 160, $"The empty state is {hint.Length} characters; keep it one short line.");
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
