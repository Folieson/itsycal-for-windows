# 0001 — Robust update download via the GitHub API asset endpoint

Status: implementation merged in #27; spec revised after an independent spec review
(request changes) in #30.
Related: [ADR 0002](../adr/0002-calendar-providers-and-network.md), PR #27, PR #30.

## Context

`UpdateService.DownloadAsync` downloads release assets from the `browser_download_url`
returned by the GitHub Releases API (a `github.com/.../releases/download/...` URL). On the
development network that URL consistently failed: every direct `github.com` asset request
through the app returned **HTTP 504** through the local proxy. The same release asset was
downloadable through `gh` (authenticated) and the asset bytes are also served by the GitHub
API with `Accept: application/octet-stream`. Before PR #27 the updater dropped the numeric
`id` of each asset from the release JSON, so it had no fallback. A user whose proxy blocks or
fails `github.com` would never receive an automatic update.

GitHub exposes the same assets at
`https://api.github.com/repos/{owner}/{repo}/releases/assets/{asset_id}`. The release JSON
already carries the numeric `id` for every asset. Unauthenticated API calls are limited to
60 requests/hour/IP; this spec does not add tokens, backoff or `Retry-After` handling — the
fallback is a single attempt per asset and a rate-limit response becomes a normal failure.

## Acceptance criteria

- [ ] **AC-1**: `BuildApiAssetUri(assetId)` returns
      `https://api.github.com/repos/Folieson/itsycal-for-windows/releases/assets/{assetId}`.
- [ ] **AC-2**: `ShouldRetryViaApi` returns true for transport failures (no status), 5xx,
      429 and 403; false for 2xx, 400/401/404 and 408 (408 is treated as a client error, not
      retried).
- [ ] **AC-3**: `ParseReleases` reads the numeric `id` of each asset into
      `ReleaseAsset.AssetId`; an asset without an id keeps `AssetId == 0`.
- [ ] **AC-4**: `DownloadAsync` downloads **both** `SHA256SUMS.txt` and the MSIX browser
      first. When a browser download fails with a retryable error (AC-2) and the asset has a
      nonzero id, the same asset is retried exactly once through the API endpoint with
      `Accept: application/octet-stream`. No retry loop is added. A failed fallback returns a
      normal failed result (never throws) and leaves no partial file behind.
- [ ] **AC-5**: When the browser download succeeds, exactly one request per asset is made
      (checksum + MSIX, two requests total), no API request is made, and the existing paths
      and success/failure messages are unchanged.
- [ ] **AC-6**: A checksum asset without an id is never retried through the API; a failed
      checksum download fails the whole update before the MSIX is requested (fail closed).
- [ ] **AC-7**: The MSIX is only used after its SHA256 matches the checksum file; a mismatch
      discards the download and fails the update. The asset name is reduced to a plain file
      name before writing, so an asset name containing path separators cannot write outside
      `%LOCALAPPDATA%\ItsycalForWindows\updates\<tag>\`.
- [ ] **AC-8**: A cancelled or timed-out download returns a failed result (never throws), does
      not attempt the API fallback, and does not leave the temporary file in place.
- [ ] **AC-9**: An API rate-limit or error response (403/429/5xx) after the fallback attempt
      becomes a normal failed result with a user-presentable message; no storm of retries.

## Test plan (TDD order)

| AC | Test | Kind |
|---|---|---|
| AC-1 | `UpdateLogicTests.BuildApiAssetUri_UsesRepositoryAndAssetId` | unit |
| AC-2 | `UpdateLogicTests.ShouldRetryViaApi_MatchesRetryableFailures` (incl. 408/400/401) | unit theory |
| AC-3 | `UpdateLogicTests.ParseReleases_ReadsAssetIds` | unit |
| AC-4 | `UpdateLogicTests.DownloadAsync_FallsBackToApiForChecksumAndMsix` (fake `HttpMessageHandler`, temp data folder: 4 requests in order, API requests carry octet-stream, SHA verified) | unit |
| AC-4/AC-9 | `UpdateLogicTests.DownloadAsync_FailedApiFallbackReturnsFailureWithoutThrowing` | unit |
| AC-5 | `UpdateLogicTests.DownloadAsync_BrowserOnlySendsOneRequestPerAsset` | unit |
| AC-6 | `UpdateLogicTests.DownloadAsync_MissingChecksumAssetIdFailsClosed` | unit |
| AC-7 | `UpdateLogicTests.DownloadAsync_TamperedMsixFailsClosed` + `DownloadAsync_TraversalAssetNameStaysInsideUpdatesFolder` | unit |
| AC-8 | `UpdateLogicTests.DownloadAsync_PreCancelledTokenReturnsFailure` | unit |
| AC-9 | covered by AC-4/AC-2 units; API failure is a normal failed result | unit |
| - | live fallback on a proxy-blocked network (manual record below) | manual |

The HTTP edge is covered with a fake `HttpMessageHandler` injected through the internal
`UpdateService(settings, handler, dataFolder)` constructor; no new package is required.
Manual verification is a deliberate complement, not a substitute for the unit tests.

## Manual verification record (2026-11-08)

- Condition: proxy returns HTTP 504 for direct `github.com` release-asset URLs (observed via
  the app and the probe); `gh` (authenticated) downloads the same asset.
- Procedure: release `v1.2.1` built; probe called `CheckAsync` → `DownloadAsync` (fallback
  path) → `ApplyAsync`. Evidence: `download: success=True
  … ItsycalForWindows_1.2.1.0_x64.msix`; SHA256 verified by the service; updater log:
  `starting update`, `installed`, `restarted Command Palette`; installed version `1.2.1.0`.
- The full chronology is in the harness checkpoint note
  `.agents/notes/proposed/process/2026-11-08-itsycal-for-windows-checkpoint.md` (harness
  repository, not this product repository).

## Out of scope

- Authenticated GitHub API calls/tokens, proxy configuration UI.
- Retry/backoff policy and `Retry-After`/rate-limit reset handling (single fallback attempt
  only; rate-limited results surface as failures).
- Resuming partial downloads, parallel downloads, mirror selection.

## Review

- Spec reviewer (independent, read-only): 2026-11-08, verdict **request changes**.
  Blocking findings addressed in PR #30: test-name/mapping mismatch and missing download-level
  tests (B1/B2), checksum fallback requirement (B3), fail-closed and transport-trigger
  definitions (M1/M2), cancellation (M3), rate limits (M4), asset-name safety (M5), retroactive
  context correction (M6), manual evidence moved into this spec (M7), ADR link and structure
  (m1/m5).
- Code review: requested on the revised implementation PR (#30).
