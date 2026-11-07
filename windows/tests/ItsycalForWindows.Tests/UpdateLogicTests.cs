using System;
using System.Collections.Generic;
using System.Linq;
using ItsycalForWindows.Services;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for the pure update logic behind <see cref="UpdateService"/>: release-tag parsing and
/// ordering, GitHub release selection (highest version, drafts and prereleases excluded),
/// checksum-file parsing and architecture asset selection. No network or file-system access is
/// involved, so these tests run offline in CI.
/// </summary>
public sealed class UpdateLogicTests
{
    private const string Sha256A = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string Sha256B = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    // --- release-tag parsing and ordering -----------------------------------

    [Theory]
    [InlineData("v0.0.0", 0, 0, 0)]
    [InlineData("v1.2.0", 1, 2, 0)]
    [InlineData("v10.20.30", 10, 20, 30)]
    public void TryParseReleaseTag_AcceptsThreePartTags(string tag, int major, int minor, int patch)
    {
        Assert.True(UpdateService.TryParseReleaseTag(tag, out var version));
        Assert.Equal(new SemanticVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2.0")]
    [InlineData("V1.2.0")]
    [InlineData("v1.2")]
    [InlineData("v1.2.3.4")]
    [InlineData("v1.2.x")]
    [InlineData("v-1.2.3")]
    [InlineData("v1.2.3-beta")]
    [InlineData("release-v1.2.3")]
    public void TryParseReleaseTag_RejectsOtherShapes(string tag)
    {
        Assert.False(UpdateService.TryParseReleaseTag(tag, out _));
    }

    [Fact]
    public void TryParseReleaseTag_RejectsNull()
    {
        Assert.False(UpdateService.TryParseReleaseTag(null, out _));
    }

    [Fact]
    public void CompareVersions_OrdersByMajorThenMinorThenPatch()
    {
        Assert.True(UpdateService.CompareVersions(Version(1, 1, 1), Version(1, 2, 0)) < 0);
        Assert.True(UpdateService.CompareVersions(Version(1, 2, 0), Version(1, 1, 9)) > 0);
        Assert.True(UpdateService.CompareVersions(Version(1, 2, 3), Version(1, 2, 4)) < 0);
        Assert.Equal(0, UpdateService.CompareVersions(Version(1, 2, 3), Version(1, 2, 3)));
        Assert.True(UpdateService.CompareVersions(Version(10, 0, 0), Version(9, 99, 99)) > 0);
    }

    // --- GitHub release selection -------------------------------------------

    [Fact]
    public void SelectNewestRelease_PicksTheHighestVersionNotTheNewestArrayEntry()
    {
        GitHubRelease[] releases =
        [
            Release("v1.1.1"),
            Release("v1.2.0"),
            Release("v1.0.9"),
        ];

        Assert.Equal("v1.2.0", UpdateService.SelectNewestRelease(releases, includePrereleases: true)?.TagName);
    }

    [Fact]
    public void SelectNewestRelease_ComparesSemanticallyNotLexically()
    {
        GitHubRelease[] releases =
        [
            Release("v1.9.0"),
            Release("v1.10.0"),
            Release("v1.2.0"),
        ];

        Assert.Equal("v1.10.0", UpdateService.SelectNewestRelease(releases, includePrereleases: true)?.TagName);
    }

    [Fact]
    public void SelectNewestRelease_SkipsDrafts()
    {
        GitHubRelease[] releases =
        [
            Release("v2.0.0", draft: true),
            Release("v1.1.1"),
        ];

        Assert.Equal("v1.1.1", UpdateService.SelectNewestRelease(releases, includePrereleases: true)?.TagName);
    }

    [Fact]
    public void SelectNewestRelease_ExcludesPrereleasesWhenDisabled()
    {
        GitHubRelease[] releases =
        [
            Release("v1.0.0"),
            Release("v1.1.1", prerelease: true),
        ];

        Assert.Equal("v1.1.1", UpdateService.SelectNewestRelease(releases, includePrereleases: true)?.TagName);
        Assert.Equal("v1.0.0", UpdateService.SelectNewestRelease(releases, includePrereleases: false)?.TagName);
    }

    [Fact]
    public void SelectNewestRelease_IgnoresTagsThatAreNotVersions()
    {
        GitHubRelease[] releases =
        [
            Release("nightly-2026-11-08"),
            Release("v1.1.0"),
            Release("v2.0"),
        ];

        Assert.Equal("v1.1.0", UpdateService.SelectNewestRelease(releases, includePrereleases: true)?.TagName);
    }

    [Fact]
    public void SelectNewestRelease_ReturnsNullWhenNothingIsEligible()
    {
        Assert.Null(UpdateService.SelectNewestRelease(null, includePrereleases: true));
        Assert.Null(UpdateService.SelectNewestRelease(Array.Empty<GitHubRelease>(), includePrereleases: true));
        Assert.Null(UpdateService.SelectNewestRelease([Release("v2.0.0", draft: true)], includePrereleases: true));
        Assert.Null(UpdateService.SelectNewestRelease([Release("v1.1.1", prerelease: true)], includePrereleases: false));
    }

    [Fact]
    public void ParseReleases_ReadsTheGitHubReleaseShape()
    {
        const string Json = """
            [
              {
                "tag_name": "v1.1.1",
                "draft": false,
                "prerelease": true,
                "published_at": "2026-11-07T21:46:21Z",
                "html_url": "https://github.com/Folieson/itsycal-for-windows/releases/tag/v1.1.1",
                "body": "Release notes",
                "assets": [
                  {
                    "name": "ItsycalForWindows_1.1.1.0_x64.msix",
                    "browser_download_url": "https://github.com/Folieson/itsycal-for-windows/releases/download/v1.1.1/ItsycalForWindows_1.1.1.0_x64.msix"
                  },
                  {
                    "name": "SHA256SUMS.txt",
                    "browser_download_url": "https://github.com/Folieson/itsycal-for-windows/releases/download/v1.1.1/SHA256SUMS.txt"
                  },
                  {
                    "name": "insecure.msix",
                    "browser_download_url": "http://example.test/insecure.msix"
                  }
                ]
              },
              {
                "tag_name": "v2.0.0",
                "draft": true,
                "prerelease": false,
                "assets": []
              }
            ]
            """;

        var releases = UpdateService.ParseReleases(Json);

        Assert.Equal(2, releases.Count);
        var release = releases[0];
        Assert.Equal("v1.1.1", release.TagName);
        Assert.False(release.Draft);
        Assert.True(release.Prerelease);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 21, 46, 21, TimeSpan.Zero), release.PublishedAt);
        Assert.Equal("Release notes", release.ReleaseNotes);
        Assert.Equal("https://github.com/Folieson/itsycal-for-windows/releases/tag/v1.1.1", release.PageUri?.ToString());
        Assert.Equal(2, release.Assets.Count);
        Assert.Equal("ItsycalForWindows_1.1.1.0_x64.msix", release.Assets[0].Name);
        Assert.True(releases[1].Draft);
        Assert.Empty(releases[1].Assets);
    }

