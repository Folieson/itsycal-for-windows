using System;
using System.IO;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace ItsycalForWindows;

/// <summary>
/// Self-update preferences, persisted by the Command Palette host through
/// <see cref="JsonSettingsManager"/>.
/// </summary>
/// <remarks>
/// This manager is deliberately separate from <see cref="SettingsManager"/>: calendar settings
/// and update switches are owned by different features, live in their own file, and can evolve
/// without touching each other's persisted state. The update switches are read by
/// <see cref="Services.UpdateService"/> and by the provider that exposes the update commands.
/// </remarks>
internal sealed class UpdateSettings : JsonSettingsManager
{
    private static readonly string _namespace = "ItsycalForWindows.Update";

    private readonly ToggleSetting _autoCheck = new(
        Namespaced(nameof(AutoCheck)),
        "Check for updates automatically",
        "Check GitHub Releases for a newer version at most once every 24 hours.",
        true);

    private readonly ToggleSetting _autoInstall = new(
        Namespaced(nameof(AutoInstall)),
        "Install updates automatically",
        "Install a verified update without asking, then restart Command Palette.",
        false);

    private readonly ToggleSetting _includePrereleases = new(
        Namespaced(nameof(IncludePrereleases)),
        "Include prerelease builds",
        "Also accept prerelease builds. Releases are currently published as prereleases, so "
            + "turning this off means almost no update is ever offered.",
        true);

    public UpdateSettings()
    {
        FilePath = UpdateSettingsJsonPath();

        Settings.Add(_autoCheck);
        Settings.Add(_autoInstall);
        Settings.Add(_includePrereleases);

        // A missing, unreadable or corrupt settings file must never prevent the extension from
        // starting; the defaults above are safe to use in that case.
        try
        {
            LoadSettings();
        }
        catch (Exception)
        {
        }

        Settings.SettingsChanged += (_, _) =>
        {
            try
            {
                SaveSettings();
            }
            catch (Exception)
            {
            }
        };
    }

    /// <summary>True when the extension should check for updates periodically.</summary>
    public bool AutoCheck => _autoCheck.Value;

    /// <summary>
    /// True when a downloaded update should be applied without asking. The provider owns that
    /// policy; the service only downloads and stages the updater.
    /// </summary>
    public bool AutoInstall => _autoInstall.Value;

    /// <summary>
    /// True when prerelease GitHub releases are acceptable. Defaults to true because this
    /// repository publishes its development builds as prereleases.
    /// </summary>
    public bool IncludePrereleases => _includePrereleases.Value;

    /// <summary>
    /// Settings page for the update switches, ready to be shown from a Command Palette
    /// command context menu.
    /// </summary>
    public IContentPage SettingsPage => Settings.SettingsPage;

    internal static string UpdateSettingsJsonPath()
    {
        // Same directory fallback chain as SettingsManager, but a separate file: the calendar
        // and update managers must never overwrite each other's persisted settings.
        foreach (var candidate in new[]
                 {
                     TryResolveSettingsDirectory(),
                     Path.Combine(Path.GetTempPath(), "ItsycalForWindows"),
                     AppContext.BaseDirectory,
                 })
        {
            if (string.IsNullOrEmpty(candidate))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(candidate);
                return Path.Combine(candidate, "update-settings.json");
            }
            catch (Exception)
            {
            }
        }

        return Path.Combine(Path.GetTempPath(), "ItsycalForWindows", "update-settings.json");
    }

    private static string Namespaced(string propertyName) => $"{_namespace}.{propertyName}";

    private static string? TryResolveSettingsDirectory()
    {
        try
        {
            return Utilities.BaseSettingsPath("ItsycalForWindows");
        }
        catch (Exception)
        {
            return null;
        }
    }
}
