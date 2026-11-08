using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("ItsycalForWindows.Tests")]

namespace ItsycalForWindows.Services;

/// <summary>Architecture of a published MSIX package.</summary>
internal enum UpdateArchitecture
{
    /// <summary>The <c>win-x64</c> package.</summary>
    X64,

    /// <summary>The <c>win-arm64</c> package.</summary>
    Arm64,
}

/// <summary>Outcome of an update check.</summary>
internal enum UpdateCheckStatus
{
    /// <summary>The installed version is current.</summary>
    UpToDate,

    /// <summary>A newer release was found.</summary>
    Available,

    /// <summary>The check could not be completed; see <see cref="UpdateCheckResult.Message"/>.</summary>
    Failed,
}

/// <summary>A semantic version <c>major.minor.patch</c>, as used by release tags <c>vX.Y.Z</c>.</summary>
internal readonly record struct SemanticVersion(int Major, int Minor, int Patch) : IComparable<SemanticVersion>
{
    /// <inheritdoc />
    public int CompareTo(SemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    /// <inheritdoc />
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", Major, Minor, Patch);
}

/// <summary>One downloadable file of a GitHub release.</summary>
/// <param name="Name">Asset file name, for example <c>ItsycalForWindows_1.1.1.0_x64.msix</c>.</param>
/// <param name="DownloadUri">HTTPS download URL reported by the GitHub API.</param>
/// <param name="AssetId">
/// Numeric GitHub asset id, used for the API asset fallback when the browser download URL is
/// blocked or failing. Zero when the API response did not carry an id.
/// </param>
internal sealed record ReleaseAsset(string Name, Uri DownloadUri, long AssetId = 0);

/// <summary>The parts of a GitHub release that the updater needs.</summary>
/// <param name="TagName">Release tag, for example <c>v1.1.1</c>.</param>
/// <param name="Draft">True for unpublished drafts, which must never be offered.</param>
/// <param name="Prerelease">True when GitHub marks the release as a prerelease.</param>
/// <param name="PublishedAt">Publication timestamp, when GitHub reports one.</param>
/// <param name="ReleaseNotes">Release body as written by the release workflow.</param>
/// <param name="PageUri">Human-readable release page.</param>
/// <param name="Assets">Downloadable assets of the release.</param>
internal sealed record GitHubRelease(
    string TagName,
    bool Draft,
    bool Prerelease,
    DateTimeOffset? PublishedAt,
    string ReleaseNotes,
    Uri? PageUri,
    IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Result of an update check. Never carries a <see langword="null"/> <see cref="Message"/>.</summary>
internal sealed class UpdateCheckResult
{
    private UpdateCheckResult(UpdateCheckStatus status, string message)
    {
        Status = status;
        Message = message;
    }

    /// <summary>Outcome category of the check.</summary>
    public UpdateCheckStatus Status { get; }

    /// <summary>Short, user-presentable status message.</summary>
    public string Message { get; }

    /// <summary>Version of the running package, or of the fallback assembly version.</summary>
    public SemanticVersion? CurrentVersion { get; private init; }

    /// <summary>Newer version found by the check, when <see cref="Status"/> is <see cref="UpdateCheckStatus.Available"/>.</summary>
    public SemanticVersion? AvailableVersion { get; private init; }

    /// <summary>Tag of the newer release, for example <c>v1.1.1</c>.</summary>
    public string? Tag { get; private init; }

    /// <summary>Release notes of the newer release, as written by the release workflow.</summary>
    public string? ReleaseNotes { get; private init; }

    /// <summary>Publication time of the newer release.</summary>
    public DateTimeOffset? PublishedAt { get; private init; }

    /// <summary>Human-readable release page for the newer release.</summary>
    public Uri? PageUri { get; private init; }

    /// <summary>Downloadable assets of the newer release.</summary>
    public IReadOnlyList<ReleaseAsset> Assets { get; private init; } = [];

    /// <summary>True when <see cref="Status"/> is <see cref="UpdateCheckStatus.Available"/>.</summary>
    public bool HasUpdate => Status == UpdateCheckStatus.Available;

    /// <summary>MSIX download URL for the requested architecture, when the release has one.</summary>
    public Uri? GetMsixUri(UpdateArchitecture architecture) => UpdateService.SelectMsixAsset(Assets, architecture)?.DownloadUri;

    /// <summary>URL of the release <c>SHA256SUMS.txt</c>, when the release has one.</summary>
    public Uri? ChecksumUri => UpdateService.SelectSumsAsset(Assets)?.DownloadUri;

    internal static UpdateCheckResult UpToDate(SemanticVersion currentVersion, string message) =>
        new(UpdateCheckStatus.UpToDate, message) { CurrentVersion = currentVersion };

    internal static UpdateCheckResult Available(SemanticVersion currentVersion, SemanticVersion availableVersion, GitHubRelease release) =>
        new(UpdateCheckStatus.Available, $"Version {availableVersion} is available.")
        {
            CurrentVersion = currentVersion,
            AvailableVersion = availableVersion,
            Tag = release.TagName,
            ReleaseNotes = release.ReleaseNotes,
            PublishedAt = release.PublishedAt,
            PageUri = release.PageUri,
            Assets = release.Assets,
        };

    internal static UpdateCheckResult Failed(string message) => new(UpdateCheckStatus.Failed, message);
}

/// <summary>Result of downloading and verifying an update package.</summary>
/// <param name="Success">True when the package exists and its SHA256 matched <c>SHA256SUMS.txt</c>.</param>
/// <param name="Message">Short, user-presentable status message.</param>
/// <param name="MsixPath">Full path of the verified MSIX when <paramref name="Success"/> is true.</param>
/// <param name="Tag">Release tag the package belongs to.</param>
/// <param name="Architecture">Architecture of the downloaded package.</param>
internal sealed record UpdateDownloadResult(
    bool Success,
    string Message,
    string? MsixPath,
    string? Tag,
    UpdateArchitecture Architecture)
{
    internal static UpdateDownloadResult Succeeded(string msixPath, string tag, UpdateArchitecture architecture, string message) =>
        new(true, message, msixPath, tag, architecture);

    internal static UpdateDownloadResult Failed(string message, UpdateArchitecture architecture = UpdateArchitecture.X64) =>
        new(false, message, null, null, architecture);
}

/// <summary>Result of staging the detached updater.</summary>
/// <param name="Success">True when the updater script was written and its process started.</param>
/// <param name="Message">Short, user-presentable status message.</param>
/// <param name="ScriptPath">Path of the generated updater script.</param>
/// <param name="LogPath">Path of the update log the script appends to.</param>
internal sealed record UpdateApplyResult(bool Success, string Message, string? ScriptPath, string? LogPath)
{
    internal static UpdateApplyResult Succeeded(string scriptPath, string logPath, string message) =>
        new(true, message, scriptPath, logPath);

    internal static UpdateApplyResult Failed(string message) => new(false, message, null, null);
}

/// <summary>
/// Self-update support for the extension: check GitHub Releases for a newer signed MSIX,
/// download it with its checksum file into
/// <c>%LOCALAPPDATA%\ItsycalForWindows\updates\&lt;tag&gt;\</c>, verify the SHA256, and stage a
/// detached PowerShell updater that installs the package and restarts Command Palette.
/// </summary>
/// <remarks>
/// <para>
/// Network use is limited to HTTPS GET requests for the GitHub Releases API and the release
/// assets of this repository. Nothing is sent anywhere: no telemetry, no calendar data.
/// </para>
/// <para>
/// MSIX updates require the package to be signed by a certificate this machine trusts; the
/// repository's development builds are signed by its persistent development certificate.
/// Windows refuses to update a running package, so the generated updater stops
/// <c>Microsoft.CmdPal.UI</c> and <c>ItsycalForWindows</c> before calling
/// <c>Add-AppxPackage</c>, then restarts the Command Palette host.
/// </para>
/// <para>
/// This type never throws into callers: every failure is returned as a failed result with a
/// short message. The pure decision helpers are <see langword="internal"/> and covered by unit
/// tests.
/// </para>
/// </remarks>
internal sealed class UpdateService
{
    /// <summary>Repository whose GitHub releases are checked.</summary>
    internal const string RepositorySlug = "Folieson/itsycal-for-windows";

    /// <summary>
    /// Template for the GitHub API asset endpoint. The release JSON carries a numeric asset id,
    /// and the API serves the same bytes with <c>Accept: application/octet-stream</c>; that
    /// endpoint survives proxies and rate limits that block the browser download URL.
    /// </summary>
    internal const string ApiAssetUrlFormat = "https://api.github.com/repos/{0}/releases/assets/{1}";

    private static readonly CompositeFormat ApiAssetUriFormat = CompositeFormat.Parse(ApiAssetUrlFormat);

    /// <summary>GitHub REST endpoint listing the newest releases.</summary>
    internal const string ReleasesUrl = "https://api.github.com/repos/Folieson/itsycal-for-windows/releases?per_page=10";

    /// <summary>User agent GitHub requires on API requests.</summary>
    internal const string UserAgent = "ItsycalForWindows-UpdateCheck/1.0";

    /// <summary>File name of the release checksum list.</summary>
    internal const string ChecksumFileName = "SHA256SUMS.txt";

    /// <summary>Minimum time between automatic background checks.</summary>
    internal static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(24);

    private const string DataFolderName = "ItsycalForWindows";
    private const string UpdatesFolderName = "updates";
    private const string StateFileName = "update-state.json";
    private const string LogFileName = "update.log";
    private const string UpdaterScriptName = "apply-update.ps1";
    private const string CommandPalettePackageName = "Microsoft.CommandPalette";
    private const string CommandPaletteProcessName = "Microsoft.CmdPal.UI";
    private const string ExtensionProcessName = "ItsycalForWindows";
    private const int MaxErrorMessageLength = 200;

    private static readonly Lazy<HttpClient> DefaultHttpClient = new(CreateHttpClient);

    private readonly UpdateSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly string _dataFolder;

    /// <summary>Creates the update service.</summary>
    /// <param name="settings">Update switches the service consults.</param>
    public UpdateService(UpdateSettings settings)
        : this(settings, handler: null, dataFolder: null)
    {
    }

    /// <summary>
    /// Test seam: lets tests inject an <see cref="HttpMessageHandler"/> and a temporary data
    /// folder so the download pipeline (browser URL, API fallback, SHA verification, file paths)
    /// is exercised without network access or writes to the real user profile.
    /// </summary>
    /// <param name="settings">Update switches the service consults.</param>
    /// <param name="handler">Handler for all HTTP requests; null uses the production client.</param>
    /// <param name="dataFolder">Root folder for downloads; null uses the real update data folder.</param>
    internal UpdateService(UpdateSettings settings, HttpMessageHandler? handler, string? dataFolder = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = handler is null ? DefaultHttpClient.Value : new HttpClient(handler, disposeHandler: false);
        _dataFolder = string.IsNullOrWhiteSpace(dataFolder) ? DataFolder : dataFolder;
    }

    /// <summary>Update switches backing the service.</summary>
    public UpdateSettings Settings => _settings;

    /// <summary>Architecture of the running package; used to pick the MSIX asset.</summary>
    public static UpdateArchitecture CurrentArchitecture => GetCurrentArchitecture();

    /// <summary>Base folder of the updater state, downloads and log.</summary>
    internal static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        DataFolderName);

    /// <summary>Timestamped log the updater script appends to.</summary>
    internal static string LogFilePath => Path.Combine(DataFolder, LogFileName);

    /// <summary>Folder holding the state file and all downloaded updates.</summary>
    internal static string UpdateStateFilePath => Path.Combine(DataFolder, StateFileName);

    /// <summary>
    /// True when an automatic check is due: the user enabled automatic checks and the last
    /// successful check is older than <see cref="AutoCheckInterval"/>. A missing or unreadable
    /// state file counts as "never checked", so the first launch after install checks once.
    /// </summary>
    public bool ShouldAutoCheck()
    {
        DateTimeOffset? lastCheckUtc;
        try
        {
            lastCheckUtc = ReadLastCheckUtc();
        }
        catch (Exception)
        {
            // The state file is an optimization; a broken one must not disable update checks.
            lastCheckUtc = null;
        }

        return IsAutoCheckDue(_settings.AutoCheck, lastCheckUtc, DateTimeOffset.UtcNow, AutoCheckInterval);
    }

    /// <summary>
    /// Checks GitHub Releases for a newer version. Respects
    /// <see cref="UpdateSettings.IncludePrereleases"/>, ignores drafts, and compares against the
    /// running package version (<c>Windows.ApplicationModel.Package</c>, with the assembly
    /// version as the unpackaged fallback). Records <c>LastCheckUtc</c> after a completed check.
    /// </summary>
    /// <param name="cancellationToken">Cancels the HTTP request.</param>
    /// <returns>The check result; failures are returned, never thrown.</returns>
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = GetCurrentVersion();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed($"GitHub returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var releases = ParseReleases(json);
            var newest = SelectNewestRelease(releases, _settings.IncludePrereleases);
            if (newest is null)
            {
                return UpdateCheckResult.Failed(
                    _settings.IncludePrereleases
                        ? "No release with a vX.Y.Z tag was found on GitHub."
                        : "No release with a vX.Y.Z tag was found; prereleases are ignored in settings.");
            }

            TryRecordCheck(DateTimeOffset.UtcNow);

            if (!TryParseReleaseTag(newest.TagName, out var newestVersion))
            {
                return UpdateCheckResult.Failed($"Release tag \"{newest.TagName}\" is not a vX.Y.Z version.");
            }

            if (newestVersion.CompareTo(currentVersion) <= 0)
            {
                return UpdateCheckResult.UpToDate(
                    currentVersion,
                    $"Itsycal for Windows {currentVersion} is up to date.");
            }

            var missingAssets = new List<string>(3);
            if (SelectMsixAsset(newest.Assets, UpdateArchitecture.X64) is null)
            {
                missingAssets.Add("x64 MSIX");
            }

            if (SelectMsixAsset(newest.Assets, UpdateArchitecture.Arm64) is null)
            {
                missingAssets.Add("arm64 MSIX");
            }

            if (SelectSumsAsset(newest.Assets) is null)
            {
                missingAssets.Add(ChecksumFileName);
            }

            if (missingAssets.Count > 0)
            {
                return UpdateCheckResult.Failed($"Release {newest.TagName} is missing: {string.Join(", ", missingAssets)}.");
            }

            return UpdateCheckResult.Available(currentVersion, newestVersion, newest);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UpdateCheckResult.Failed("The update server did not respond in time.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return UpdateCheckResult.Failed("The update check was cancelled.");
        }
        catch (Exception exception)
        {
            return UpdateCheckResult.Failed($"Update check failed: {Shorten(exception)}");
        }
    }

    /// <summary>
    /// Downloads the MSIX for the requested architecture and the release <c>SHA256SUMS.txt</c>
    /// into <c>%LOCALAPPDATA%\ItsycalForWindows\updates\&lt;tag&gt;\</c>, then verifies the
    /// package SHA256 against the checksum file. Verification fails closed: a missing checksum
    /// entry, a malformed checksum file, or a hash mismatch discards the download and returns a
    /// failed result.
    /// </summary>
    /// <param name="update">A result with <see cref="UpdateCheckResult.HasUpdate"/> set.</param>
    /// <param name="architecture">Architecture of the package to download.</param>
    /// <param name="cancellationToken">Cancels the downloads.</param>
    /// <returns>The download result; failures are returned, never thrown.</returns>
    public async Task<UpdateDownloadResult> DownloadAsync(
        UpdateCheckResult update,
        UpdateArchitecture architecture,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (update is null || !update.HasUpdate || string.IsNullOrEmpty(update.Tag))
            {
                return UpdateDownloadResult.Failed("No update is available to download.", architecture);
            }

            var msixAsset = SelectMsixAsset(update.Assets, architecture);
            if (msixAsset is null)
            {
                return UpdateDownloadResult.Failed($"Release {update.Tag} has no MSIX for {architecture}.", architecture);
            }

            var sumsAsset = SelectSumsAsset(update.Assets);
            if (sumsAsset is null)
            {
                return UpdateDownloadResult.Failed($"Release {update.Tag} has no {ChecksumFileName}.", architecture);
            }

            var folder = Path.Combine(_dataFolder, UpdatesFolderName, ToSafeFolderName(update.Tag));
            Directory.CreateDirectory(folder);

            // Asset names come from the GitHub API; keep only the file name so a crafted
            // release cannot write outside the updates folder.
            var msixFileName = Path.GetFileName(msixAsset.Name);
            var msixPath = Path.Combine(folder, msixFileName);
            var sumsPath = Path.Combine(folder, ChecksumFileName);

            var sumsDownloadPath = sumsPath + ".download";
            try
            {
                await DownloadFileAsync(sumsAsset.DownloadUri, sumsAsset.AssetId, sumsDownloadPath, cancellationToken).ConfigureAwait(false);
                File.Move(sumsDownloadPath, sumsPath, overwrite: true);
            }
            finally
            {
                TryDeleteFile(sumsDownloadPath);
            }

            var checksumText = await File.ReadAllTextAsync(sumsPath, cancellationToken).ConfigureAwait(false);
            if (!TryGetExpectedSha256(checksumText, msixAsset.Name, out var expectedSha256))
            {
                return UpdateDownloadResult.Failed(
                    $"{ChecksumFileName} has no valid SHA256 entry for {msixAsset.Name}.",
                    architecture);
            }

            // A previous attempt may already have produced a verified package; reuse it instead
            // of downloading the same file again.
            if (File.Exists(msixPath) && await MatchesSha256Async(msixPath, expectedSha256, cancellationToken).ConfigureAwait(false))
            {
                return UpdateDownloadResult.Succeeded(
                    msixPath,
                    update.Tag,
                    architecture,
                    $"Using the already verified package {msixAsset.Name}.");
            }

            var downloadPath = msixPath + ".download";
            try
            {
                await DownloadFileAsync(msixAsset.DownloadUri, msixAsset.AssetId, downloadPath, cancellationToken).ConfigureAwait(false);
                if (!await MatchesSha256Async(downloadPath, expectedSha256, cancellationToken).ConfigureAwait(false))
                {
                    TryDeleteFile(downloadPath);
                    return UpdateDownloadResult.Failed(
                        $"SHA256 verification failed for {msixAsset.Name}; the download was discarded.",
                        architecture);
                }

                File.Move(downloadPath, msixPath, overwrite: true);
            }
            catch (Exception exception)
            {
                TryDeleteFile(downloadPath);
                return UpdateDownloadResult.Failed($"Download failed: {Shorten(exception)}", architecture);
            }

            return UpdateDownloadResult.Succeeded(
                msixPath,
                update.Tag,
                architecture,
                $"Downloaded and verified {msixAsset.Name}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return UpdateDownloadResult.Failed($"Download failed: {Shorten(exception)}", architecture);
        }
        catch (OperationCanceledException)
        {
            return UpdateDownloadResult.Failed("The download was cancelled.", architecture);
        }
    }

    /// <summary>
    /// Writes a PowerShell updater next to the verified package and starts it detached. The
    /// script waits two seconds, stops Command Palette and the extension, installs the MSIX with
    /// <c>Add-AppxPackage</c>, restarts Command Palette from its package family name and app id,
    /// and appends a timestamped result to <c>%LOCALAPPDATA%\ItsycalForWindows\update.log</c>.
    /// </summary>
    /// <param name="verifiedMsixPath">Path returned by a successful <see cref="DownloadAsync"/>.</param>
    /// <param name="cancellationToken">Checked before the script is staged.</param>
    /// <returns>The staging result; failures are returned, never thrown.</returns>
    public static Task<UpdateApplyResult> ApplyAsync(string verifiedMsixPath, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(verifiedMsixPath))
            {
                return Task.FromResult(UpdateApplyResult.Failed("No verified update package was provided."));
            }

            var fullMsixPath = Path.GetFullPath(verifiedMsixPath);
            if (!File.Exists(fullMsixPath))
            {
                return Task.FromResult(UpdateApplyResult.Failed("The verified update package no longer exists."));
            }

            var updatesRoot = Path.GetFullPath(Path.Combine(DataFolder, UpdatesFolderName));
            if (!fullMsixPath.StartsWith(updatesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(UpdateApplyResult.Failed("The update package is not in the updater's download folder."));
            }

            if (!fullMsixPath.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(UpdateApplyResult.Failed("The update package is not an MSIX file."));
            }

            var scriptPath = Path.Combine(Path.GetDirectoryName(fullMsixPath)!, UpdaterScriptName);
            var script = BuildUpdaterScript(fullMsixPath, LogFilePath);
            File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var startInfo = new ProcessStartInfo
            {
                FileName = ResolveWindowsPowerShellPath(),
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-WindowStyle");
            startInfo.ArgumentList.Add("Hidden");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Task.FromResult(UpdateApplyResult.Failed("The updater process could not be started."));
            }

            return Task.FromResult(UpdateApplyResult.Succeeded(
                scriptPath,
                LogFilePath,
                "The update will be installed in a few seconds; Command Palette restarts automatically."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(UpdateApplyResult.Failed($"Could not start the updater: {Shorten(exception)}"));
        }
    }

    /// <summary>True when an automatic check is due at <paramref name="nowUtc"/>.</summary>
    /// <param name="autoCheck">Value of <see cref="UpdateSettings.AutoCheck"/>.</param>
    /// <param name="LastCheckUtc">Timestamp of the last completed check, or null.</param>
    /// <param name="nowUtc">Current UTC time.</param>
    /// <param name="interval">Minimum time between checks.</param>
    internal static bool IsAutoCheckDue(bool autoCheck, DateTimeOffset? lastCheckUtc, DateTimeOffset nowUtc, TimeSpan interval)
    {
        if (!autoCheck)
        {
            return false;
        }

        return lastCheckUtc is null || nowUtc - lastCheckUtc.Value >= interval;
    }

    /// <summary>Compares two versions by major, minor and patch.</summary>
    internal static int CompareVersions(SemanticVersion left, SemanticVersion right) => left.CompareTo(right);

    /// <summary>Parses a release tag such as <c>v1.2.0</c>.</summary>
    internal static bool TryParseReleaseTag(string? tag, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(tag) || tag[0] != 'v')
        {
            return false;
        }

        var parts = tag[1..].Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch);
        return true;
    }

    /// <summary>
    /// Parses the JSON body of the GitHub releases API. Malformed entries are skipped; a body
    /// that is not a JSON array or cannot be parsed throws <see cref="JsonException"/> (caught by
    /// <see cref="CheckAsync"/>).
    /// </summary>
    internal static IReadOnlyList<GitHubRelease> ParseReleases(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var releases = new List<GitHubRelease>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var tagName = GetString(element, "tag_name");
            if (string.IsNullOrWhiteSpace(tagName))
            {
                continue;
            }

            var assets = new List<ReleaseAsset>();
            if (element.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var assetElement in assetsElement.EnumerateArray())
                {
                    var name = GetString(assetElement, "name");
                    var url = GetString(assetElement, "browser_download_url");
                    if (string.IsNullOrWhiteSpace(name) ||
                        !Uri.TryCreate(url, UriKind.Absolute, out var downloadUri) ||
                        !string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    assets.Add(new ReleaseAsset(
                        name,
                        downloadUri,
                        assetElement.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var assetId)
                            ? assetId
                            : 0));
                }
            }

            releases.Add(new GitHubRelease(
                tagName,
                IsTrue(element, "draft"),
                IsTrue(element, "prerelease"),
                GetDateTimeOffset(element, "published_at"),
                GetString(element, "body") ?? string.Empty,
                Uri.TryCreate(GetString(element, "html_url"), UriKind.Absolute, out var pageUri) ? pageUri : null,
                assets));
        }

        return releases;
    }

    /// <summary>
    /// Picks the highest <c>vX.Y.Z</c> release, skipping drafts and, unless requested,
    /// prereleases. Releases whose tag does not parse as a version are ignored.
    /// </summary>
    internal static GitHubRelease? SelectNewestRelease(IEnumerable<GitHubRelease>? releases, bool includePrereleases)
    {
        if (releases is null)
        {
            return null;
        }

        GitHubRelease? newest = null;
        var newestVersion = default(SemanticVersion);
        var hasNewest = false;
        foreach (var release in releases)
        {
            if (release is null || release.Draft || (release.Prerelease && !includePrereleases))
            {
                continue;
            }

            if (!TryParseReleaseTag(release.TagName, out var version))
            {
                continue;
            }

            if (!hasNewest || version.CompareTo(newestVersion) > 0)
            {
                newest = release;
                newestVersion = version;
                hasNewest = true;
            }
        }

        return newest;
    }

    /// <summary>Ends the asset file name that identifies the MSIX of an architecture.</summary>
    internal static string ToAssetSuffix(UpdateArchitecture architecture) =>
        architecture == UpdateArchitecture.Arm64 ? "_arm64.msix" : "_x64.msix";

    /// <summary>Finds the MSIX asset for the requested architecture by file-name suffix.</summary>
    internal static ReleaseAsset? SelectMsixAsset(IReadOnlyList<ReleaseAsset>? assets, UpdateArchitecture architecture)
    {
        if (assets is null)
        {
            return null;
        }

        var suffix = ToAssetSuffix(architecture);
        return assets.FirstOrDefault(asset =>
            asset.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds the release <c>SHA256SUMS.txt</c> asset.</summary>
    internal static ReleaseAsset? SelectSumsAsset(IReadOnlyList<ReleaseAsset>? assets)
    {
        if (assets is null)
        {
            return null;
        }

        return assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, ChecksumFileName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Looks up the expected SHA256 of <paramref name="fileName"/> in the content of a
    /// <c>SHA256SUMS.txt</c> file. Accepts the standard two-column format (one or more spaces or
    /// tabs between hash and file name), lowercase or uppercase hashes, and the binary-mode
    /// <c>*</c> marker before the file name. Malformed lines are ignored, so a missing or invalid
    /// entry makes the caller fail closed.
    /// </summary>
    internal static bool TryGetExpectedSha256(string checksumFileContent, string fileName, out string sha256)
    {
        sha256 = string.Empty;
        if (string.IsNullOrEmpty(checksumFileContent) || string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var target = Path.GetFileName(fileName.Trim());
        foreach (var rawLine in checksumFileContent.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var separatorIndex = -1;
            for (var index = 0; index < line.Length; index++)
            {
                if (char.IsWhiteSpace(line[index]))
                {
                    separatorIndex = index;
                    break;
                }
            }

            if (separatorIndex <= 0)
            {
                continue;
            }

            var hashCandidate = line[..separatorIndex];
            if (!IsSha256Hex(hashCandidate))
            {
                continue;
            }

            var nameCandidate = line[(separatorIndex + 1)..].TrimStart();
            if (nameCandidate.StartsWith('*'))
            {
                nameCandidate = nameCandidate[1..];
            }

            if (!string.Equals(nameCandidate, target, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sha256 = hashCandidate.ToLowerInvariant();
            return true;
        }

        return false;
    }

    /// <summary>Version of the running package, or of the assembly for unpackaged runs.</summary>
    internal static SemanticVersion GetCurrentVersion()
    {
        try
        {
            var packageVersion = Windows.ApplicationModel.Package.Current.Id.Version;
            return new SemanticVersion(packageVersion.Major, packageVersion.Minor, packageVersion.Build);
        }
        catch (Exception)
        {
            // Unpackaged runs (tests, dotnet run) have no package identity.
            var assemblyVersion = typeof(UpdateService).Assembly.GetName().Version;
            return assemblyVersion is null
                ? new SemanticVersion(0, 0, 0)
                : new SemanticVersion(
                    assemblyVersion.Major,
                    Math.Max(0, assemblyVersion.Minor),
                    Math.Max(0, assemblyVersion.Build));
        }
    }

    /// <summary>
    /// Architecture of the current process. This repository publishes only x64 and arm64
    /// packages, so any other process architecture falls back to x64.
    /// </summary>
    internal static UpdateArchitecture GetCurrentArchitecture() =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? UpdateArchitecture.Arm64
            : UpdateArchitecture.X64;

    /// <summary>Folder that receives the downloads of one release tag.</summary>

    /// <summary>
    /// Turns a release tag into a folder name that cannot escape the updates folder: only
    /// letters, digits, dots, dashes and underscores survive, and the leading/trailing dots are
    /// removed.
    /// </summary>
    internal static string ToSafeFolderName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '_');
        }

        var safe = builder.ToString().Trim('.', ' ');
        return safe.Length == 0 ? "unknown" : safe;
    }

    /// <summary>
    /// Builds the detached updater script. The MSIX must be signed by a certificate the machine
    /// trusts; the script closes Command Palette and the extension before installing.
    /// </summary>
    internal static string BuildUpdaterScript(string msixPath, string logPath)
    {
        var script = new StringBuilder();
        script.AppendLine("# Itsycal for Windows updater. Generated by UpdateService; safe to delete after it runs.");
        script.AppendLine("# Requires an MSIX signed by a certificate this machine trusts. Windows refuses to update");
        script.AppendLine("# a running package, so Command Palette and the extension are stopped first.");
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.Append("$msix = '").Append(EscapePowerShellLiteral(Path.GetFullPath(msixPath))).AppendLine("'");
        script.Append("$log = '").Append(EscapePowerShellLiteral(Path.GetFullPath(logPath))).AppendLine("'");
        script.Append("$processNames = @('")
            .Append(EscapePowerShellLiteral(CommandPaletteProcessName))
            .Append("', '")
            .Append(EscapePowerShellLiteral(ExtensionProcessName))
            .AppendLine("')");
        script.Append("$hostPackageName = '").Append(EscapePowerShellLiteral(CommandPalettePackageName)).AppendLine("'");
        script.AppendLine();
        script.AppendLine("function Write-UpdateLog([string] $message) {");
        script.AppendLine("    $line = '{0} {1}' -f (Get-Date).ToUniversalTime().ToString('o'), $message");
        script.AppendLine("    try {");
        script.AppendLine("        $directory = Split-Path -Parent $log");
        script.AppendLine("        if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }");
        script.AppendLine("        Add-Content -LiteralPath $log -Value $line");
        script.AppendLine("    } catch {");
        script.AppendLine("        # Logging is best effort and must never block the update.");
        script.AppendLine("    }");
        script.AppendLine("}");
        script.AppendLine();
        script.AppendLine("Write-UpdateLog \"starting update: $msix\"");
        script.AppendLine("Start-Sleep -Seconds 2");
        script.AppendLine();
        script.AppendLine("try {");
        script.AppendLine("    foreach ($name in $processNames) {");
        script.AppendLine("        Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue");
        script.AppendLine("    }");
        script.AppendLine("    Start-Sleep -Milliseconds 500");
        script.AppendLine("    Add-AppxPackage -Path $msix -ErrorAction Stop");
        script.AppendLine("    Write-UpdateLog \"installed: $msix\"");
        script.AppendLine("} catch {");
        script.AppendLine("    Write-UpdateLog (\"install failed: \" + $_.Exception.Message)");
        script.AppendLine("    exit 1");
        script.AppendLine("}");
        script.AppendLine();
        script.AppendLine("try {");
        script.AppendLine("    $hostPackage = Get-AppxPackage -Name $hostPackageName -ErrorAction SilentlyContinue");
        script.AppendLine("    if ($hostPackage) {");
        script.AppendLine("        $applicationId = (Get-AppxPackageManifest -Package $hostPackage.PackageFullName).Package.Applications.Application.Id");
        script.AppendLine("        Start-Process -FilePath ('shell:AppsFolder\\{0}!{1}' -f $hostPackage.PackageFamilyName, $applicationId)");
        script.AppendLine("        Write-UpdateLog 'restarted Command Palette'");
        script.AppendLine("    } else {");
        script.AppendLine("        Write-UpdateLog \"package $hostPackageName not found; restart Command Palette manually\"");
        script.AppendLine("    }");
        script.AppendLine("} catch {");
        script.AppendLine("    Write-UpdateLog (\"restart failed: \" + $_.Exception.Message)");
        script.AppendLine("}");
        return script.ToString();
    }

    /// <summary>Escapes a value for use inside a PowerShell single-quoted string.</summary>
    internal static string EscapePowerShellLiteral(string value) => value
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal);

    /// <summary>Creates the shared HTTP client used by the check and download requests.</summary>
    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>Builds the GitHub API endpoint for one release asset.</summary>
    /// <param name="assetId">Numeric id from the release JSON.</param>
    internal static Uri BuildApiAssetUri(long assetId) => new(string.Format(
        CultureInfo.InvariantCulture,
        ApiAssetUriFormat,
        RepositorySlug,
        assetId));

    /// <summary>
    /// True when a failed browser-asset download is worth retrying through the GitHub API asset
    /// endpoint: transport failures (no status), server errors, rate limiting and forbidden
    /// responses. Client errors such as 404 are not retried.
    /// </summary>
    /// <param name="statusCode">Status reported by the failed request, or null for transport failures.</param>
    internal static bool ShouldRetryViaApi(HttpStatusCode? statusCode) =>
        statusCode is null
        || (int)statusCode >= 500
        || statusCode == HttpStatusCode.TooManyRequests
        || statusCode == HttpStatusCode.Forbidden;


    /// <summary>
    /// True when a failed browser download should be retried through the API asset endpoint:
    /// HTTP errors classified by <see cref="ShouldRetryViaApi"/>, request timeouts surfaced as
    /// <see cref="OperationCanceledException"/> while the caller token is still live, and
    /// transport failures while reading the response body. A user cancellation is never retried.
    /// </summary>
    /// <param name="assetId">Numeric release-asset id; zero disables the fallback.</param>
    /// <param name="exception">Exception raised by the browser download attempt.</param>
    /// <param name="cancellationToken">Caller token; a requested cancellation disables the fallback.</param>
    internal static bool ShouldFallbackToApi(long assetId, Exception exception, CancellationToken cancellationToken)
    {
        if (assetId <= 0 || cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return exception switch
        {
            HttpRequestException http => ShouldRetryViaApi(http.StatusCode),
            OperationCanceledException => true, // HttpClient timeout, not a user cancellation
            IOException => true, // response stream died mid-download
            _ => false,
        };
    }

    private async Task DownloadFileAsync(Uri uri, long assetId, string path, CancellationToken cancellationToken)
    {
        try
        {
            await DownloadFileFromUriAsync(uri, path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (ShouldFallbackToApi(assetId, exception, cancellationToken))
        {
            await DownloadFileFromApiAsync(assetId, path, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DownloadFileFromUriAsync(Uri uri, string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} while downloading {uri.Host}.",
                inner: null,
                response.StatusCode);
        }

        await CopyResponseToFileAsync(response, path, cancellationToken).ConfigureAwait(false);
    }

    private async Task DownloadFileFromApiAsync(long assetId, string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildApiAssetUri(assetId));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} while downloading GitHub API asset {assetId}.",
                inner: null,
                response.StatusCode);
        }

        await CopyResponseToFileAsync(response, path, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyResponseToFileAsync(
        HttpResponseMessage response,
        string path,
        CancellationToken cancellationToken)
    {
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            useAsync: true);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> MatchesSha256Async(string path, string expectedSha256, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(hash), expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSha256Hex(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            var isHex = (character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f')
                || (character >= 'A' && character <= 'F');
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    private static DateTimeOffset? ReadLastCheckUtc()
    {
        if (!File.Exists(UpdateStateFilePath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(UpdateStateFilePath));
        if (document.RootElement.ValueKind == JsonValueKind.Object &&
            document.RootElement.TryGetProperty("LastCheckUtc", out var element) &&
            element.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                element.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var lastCheckUtc))
        {
            return lastCheckUtc;
        }

        return null;
    }

    private static void TryRecordCheck(DateTimeOffset checkedUtc)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            var json = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"LastCheckUtc\":\"{0:O}\"}}",
                checkedUtc);
            File.WriteAllText(UpdateStateFilePath, json);
        }
        catch (Exception)
        {
            // Losing the timestamp only means the next launch may check again.
        }
    }

    private static string ResolveWindowsPowerShellPath()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windowsDirectory))
        {
            var candidate = Path.Combine(windowsDirectory, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "powershell.exe";
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool IsTrue(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string Shorten(Exception exception)
    {
        var message = exception.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = exception.GetType().Name;
        }

        message = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length <= MaxErrorMessageLength ? message : message[..MaxErrorMessageLength] + "...";
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // A leftover partial download is harmless; it is overwritten next time.
        }
    }
}