    [Fact]
    public void ParseReleases_ReturnsEmptyListForANonArrayDocument()
    {
        Assert.Empty(UpdateService.ParseReleases("{}"));
    }

    // --- asset selection -----------------------------------------------------

    [Fact]
    public void SelectMsixAsset_PicksTheRequestedArchitecture()
    {
        ReleaseAsset[] assets =
        [
            Asset("ItsycalForWindows_1.1.1.0_arm64.msix"),
            Asset("ItsycalForWindows_1.1.1.0_dev.cer"),
            Asset("ItsycalForWindows_1.1.1.0_x64.msix"),
            Asset("SHA256SUMS.txt"),
            Asset("ItsycalForWindows_1.1.1.0_winget-manifests.zip"),
        ];

        Assert.Equal("ItsycalForWindows_1.1.1.0_x64.msix", UpdateService.SelectMsixAsset(assets, UpdateArchitecture.X64)?.Name);
        Assert.Equal("ItsycalForWindows_1.1.1.0_arm64.msix", UpdateService.SelectMsixAsset(assets, UpdateArchitecture.Arm64)?.Name);
    }

    [Fact]
    public void SelectMsixAsset_ReturnsNullWhenTheArchitectureIsMissing()
    {
        ReleaseAsset[] assets = [Asset("ItsycalForWindows_1.1.1.0_x64.msix")];

        Assert.Null(UpdateService.SelectMsixAsset(assets, UpdateArchitecture.Arm64));
        Assert.Null(UpdateService.SelectMsixAsset(null, UpdateArchitecture.X64));
    }

