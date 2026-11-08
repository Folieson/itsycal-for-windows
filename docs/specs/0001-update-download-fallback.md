# 0001 — Robust update download via the GitHub API asset endpoint

## Context

`UpdateService.DownloadAsync` downloads release assets from the `browser_download_url`
returned by the GitHub Releases API (a `github.com/.../releases/download/...` URL). On some
networks and proxies that host is blocked or intermittently returns 5xx. During the v1.2
self-update verification on the development machine, every direct `github.com` asset request
returned **HTTP 504**, while the same asset downloaded through `gh` and the GitHub API worked.
The installer half was verified with a manually staged, SHA256-checked MSIX, but users on
such networks would never receive the update automatically.

GitHub exposes the same release assets through
`https://api.github.com/repos/{owner}/{repo}/releases/assets/{asset_id}` with
`Accept: application/octet-stream`, which proxies and API clients handle better. The release
JSON already contains the numeric `id` for every asset; the updater currently drops it.

## Acceptance criteria

- [ ] AC-1: `BuildApiAssetUri(assetId)` returns
      `https://api.github.com/repos/Folieson/itsycal-for-windows/releases/assets/{assetId}`.
- [ ] AC-2: `ShouldRetryViaApi` returns true for transport failures (no status), 5xx,
      429 and 403 responses; false for 2xx/404/other client errors.
- [ ] AC-3: `ParseReleases` reads the numeric `id` of each asset into `ReleaseAsset.AssetId`,
      and an asset without an id keeps `AssetId == 0`.
- [ ] AC-4: `DownloadAsync` first tries the browser download URL; when it fails with a
      retryable failure and an asset id is available, it downloads the same asset through the
      API endpoint with `Accept: application/octet-stream` and still verifies SHA256 against
      `SHA256SUMS.txt` before using the file. A failed API retry returns a normal failed
      result (never throws).
- [ ] AC-5: Existing behaviour is unchanged when the browser download succeeds (single
      request, same paths, same messages).

## Test plan (TDD order)

| AC | Test | Kind |
|---|---|---|
| AC-1 | `UpdateLogicTests.BuildApiAssetUri_UsesRepositoryAndAssetId` | unit |
| AC-2 | `UpdateLogicTests.ShouldRetryViaApi_MatchesRetryableFailures` | unit theory |
| AC-3 | `UpdateLogicTests.ParseReleases_ReadsAssetIds` | unit |
| AC-4 | `UpdateLogicTests.DownloadFallback_DecisionUsesAssetIdAndStatus` (pure decision helper) + manual update run on a network where github.com is blocked | unit + manual |
| AC-5 | existing `UpdateLogicTests` download/asset-selection tests stay green | unit |

A true network fallback cannot be exercised in unit tests (no HTTP mock in the current test
setup); the decision logic is extracted into pure helpers so it is testable, and the manual
run is recorded in the checkpoint note.

## Out of scope

- Authenticated GitHub API calls / tokens.
- Proxy configuration UI.
- Retrying checksum downloads through the API when the asset id is missing (no fallback data).
