using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

    [Fact]
    public void BuildApiAssetUri_UsesRepositoryAndAssetId()
    {
        var uri = UpdateService.BuildApiAssetUri(123456789);

        Assert.Equal(
            "https://api.github.com/repos/Folieson/itsycal-for-windows/releases/assets/123456789",
            uri.ToString());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(504, true)]
    [InlineData(429, true)]
    [InlineData(403, true)]
    [InlineData(200, false)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(404, false)]
    [InlineData(408, false)]
    public void ShouldRetryViaApi_MatchesRetryableFailures(int? statusCode, bool expected)
    {
        var status = statusCode is null ? (HttpStatusCode?)null : (HttpStatusCode)statusCode.Value;

        Assert.Equal(expected, UpdateService.ShouldRetryViaApi(status));
    }

    [Theory]
    [InlineData(0, 504, false)]
    [InlineData(42, 504, true)]
    [InlineData(42, null, true)]
    [InlineData(42, 404, false)]
    public void CanRetryViaApi_RequiresAssetIdAndRetryableStatus(int assetId, int? statusCode, bool expected)
    {
        var status = statusCode is null ? (HttpStatusCode?)null : (HttpStatusCode)statusCode.Value;

        Assert.Equal(expected, UpdateService.CanRetryViaApi(assetId, status));
    }

    [Fact]
    public void ParseReleases_ReadsAssetIds()
    {
        const string json = """
        [
          {
            "tag_name": "v1.2.0",
            "draft": false,
            "prerelease": true,
            "published_at": "2026-11-08T00:00:00Z",
            "body": "",
            "html_url": "https://github.com/Folieson/itsycal-for-windows/releases/tag/v1.2.0",
            "assets": [
              {
                "id": 42,
                "name": "ItsycalForWindows_1.2.0.0_x64.msix",
                "browser_download_url": "https://github.com/Folieson/itsycal-for-windows/releases/download/v1.2.0/ItsycalForWindows_1.2.0.0_x64.msix"
              },
              {
                "name": "SHA256SUMS.txt",
                "browser_download_url": "https://github.com/Folieson/itsycal-for-windows/releases/download/v1.2.0/SHA256SUMS.txt"
              }
            ]
          }
        ]
        """;

        var release = Assert.Single(UpdateService.ParseReleases(json));

        Assert.Equal(42, release.Assets[0].AssetId);
        Assert.Equal(0, release.Assets[1].AssetId);
    }

    [Fact]
    public void DownloadAsync_BrowserOnlySendsOneRequestPerAsset()
    {
        var check = CheckWithAssets();
        var (service, folder, handler) = NewService(request =>
            request.RequestUri!.AbsolutePath.Contains("SHA256SUMS.txt", StringComparison.Ordinal)
                ? Ok(SumsBytes(MsixName))
                : Ok(MsixBytes));

        try
        {
            var result = service.DownloadAsync(check, UpdateArchitecture.X64).GetAwaiter().GetResult();

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, handler.Requests.Count);
            Assert.All(handler.Requests, request => Assert.Equal("github.com", request.RequestUri!.Host));
            Assert.True(File.Exists(result.MsixPath));
            Assert.Contains("Downloaded and verified", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public void DownloadAsync_FallsBackToApiForChecksumAndMsix()
    {
        var check = CheckWithAssets();
        var (service, folder, handler) = NewService(request =>
        {
            if (request.RequestUri!.Host == "github.com")
            {
                return Status(HttpStatusCode.GatewayTimeout);
            }

            Assert.Equal("api.github.com", request.RequestUri.Host);
            Assert.Contains(request.Headers.Accept, value => value.MediaType == "application/octet-stream");
            return request.RequestUri.AbsolutePath.Contains("/assets/7", StringComparison.Ordinal)
                ? Ok(SumsBytes(MsixName))
                : Ok(MsixBytes);
        });

        try
        {
            var result = service.DownloadAsync(check, UpdateArchitecture.X64).GetAwaiter().GetResult();

            Assert.True(result.Success, result.Message);
            Assert.Equal(4, handler.Requests.Count);
            Assert.Equal(
                new[] { "github.com", "api.github.com", "github.com", "api.github.com" },
                handler.Requests.Select(request => request.RequestUri!.Host));
            Assert.Contains("SHA256SUMS.txt", handler.Requests[0].RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("/assets/7", handler.Requests[1].RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.True(File.Exists(result.MsixPath));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public void DownloadAsync_FailedApiFallbackReturnsFailureWithoutThrowing()
    {
        var check = CheckWithAssets();
        var (service, folder, handler) = NewService(request =>
            request.RequestUri!.Host == "github.com"
                ? Status(HttpStatusCode.GatewayTimeout)
                : Status(HttpStatusCode.InternalServerError));

        try
        {
            var result = service.DownloadAsync(check, UpdateArchitecture.X64).GetAwaiter().GetResult();

            Assert.False(result.Success);
            Assert.StartsWith("Download failed", result.Message, StringComparison.Ordinal);
            Assert.Equal(2, handler.Requests.Count);
            Assert.Empty(Directory.GetFiles(folder, "*.download", SearchOption.AllDirectories));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public void DownloadAsync_MissingChecksumAssetIdFailsClosed()
    {
        var check = CheckWithAssets(sumsId: 0);
        var (service, folder, handler) = NewService(request => Status(HttpStatusCode.ServiceUnavailable));

        try
        {
            var result = service.DownloadAsync(check, UpdateArchitecture.X64).GetAwaiter().GetResult();

            Assert.False(result.Success);
            Assert.Equal(1, handler.Requests.Count);
            Assert.Empty(Directory.GetFiles(folder, "*.download", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(folder, "*.msix", SearchOption.AllDirectories));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public void DownloadAsync_TamperedMsixFailsClosed()
    {
        var check = CheckWithAssets();
        var wrongSums = Encoding.UTF8.GetBytes(new string('0', 64) + "  " + MsixName + Environment.NewLine);
        var (service, folder, handler) = NewService(request =>
            request.RequestUri!.AbsolutePath.Contains("SHA256SUMS.txt", StringComparison.Ordinal)
                ? Ok(wrongSums)
                : Ok(MsixBytes));

        try
        {
            var result = service.DownloadAsync(check, UpdateArchitecture.X64).GetAwaiter().GetResult();

            Assert.False(result.Success);
            Assert.Contains("SHA256 verification failed", result.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(folder, "*.download", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(folder, "*.msix", SearchOption.AllDirectories));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public void DownloadAsync_TraversalAssetNameStaysInsideUpdatesFolder()
    {
        const string hostileName = "..\\..\\evil_x64.msix";
        var check = CheckWithAssets(msixName: hostileName);
        var (service, folder, handler) = NewService(request =>
            request.RequestUri!.AbsolutePath.Contains("SHA256SUMS.txt", StringComparison.Ordinal)
                ? Ok(SumsBytes(hostileName))
                : Ok(MsixBytes));

        try
        {
            var result = service.DownloadAsync(check, UpdateArchitecture.X64).GetAwaiter().GetResult();

            Assert.True(result.Success, result.Message);
            Assert.Equal("evil_x64.msix", Path.GetFileName(result.MsixPath));
            Assert.StartsWith(Path.GetFullPath(folder), Path.GetFullPath(result.MsixPath!), StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(folder, "evil_x64.msix")));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public void DownloadAsync_TimeoutFallsBackToApi()
    {
        var check = CheckWithAssets();
        var (service, folder, handler) = NewService(request =>
        {
            if (request.RequestUri!.Host == "github.com")
            {
                throw new TaskCanceledException("simulated HttpClient timeout");
            }

            return request.RequestUri.AbsolutePath.Contains("/assets/7", StringComparison.Ordinal)
                ? Ok(SumsBytes(MsixName))
                : Ok(MsixBytes);
        });

        try
        {
            var result = service.DownloadAsync(check, UpdateArchitecture.X64).GetAwaiter().GetResult();

            Assert.True(result.Success, result.Message);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public void DownloadAsync_MidStreamIOExceptionFallsBackToApi()
    {
        var check = CheckWithAssets();
        var (service, folder, handler) = NewService(request =>
        {
            if (request.RequestUri!.Host == "github.com")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new FailingReadContent() };
            }

            return request.RequestUri.AbsolutePath.Contains("/assets/7", StringComparison.Ordinal)
                ? Ok(SumsBytes(MsixName))
                : Ok(MsixBytes);
        });

        try
        {
            var result = service.DownloadAsync(check, UpdateArchitecture.X64).GetAwaiter().GetResult();

            Assert.True(result.Success, result.Message);
            Assert.Contains(handler.Requests, request => request.RequestUri!.Host == "api.github.com");
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public void DownloadAsync_PreCancelledTokenNeverFallsBackToApi()
    {
        var check = CheckWithAssets();
        var (service, folder, handler) = NewService(_ => Status(HttpStatusCode.OK));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            var result = service.DownloadAsync(check, UpdateArchitecture.X64, cancellation.Token)
                .GetAwaiter().GetResult();

            Assert.False(result.Success);
            Assert.DoesNotContain(handler.Requests, request => request.RequestUri!.Host == "api.github.com");
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    private sealed class FailingReadContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            throw new IOException("simulated mid-stream failure");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new FailingReadStream());
    }

    private sealed class FailingReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("simulated mid-stream failure");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("simulated mid-stream failure"));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void DownloadAsync_PreCancelledTokenReturnsFailure()
    {
        var check = CheckWithAssets();
        var (service, folder, _) = NewService(_ => Status(HttpStatusCode.OK));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            var result = service.DownloadAsync(check, UpdateArchitecture.X64, cancellation.Token)
                .GetAwaiter().GetResult();

            Assert.False(result.Success);
            Assert.Empty(Directory.GetFiles(folder, "*.download", SearchOption.AllDirectories));
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    private const string MsixName = "ItsycalForWindows_1.2.1.0_x64.msix";

    private static readonly byte[] MsixBytes = Encoding.UTF8.GetBytes("fake-msix-content");

    private static UpdateCheckResult CheckWithAssets(
        long sumsId = 7,
        long msixId = 11,
        string msixName = MsixName)
    {
        var sumsUri = new Uri("https://github.com/Folieson/itsycal-for-windows/releases/download/v1.2.1/SHA256SUMS.txt");
        var msixUri = new Uri("https://github.com/Folieson/itsycal-for-windows/releases/download/v1.2.1/evil_x64.msix");
        var assets = new ReleaseAsset[]
        {
            new("SHA256SUMS.txt", sumsUri, sumsId),
            new(msixName, msixUri, msixId),
        };
        var release = new GitHubRelease(
            "v1.2.1",
            Draft: false,
            Prerelease: true,
            PublishedAt: null,
            ReleaseNotes: string.Empty,
            PageUri: null,
            Assets: assets);
        return UpdateCheckResult.Available(Version(1, 2, 0), Version(1, 2, 1), release);
    }

    private static byte[] SumsBytes(string msixName) =>
        Encoding.UTF8.GetBytes(
            Convert.ToHexString(SHA256.HashData(MsixBytes)).ToLowerInvariant()
            + "  " + Path.GetFileName(msixName) + Environment.NewLine);

    private static HttpResponseMessage Ok(byte[] content) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    private static HttpResponseMessage Status(HttpStatusCode statusCode) => new(statusCode);

    private static (UpdateService Service, string Folder, StubHttpMessageHandler Handler) NewService(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var folder = Path.Combine(Path.GetTempPath(), "itsycal-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var handler = new StubHttpMessageHandler(respond);
        return (new UpdateService(new UpdateSettings(), handler, folder), folder, handler);
    }

    private static void DeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch
        {
            // Best effort test cleanup.
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }

    private static SemanticVersion Version(int major, int minor, int patch) => new(major, minor, patch);

    private static ReleaseAsset Asset(string name) =>
        new(name, new Uri($"https://example.test/{name}", UriKind.Absolute));

    private static GitHubRelease Release(string tag, bool draft = false, bool prerelease = false, params ReleaseAsset[] assets) =>
        new(tag, draft, prerelease, null, string.Empty, null, assets);
}