    [Fact]
    public void SelectMsixAsset_RequiresTheExactSuffix()
    {
        ReleaseAsset[] assets =
        [
            Asset("ItsycalForWindows_1.1.1.0_x64.msix.txt"),
            Asset("ItsycalForWindows_1.1.1.0_ia64.msix"),
        ];

        Assert.Null(UpdateService.SelectMsixAsset(assets, UpdateArchitecture.X64));
        Assert.Null(UpdateService.SelectMsixAsset(assets, UpdateArchitecture.Arm64));
    }

    [Fact]
    public void SelectSumsAsset_FindsTheChecksumFileCaseInsensitively()
    {
        ReleaseAsset[] assets = [Asset("sha256sums.txt")];

        Assert.Equal("sha256sums.txt", UpdateService.SelectSumsAsset(assets)?.Name);
        Assert.Null(UpdateService.SelectSumsAsset([Asset("checksums.txt")]));
        Assert.Null(UpdateService.SelectSumsAsset(null));
    }

    // --- SHA256SUMS parsing --------------------------------------------------

    [Fact]
    public void TryGetExpectedSha256_ParsesTheStandardTwoColumnFormat()
    {
        var content = $"{Sha256A}  ItsycalForWindows_1.1.1.0_x64.msix\n{Sha256B}  SHA256SUMS.txt\n";

        Assert.True(UpdateService.TryGetExpectedSha256(content, "ItsycalForWindows_1.1.1.0_x64.msix", out var sha256));
        Assert.Equal(Sha256A, sha256);
    }

    [Fact]
    public void TryGetExpectedSha256_AcceptsUppercaseHashesAndTheBinaryMarker()
    {
        var content = $"{Sha256A.ToUpperInvariant()}\t*ItsycalForWindows_1.1.1.0_arm64.msix\r\n";
        var contentWithUppercase = content.Replace("\t", " ", StringComparison.Ordinal);

        Assert.True(UpdateService.TryGetExpectedSha256(contentWithUppercase, "ItsycalForWindows_1.1.1.0_arm64.msix", out var sha256));
        Assert.Equal(Sha256A, sha256);
    }

    [Fact]
    public void TryGetExpectedSha256_IgnoresCommentsBlankAndMalformedLines()
    {
        var content = string.Join(
            '\n',
            "# generated by the release pipeline",
            string.Empty,
            "not-a-hash  ItsycalForWindows_1.1.1.0_x64.msix",
            $"{Sha256A[..32]}  ItsycalForWindows_1.1.1.0_x64.msix",
            $"{Sha256B}  other.msix",
            $"{Sha256A}  ItsycalForWindows_1.1.1.0_x64.msix");

        Assert.True(UpdateService.TryGetExpectedSha256(content, "ItsycalForWindows_1.1.1.0_x64.msix", out var sha256));
        Assert.Equal(Sha256A, sha256);
    }

    [Fact]
    public void TryGetExpectedSha256_FailsClosedWhenTheFileHasNoValidEntry()
    {
        var content = $"{Sha256B}  other.msix\n";

        Assert.False(UpdateService.TryGetExpectedSha256(content, "ItsycalForWindows_1.1.1.0_x64.msix", out var sha256));
        Assert.Equal(string.Empty, sha256);
        Assert.False(UpdateService.TryGetExpectedSha256(string.Empty, "ItsycalForWindows_1.1.1.0_x64.msix", out _));
        Assert.False(UpdateService.TryGetExpectedSha256(content, " ", out _));
    }

    [Fact]
    public void TryGetExpectedSha256_MatchesAPathByItsFileName()
    {
        var content = $"{Sha256A}  ItsycalForWindows_1.1.1.0_x64.msix\n";
        var path = @"C:\Users\test\AppData\Local\ItsycalForWindows\updates\v1.1.1\ItsycalForWindows_1.1.1.0_x64.msix";

        Assert.True(UpdateService.TryGetExpectedSha256(content, path, out var sha256));
        Assert.Equal(Sha256A, sha256);
    }

    // --- auto-check gating ---------------------------------------------------

    [Fact]
    public void IsAutoCheckDue_ChecksWhenNeverChecked()
    {
        var now = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

        Assert.True(UpdateService.IsAutoCheckDue(true, null, now, TimeSpan.FromHours(24)));
    }

    [Fact]
    public void IsAutoCheckDue_RespectsTheInterval()
    {
        var now = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

        Assert.False(UpdateService.IsAutoCheckDue(true, now.AddHours(-1), now, TimeSpan.FromHours(24)));
        Assert.False(UpdateService.IsAutoCheckDue(true, now.AddHours(-23), now, TimeSpan.FromHours(24)));
        Assert.True(UpdateService.IsAutoCheckDue(true, now.AddHours(-24), now, TimeSpan.FromHours(24)));
        Assert.True(UpdateService.IsAutoCheckDue(true, now.AddHours(-25), now, TimeSpan.FromHours(24)));
    }

    [Fact]
    public void IsAutoCheckDue_StaysFalseWhenAutomaticChecksAreDisabled()
    {
        var now = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

        Assert.False(UpdateService.IsAutoCheckDue(false, null, now, TimeSpan.FromHours(24)));
        Assert.False(UpdateService.IsAutoCheckDue(false, now.AddDays(-30), now, TimeSpan.FromHours(24)));
    }

    // --- supporting helpers --------------------------------------------------

    [Fact]
    public void ToSafeFolderName_KeepsVersionTagsAndStripsPathSeparators()
    {
        Assert.Equal("v1.1.1", UpdateService.ToSafeFolderName("v1.1.1"));

        var safe = UpdateService.ToSafeFolderName("../../Windows/System32");
        Assert.DoesNotContain("/", safe);
        Assert.DoesNotContain("\\", safe);
        Assert.True(safe.Length > 0);
    }

    [Fact]
    public void EscapePowerShellLiteral_DoublesSingleQuotes()
    {
        Assert.Equal("C:\\O''Brien\\pkg.msix", UpdateService.EscapePowerShellLiteral("C:\\O'Brien\\pkg.msix"));
    }

    [Fact]
    public void BuildUpdaterScript_InstallsThePackageAndRestartsTheHost()
    {
        var script = UpdateService.BuildUpdaterScript(
            @"C:\Users\test\AppData\Local\ItsycalForWindows\updates\v1.1.1\ItsycalForWindows_1.1.1.0_x64.msix",
            @"C:\Users\test\AppData\Local\ItsycalForWindows\update.log");

        Assert.Contains("Add-AppxPackage -Path $msix -ErrorAction Stop", script);
        Assert.Contains("Microsoft.CmdPal.UI", script);
        Assert.Contains("ItsycalForWindows", script);
        Assert.Contains("update.log", script);
        Assert.Contains("Start-Sleep -Seconds 2", script);
        Assert.Contains("Get-AppxPackage -Name $hostPackageName", script);
    }

    private static SemanticVersion Version(int major, int minor, int patch) => new(major, minor, patch);

    private static ReleaseAsset Asset(string name) =>
        new(name, new Uri($"https://example.test/{name}", UriKind.Absolute));

    private static GitHubRelease Release(string tag, bool draft = false, bool prerelease = false, params ReleaseAsset[] assets) =>
        new(tag, draft, prerelease, null, string.Empty, null, assets);
}
