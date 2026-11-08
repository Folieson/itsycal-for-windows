# 0006 — Calendar provider framework and read-only ICS subscriptions

Status: proposed — revision 2 (2026-11-08). Revision 1 was reviewed independently and
received "request changes" for the secret-at-rest design, the missing secret-delivery path,
the transport policy and spec/ADR reconciliation. Revision 2 fixes those blocking findings
and the majors; CI is green (run 42506197925, head `4cd4f18`: 414/414 tests, 52.7% total
coverage, Providers 87.70%/84.12%, ARM64 step 0/0). A second independent spec review still
requires the ADR/backlog/evidence reconciliation recorded below; no approval is claimed until
that re-review completes.
Backlog: [PRV-300](../BACKLOG.md) (provider epic: registry, shared contracts and
project skeleton), with slices of PRV-305 (ICS subscriptions), PRV-307 (secret storage) and
PRV-308 (sync/cache). This is the first slice only; OAuth/Graph/CalDAV transports and the
provider settings UI are explicitly out of scope.
Related: [ADR 0005](../adr/0005-external-calendar-providers-and-store-gate.md) (provider
epic, Store gate, security/network policy — the authority this spec conforms to),
[ADR 0002](../adr/0002-calendar-providers-and-network.md) (network/privacy baseline),
[ADR 0003](../adr/0003-surfaces-and-host-strategy.md) (shared core),
[ADR 0004](../adr/0004-rebrand-independent-product.md) (Calwren identity).
Specs [0003](0003-compact-calendar-ui.md)/[0004](0004-standalone-compact-flyout.md) own
the calendar surfaces; this spec owns only the provider boundary below them.

Revision 2 changes:

- token-bearing ICS addresses moved out of `accounts.json` into a per-account DPAPI secret;
  `ProviderAccount.RemoteId` keeps only a display-safe origin and `ToString` is redacted;
- `ProviderSecret.Resource` + a `ProviderRequest` context with the resolved secret; the sync
  service delivers it and `IProviderSecretStore.GetAsync` exists;
- HTTPS-only transport policy with a private/self-hosted opt-in, link-local/cloud-metadata
  blocking, at most three redirects validated per hop, one total timeout budget and
  automatic redirection disabled/failed closed;
- ICS feed cache keyed by `(accountId, address)`, saved-address invalidation and a
  non-ready 304-without-cache state;
- in-memory cache TTL, forced refresh and invalidation on disable/remove/address change;
- cross-process store lock, per-account DPAPI records, corrupt-file quarantine, secret-first
  removal and protected-DACL assertions;
- thin `IEventSource` adapter, unused app `ProjectReference` removed, coverage include for
  the provider assembly, ARM64 CI/local build coverage, THIRD-PARTY notice completed;
- spec/ADR 0005 reconciliation (storage paths, OAuth storage split, encrypted-cache intent)
  and an updated privacy/trust-boundary section.

## Context

The PO gated Microsoft Store distribution on external calendar providers. A Calwren
user's calendar is usually in Google/Gmail, Microsoft Graph (Outlook/M365/Exchange
Online), Yandex Calendar or a CalDAV server (iCloud/Fastmail/Nextcloud); exported `.ics`
files cover the rest. The current Windows product reads only the Windows appointment
store and the local `%LOCALAPPDATA%\ItsycalForWindows\calendars` folder, so it cannot
answer "what is on my calendar" on a clean Windows 11 install.

ADR 0005 permits network access only for features the user explicitly enables, requires
provider tokens and tokenized feed URLs to be DPAPI-protected and never logged, and
forbids telemetry. The Core `IEventSource` boundary already normalizes events for the UI.
This slice adds the provider layer *below* that boundary — account metadata, secret
storage, provider contracts, a sync service and the first real provider — without OAuth
network calls or a settings UI.

The four OAuth/CalDAV providers are deliberately registered skeletons. They return
`ProviderStatus.NotConfigured`/`NotImplemented` instead of invented events, so a settings
surface or a sync attempt gets an honest state while the transports are built next.

## Scope

In scope: the `ItsycalForWindows.Providers` library (accounts, per-account DPAPI secrets,
provider contracts, registry, sync service, in-memory cache, `IEventSource` adapter,
skeletons), the read-only ICS subscription provider over HTTPS and local `.ics`, unit
tests, the solution wiring, the coverage include, the ARM64 CI build, and this spec.

Out of scope: OAuth authorization flows, Google Calendar/Graph/Yandex/CalDAV network
implementations, Credential Locker refresh-token storage, token refresh, provider settings
UI, the encrypted on-disk event cache, event writes, Windows appointment store changes,
flyout/CmdPal UI wiring and composition, localization, telemetry, and Store distribution.

## Decisions

### Account metadata vs. secrets

- `ProviderAccount` is the non-secret record persisted in
  `%LOCALAPPDATA%\ItsycalForWindows\accounts\accounts.json`. For an ICS subscription its
  `RemoteId` is only `scheme://host[:port]` (or `file` for a local file); the complete feed
  address, including a token-bearing path/query, is `ProviderSecret.Resource` and is never
  written to the account file.
- `ProviderAccount.CreateIcsSubscription` returns the metadata account plus the secret;
  `ProviderAccount.Create`/`DpapiAccountStore.Add` reject a tokenized ICS `RemoteId` so the
  unsafe state cannot be persisted by accident. `ProviderAccount.ToString` prints only
  `Id`, `Kind`, `Enabled` and the display-safe host — no display name, sign-in name, path,
  query or token.
- `ProviderSecret` carries `AccessToken`, `RefreshToken`, `ExpiresAt`, `Password` and
  `Resource`; `ToString` redacts every value. `ProviderEvent`, `ProviderEventResult`,
  `ProviderSyncOutcome`, `ProviderSyncResult` and `ProviderRequest` have count/host-only
  `ToString` overrides that never render event content.
- `IProviderSecretStore` exposes `TryGet`, `GetAsync`, `Save` and `Remove`. The sync service
  resolves the secret with `GetAsync` and passes it through `ProviderRequest` to
  `ICalendarProvider.ProbeAsync`/`GetEventsAsync`; providers never read the store and a
  missing secret is delivered as `null` (the provider reports `NotConfigured`).

### Secret storage and store safety

- `secrets.dat` lives next to `accounts.json` and contains one DPAPI
  (`ProtectedData.Protect`, `CurrentUser`) record per account. Entropy is a SHA-256 digest
  of the versioned application entropy plus the account id, so records are independently
  protected and one damaged record does not hide the others.
- Writes are atomic (temp file + `File.Move(overwrite)`); the account directory and both
  files carry a protected current-user-only ACL.
- Every operation is serialized by an in-process lock **and** a cross-process named mutex
  (`Local\ItsycalForWindows.ProviderAccounts.<directory-hash>`), because the Command
  Palette extension and the standalone flyout can run at the same time.
- A corrupt/truncated accounts or secrets file reads as empty for the caller, but before
  the next overwrite it is moved aside as `<name>.corrupt-<UTC>-<token>.bak`. A hand-edited
  account with a token-bearing ICS remote id is redacted on read.
- Account removal deletes the secret first, then the account metadata, so a crash cannot
  leave an orphaned secret; callers that disable/remove an account also call
  `ProviderSyncService.InvalidateAccount`.

### Sync and cache

- `ProviderSyncService` isolates failures per account, resolves the secret before probing,
  skips disabled accounts and reports an empty range as `Skipped` without calling the
  provider and without updating `LastSynced`.
- `MarkSynced` is called only after a successful provider fetch (fresh or 304-backed), never
  for cached/skipped/failed outcomes.
- `InMemoryProviderCache` expires entries after five minutes (ADR 0005 §5.4), caps its entry
  count and never writes event content to disk.
- `RefreshAsync(range, forceRefresh: true)` / `SyncAccountAsync(..., forceRefresh: true)`
  bypass the event cache and the provider's validators (the manual "refresh now" path). A
  changed enabled flag/kind/remote id/secret resource invalidates both caches; disabling or
  removing an account invalidates them explicitly.
- The ICS provider keeps `ETag`/`Last-Modified` and the last body in memory, keyed by
  `(accountId, SHA-256(address))`. A changed address drops the previous entry and its
  validators. HTTP 304 with no cached body is `InvalidResponse`, not `Ready([])`.

### Transport policy (ADR 0005 §4.5/§4.6)

- HTTPS only; plain HTTP and other schemes are rejected before a request. URLs with
  embedded user-info are rejected.
- Redirects are followed manually, at most three, and every hop is re-validated (scheme,
  credentials, DNS and address ranges). A redirect downgrade to HTTP or to a blocked range
  fails closed.
- One timeout budget covers every redirect hop and the response-body read; the budget is
  never reset per hop.
- `IcsSubscriptionProvider` wraps the supplied handler in an `HttpClient` with automatic
  redirects disabled for `HttpClientHandler`/`SocketsHttpHandler`; if a custom handler
  redirects behind its back, the response's `RequestMessage` mismatch is detected and
  rejected.
- Link-local (`169.254.0.0/16`, `fe80::/10`), cloud-metadata addresses (including
  `169.254.169.254`, `fd00:ec2::254`, `100.100.100.200`, `192.0.0.192`), unspecified,
  broadcast and multicast destinations are always blocked, including IPv4-mapped IPv6
  forms. Loopback/RFC 1918/CGNAT/IPv6-ULA hosts require the explicit
  `ProviderHttpOptions.AllowPrivateHosts` self-hosted opt-in; the probe then returns a
  "only add a feed or server you trust" warning in the status message.
- Local file mode accepts only an absolute rooted `.ics` path or a `file:` URI with an
  empty/`localhost` host; relative paths are rejected.

### Encrypted disk cache (PO direction, not implemented here)

The PO direction for the next provider slice is an **encrypted on-disk event cache** so a
calendar survives a restart while offline. This slice deliberately keeps only the
in-memory `IProviderCache` as a placeholder and writes no event content to disk. Delivering
the encrypted cache requires its own privacy/threat-model update (ADR 0005 Q7: key
management, retention, delete/clear, backup and same-user malware residual risk) and is
tracked under PRV-308; it is not silently implied by this framework.

### ADR 0005 reconciliation

- **Redirect cap / HTTPS / private-host policy:** aligned with ADR 0005 §4.5/§4.6 as
  described above.
- **OAuth storage:** ADR 0005 §4.2 splits Google/Microsoft refresh tokens (Credential
  Locker) from CalDAV app passwords and tokenized ICS URLs (DPAPI). This slice has no OAuth
  transport yet, so all secrets use the DPAPI record; the Credential Locker path and its
  20-entry limit remain PRV-307 scope.
- **Storage paths:** ADR 0005 §4.2 names account metadata in `settings.json` and secrets
  under `%LOCALAPPDATA%\ItsycalForWindows\providers\`. This slice keeps the existing
  `%LOCALAPPDATA%\ItsycalForWindows\accounts\` folder with `accounts.json` +
  `secrets.dat` (one per-account DPAPI record inside the file). The final settings/provider
  layout is deferred to PRV-306/PRV-307; until then this spec is the implemented layout and
  the deviation is explicit, not accidental.
- **Cache:** ADR 0005 §5.4's in-memory TTL/eviction is implemented; the encrypted-cache
  intent is recorded above.

### Integration and surfaces

- `ProviderEventSource` is the thin `IEventSource` adapter: it refreshes all accounts for
  the visible range and returns the merged Core `CalendarEvent` values, returning an empty
  list for provider failures and propagating caller cancellation.
- **Deferred wiring:** neither the Command Palette extension nor the standalone flyout
  references `ItsycalForWindows.Providers` yet. The unused app `ProjectReference` was
  removed (it only added the DLL to the MSIX payload with no code path), and the flyout was
  never referenced. The composition/settings follow-up (PRV-306/PRV-308) adds
  `ProviderEventSource` to the surfaces' composite; that is deliberately not claimed here.

### ARM64

The provider library targets `x64;ARM64`. The Windows CI job now restores and builds the
provider project for ARM64 in addition to the x64 solution build. Local evidence
(2026-11-08):

```powershell
dotnet restore windows/src/ItsycalForWindows.Providers/ItsycalForWindows.Providers.csproj -p:Platform=ARM64
dotnet build windows/src/ItsycalForWindows.Providers/ItsycalForWindows.Providers.csproj -c Debug -p:Platform=ARM64 --no-restore
# -> 0 errors, 0 warnings; output: bin/ARM64/Debug/net10.0-windows10.0.26100.0/ItsycalForWindows.Providers.dll
```

The ARM64 CI step (restore + build of the Providers project) **executed successfully** in the
green CI run 42506197925 on head `4cd4f18` (0 warnings/0 errors); the local command above is
retained for reproducibility. No separate ARM64 test job exists yet.

## Acceptance criteria

- [x] **AC-1 — project and solution:** `ItsycalForWindows.Providers` builds as a
  `net10.0-windows10.0.26100.0` class library for x64/ARM64, references only Core plus the
  BCL DPAPI package, has no UI/packaging/third-party auth dependency, is listed in
  `windows/ItsycalForWindows.sln`, is referenced by the test project and is **not** an
  unused reference of the app.
- [x] **AC-2 — account metadata:** `ProviderKind` covers Google, Microsoft Graph, Yandex
  CalDAV, generic CalDAV and ICS subscription. `ProviderAccount` has a stable `Id`, kind,
  display name, optional account name/remote id, enabled flag and created/last-sync
  timestamps, and no token/password/feed-address property. An ICS `RemoteId` is only the
  display-safe origin (or `file`); a tokenized remote id is rejected. Account `ToString`
  prints only id/kind/enabled/host.
- [x] **AC-3 — account/secret store:** `IAccountStore`/`DpapiAccountStore` list, add, find,
  remove, enable/disable and record last sync. Accounts are `accounts.json`; secrets are a
  separate `secrets.dat` with one DPAPI record per account (`CurrentUser` plus per-account
  entropy). Writes are atomic, directory/files get a protected current-user-only ACL, a
  cross-process named mutex serializes operations, corrupt/truncated files read as empty
  and are quarantined before overwrite, and removal deletes the secret first.
  `IProviderSecretStore` exposes save/try-get/get-async/remove; an empty secret removes the
  entry, and removing an account removes its secret.
- [x] **AC-4 — provider contract and registry:** `ICalendarProvider` exposes kind, display
  name, `ProbeAsync(ProviderRequest)` and `GetEventsAsync(ProviderRequest)`. The request
  carries the account, the resolved `ProviderSecret`, the range and the force-refresh flag.
  Probe returns an explicit `ProviderStatus` and does no network I/O; fetch returns
  normalized `ProviderEvent`s plus a status; expected failures are statuses, not
  exceptions, while caller cancellation propagates. `ProviderEventRange` keeps the Core
  half-open `[from, to)` contract. `IProviderFactory`/`ProviderRegistry` resolve by kind,
  reject duplicates, wrong-kind providers and unknown kinds deterministically.
- [x] **AC-5 — sync service:** per-account refresh records last attempt/success and last
  error, resolves the secret through `IProviderSecretStore.GetAsync` and passes it to the
  provider, updates `MarkSynced` only after a successful provider fetch, isolates one
  provider's failure from the others, honors caller cancellation, makes duplicate
  concurrent refreshes of the same account return `AlreadyRunning` without a second
  request, skips disabled accounts, reports an empty range as `Skipped` without a provider
  call or `MarkSynced`, and uses the optional cache before probing. A forced refresh
  bypasses the cache and every provider validator; a configuration change invalidates
  them.
- [x] **AC-6 — skeleton providers:** Google, Microsoft Graph, Yandex CalDAV and generic
  CalDAV return explicit `NotConfigured`/`NotImplemented` statuses from both probe and
  fetch, return no fabricated events and make no network request.
- [x] **AC-7 — ICS subscription over HTTPS:** fetch with an injected `HttpMessageHandler`
  (tests use a fake handler; the provider disables automatic redirects), conditional
  `ETag`/`If-Modified-Since` requests keyed by account **and** address, 304 handling with
  the last good body re-parsed for the requested range, a non-ready result when 304 arrives
  without a cached body, at most three redirects validated per hop, one total timeout
  budget, a response-size cap (including streaming bodies), and no request for an empty
  range. HTTPS-only; user-info, HTTP, private/loopback without opt-in, link-local/metadata
  and redirect downgrades are rejected without a request. Non-ICS content, oversized
  bodies, timeouts, non-success statuses and too many redirects return explicit non-ready
  statuses with no events. Caller cancellation during the body read propagates as
  `OperationCanceledException`.
- [x] **AC-8 — ICS subscription over a local file:** an absolute rooted `.ics` path (or a
  `file:` URI with no/loopback host) is read within the same size cap, parsed with the
  existing `IcsParser`, filtered to the requested range, and returns
  `Unavailable`/`NotConfigured` for missing files without throwing. Relative paths are
  rejected.
- [x] **AC-9 — privacy and security:** no telemetry, no event-content logging, no token,
  password or feed address in `ToString`, `accounts.json` or error text; all secret
  material is in the DPAPI store. The ICS provider only accepts HTTPS (or the explicit
  local-file mode), rejects URL credentials, caps redirects/time/size, applies the
  private/link-local policy, and treats the user-supplied feed address as a secret trust
  boundary. Event content is never written to disk and never sent anywhere except to the
  configured provider.
- [x] **AC-10 — integration:** a thin `IEventSource` adapter (`ProviderEventSource`) maps
  provider refresh results to Core `CalendarEvent`s with an offline test. UI/surface wiring
  is explicitly deferred and documented above; the app no longer carries an unused provider
  reference.
- [x] **AC-11 — tests, coverage and CI:** the test project references the Providers project
  and all new behavior is covered by offline unit tests; `windows/tests/coverlet.runsettings`
  includes `[ItsycalForWindows.Providers]*`; the coverage floor stays at 30%; the Windows CI
  job builds the provider library for ARM64. This spec maps each AC to tests and records that
  revision 2 is **CI-green** (run 42506197925, head `4cd4f18`: 414/414 tests, total line
  coverage 52.7%, Providers 87.70% line/84.12% branch, ARM64 step 0/0) and that an independent
  spec re-review is pending (revision 1 was request-changes; the previous blockers are
  resolved in the code). The earlier local 53.82%/88.08% numbers are superseded by CI.

## Test plan

`ItsycalForWindows.Tests` contains 159 provider-related test cases (out of 414 total) in the
files below; all run offline with no live provider traffic.

| AC | Test (file / name) | Kind |
|---|---|---|
| AC-1 | `ProviderRegistryTests.Catalog_RegistersEveryProviderKind`; solution/project-reference review; ARM64 local build (above) | unit + build |
| AC-2 | `ProviderAccountTests.Create_AssignsStableIdAndTrimsMetadata`, `Create_WithExplicitIdKeepsThatId`, `Create_IcsSubscriptionWithATokenizedRemoteIdIsRejected`, `CreateIcsSubscription_KeepsOnlyTheOriginInAccountMetadata`, `CreateIcsSubscription_LocalFileKeepsNoPathInMetadata`, `CreateIcsSubscription_RejectsUnsafeOrRelativeAddresses`, `Serialization_HasNoSecretFieldsOrValues`, `ToString_ContainsOnlyIdKindEnabledAndHost`, `ToString_RedactsPathAndQuerySecretsInRemoteUrls`, `ToString_DoesNotPrintLocalPaths` | unit |
| AC-3 | `DpapiAccountStoreTests.Add_List_Find_Remove_RoundTripsAccounts`, `SavedIcsSubscription_ReloadsWithDisplaySafeMetadataAndItsSecret`, `Add_RejectsDuplicateIdInvalidKindAndTokenizedIcsRemoteId`, `SetEnabledAndMarkSynced_PersistAcrossInstances`, `AccountsFile_NeverContainsTheIcsFeedAddressOrToken`, `Secrets_ArePerAccountEncryptedAndRoundTrip`, `GetAsync_ReturnsStoredSecretOrNull`, `EmptySecretAndExplicitSecretRemove_ClearTheEntry`, `RemoveAccount_RemovesTheSecretBeforeTheMetadata`, `CorruptAccountsFile_IsQuarantinedBeforeOverwriteAndCanBeRecovered`, `TruncatedAccountsFile_ReadsAsEmptyAndIsQuarantinedOnWrite`, `CorruptSecretsFile_IsQuarantinedAndCanBeReplaced`, `OneDamagedSecretRecord_DoesNotHideTheOtherAccounts`, `HandWrittenTokenizedIcsRemoteId_IsRedactedOnRead`, `MissingFiles_ReadAsEmpty`, `DefaultDirectory_IsUnderLocalAppData`, `Writes_LeaveNoTemporaryFiles`, `StoreFilesAndDirectory_HaveAProtectedUserOnlyAcl`, `TwoStoreInstances_ConcurrentWrites_DoNotLoseAccounts` | unit |
| AC-4 | `ProviderAccountTests.ProviderEventRange_IsHalfOpenAndFiltersOverlaps`, `ProviderStatus_FactoriesAreExplicit`, `ProviderEventResult_ProjectsToCoreEventsAndRedactsToString`, `ProviderRequest_ToStringRedactsTheSecret`, `ProviderEvent_ToStringRedactsEventContent`, `Secret_ToStringRedactsEverySecretValue`, `Secret_IsEmptyRecognizesEveryEmptyShape`, `SyncResultToStrings_RedactEventContent`; `ProviderRegistryTests.TryGet_ResolvesRegisteredKind`, `UnknownKind_ReturnsFalseAndThrowsOnGet`, `DuplicateKind_IsRejected`, `WrongKindFactory_IsRejected` | unit |
| AC-5 | `ProviderSyncServiceTests.RefreshAsync_SyncsEveryAccountAndIsolatesFailures`, `SyncAccountAsync_UsesCacheAndDoesNotCallProviderAgain`, `CacheEntry_ExpiresAfterTheTtl`, `ForceRefresh_BypassesTheCacheAndProviderValidators`, `ChangedSecret_InvalidatesTheCacheAndTheFeedCache`, `DisabledAccount_IsSkippedAndInvalidatesCaches`, `InvalidateAccount_ClearsEventAndProviderCaches`, `SyncAccountAsync_DuplicateCallReturnsAlreadyRunning`, `SyncAccountAsync_ProbeNotConfiguredDoesNotFetch`, `SyncAccountAsync_UnknownKindFailsWithoutThrowing`, `SyncAccountAsync_DeliversTheResolvedSecretToTheProvider`, `SyncAccountAsync_WithoutStoredSecretPassesNull`, `SyncAccountAsync_UpdatesLastSyncOnlyAfterSuccess`, `SyncAccountAsync_FailureDoesNotUpdateLastSync`, `SyncAccountAsync_PreCanceledTokenThrows`, `SyncAccountAsync_CancellationDuringProviderPropagates`, `SyncAccountAsync_EmptyRangeIsSkippedWithoutMarkSynced`, `RefreshAsync_PreCanceledTokenThrows`, `RefreshAsync_ForceRefreshIsPropagated` | unit |
| AC-6 | `SkeletonProviderTests.SkeletonWithoutConfiguration_ReturnsNotConfigured`, `SkeletonWithAddress_ReturnsNotImplemented`, `SkeletonWithSecret_ReturnsNotImplemented`, `SkeletonGetEvents_ReturnsEmptyAndExplicitStatus`, `Skeleton_ProbeHonorsCancellation` (all four kinds) | unit |
| AC-7 | `IcsSubscriptionProviderTests.GetEventsAsync_Http200ParsesEvents`, `GetEventsAsync_Http304ReturnsCachedEventsAndSendsValidators`, `GetEventsAsync_Http304WithoutCachedBodyIsNotReady`, `GetEventsAsync_FeedCacheIsKeyedByUrlAndDropsChangedAddressValidators`, `GetEventsAsync_ForceRefreshSkipsCachedValidators`, `InvalidateCache_DropsStoredValidators`, `GetEventsAsync_FollowsRedirectAndParsesTheTarget`, `GetEventsAsync_TooManyRedirectsIsInvalidResponse`, `GetEventsAsync_DefaultRedirectCapIsThree`, `GetEventsAsync_RedirectDowngradeToHttpIsRejectedPerHop`, `GetEventsAsync_RedirectToLinkLocalMetadataIsBlocked`, `GetEventsAsync_TotalTimeoutBudgetCoversEveryRedirectHop`, `GetEventsAsync_OversizedResponseIsRejected`, `GetEventsAsync_StreamingResponseCapIsEnforcedWithoutContentLength`, `GetEventsAsync_NonIcsResponseIsRejected`, `GetEventsAsync_MalformedIcsReturnsNoEventsWithoutThrowing`, `GetEventsAsync_NonSuccessStatusIsAnExplicitState`, `GetEventsAsync_TimeoutIsReportedWithoutThrowing`, `GetEventsAsync_CallerCancellationDuringBodyReadPropagates`, `GetEventsAsync_HttpIsRejectedWithoutARequest`, `GetEventsAsync_CredentialsInUrlAreRejectedWithoutARequest`, `GetEventsAsync_LoopbackIsRejectedWithoutExplicitOptIn`, `GetEventsAsync_PrivateDnsResolutionIsRejectedWithoutExplicitOptIn`, `GetEventsAsync_LinkLocalAndMetadataAreBlockedEvenWithPrivateOptIn`, `GetEventsAsync_PrivateHostIsAllowedWithExplicitOptIn`, `Constructor_DisablesAutomaticRedirectsOnHttpClientHandler`, `Constructor_DisablesAutomaticRedirectsOnSocketsHttpHandler`, `GetEventsAsync_DetectsAHiddenAutomaticRedirect`, `GetEventsAsync_EmptyRangeReturnsEmptyWithoutRequest`, `GetEventsAsync_PreCanceledTokenThrows`; `ProviderNetworkPolicyTests.*` | unit |
| AC-8 | `IcsSubscriptionProviderTests.GetEventsAsync_LocalFileParsesAndFiltersTheRange`, `GetEventsAsync_FileUriIsAccepted`, `GetEventsAsync_RelativeLocalPathIsRejected`, `GetEventsAsync_MissingLocalFileIsUnavailable`, `GetEventsAsync_OversizedLocalFileIsRejected`, `ProbeAsync_MissingLocalFileIsNotConfigured` | unit |
| AC-9 | `DpapiAccountStoreTests.AccountsFile_NeverContainsTheIcsFeedAddressOrToken`, `DpapiAccountStoreTests.Secrets_ArePerAccountEncryptedAndRoundTrip`, `ProviderAccountTests.*Secret*`, `ProviderAccountTests.*ToString*`, `IcsSubscriptionProviderTests.GetEventsAsync_CredentialsInUrlAreRejectedWithoutARequest`, `IcsSubscriptionProviderTests.GetEventsAsync_CallerCancellationDuringBodyReadPropagates`; static review: no telemetry/logging of content, URLs or secrets | unit + review |
| AC-10 | `ProviderEventSourceTests.GetEventsAsync_AdaptsProviderEventsToCoreEvents`, `GetEventsAsync_ProviderFailureReturnsEmptyInsteadOfThrowing`, `GetEventsAsync_EmptyRangeSkipsTheProvider`, `ForceRefreshSource_BypassesTheCache`; project-reference review (app reference removed; flyout wiring deferred) | unit + review |
| AC-11 | `dotnet build` / test commands below; `windows/tests/coverlet.runsettings`; ARM64 CI step; this spec | build + review |

## Verification (local, 2026-11-08)

Local toolchain is the .NET 10 SDK at
`C:\Users\ponam\AppData\Local\Microsoft\dotnet` (not on `PATH`); the machine has no Windows
SDK, so the solution build uses the documented workarounds. The test platform's `testhost`
cannot start in this shell (`OpenProcess` access denied while registering the parent-exit
callback), so the suite is executed with the xunit v2 console runner in-process; CI remains
the canonical `dotnet test` gate. The console runner (xunit.runner.console 2.9.3) and
coverlet.console 6.0.4 used below are local copies under the gitignored `.dsh-tmp/`; they
are not repository dependencies.

```powershell
# x64 solution build (0 errors, 0 provider warnings)
dotnet build windows/ItsycalForWindows.sln -c Debug -p:Platform=x64 `
  -p:EnableMsixTooling=false -p:EnableCoreMrtTooling=false `
  -p:CsWinRTWindowsMetadata=C:\Users\ponam\.nuget\packages\microsoft.windows.sdk.net.ref\10.0.26100.68-preview\winmd\Windows.Foundation.FoundationContract.winmd

# ARM64 provider build (0 errors, 0 warnings)
dotnet build windows/src/ItsycalForWindows.Providers/ItsycalForWindows.Providers.csproj -c Debug -p:Platform=ARM64

# Tests (local workaround for the testhost start failure)
dotnet exec <xunit-console-2.9.3>/xunit.console.dll `
  windows/tests/ItsycalForWindows.Tests/bin/x64/Debug/net10.0-windows10.0.26100.0/ItsycalForWindows.Tests.dll -nocolor
# -> Total: 414, Errors: 0, Failed: 0, Skipped: 0

# Coverage over the same run with coverlet.console 6.0.4 (Cobertura), the same filters as
# windows/tests/coverlet.runsettings
# -> ItsycalForWindows.Providers: 88.08% line / 84.15% branch; total line coverage: 53.82%
#    (floor 30%; the pre-slice value was 37.5% because Providers was not counted)
```

CI has not been run for this revision (branch not pushed); the ARM64 step and the
`dotnet test` path are the gates to confirm there. No live provider, DNS, or network
request is made by the tests.

## Privacy and security

- **Secrets at rest:** per-account DPAPI `CurrentUser` records inside `secrets.dat`;
  `accounts.json` holds no token, password or token-bearing feed address; atomic replace;
  protected current-user ACLs; corrupt files quarantined before overwrite; no event content
  on disk.
- **Redaction:** account `ToString` = id/kind/enabled/host only; secret `ToString` = "(redacted)";
  event/result/request `ToString` = counts/identifiers only. Status messages never contain
  the URL, host, token or event content.
- **Secret delivery:** `ProviderSyncService` resolves the secret and passes it only through
  `ProviderRequest`; providers never read the store; a missing secret is `NotConfigured`.
- **Network:** only providers the user configures; no telemetry/analytics/crash reporting;
  no event content leaves the machine except to the configured provider. HTTPS only;
  user-info rejected; HTTP rejected; ≤3 redirects validated per hop; one total timeout
  budget; 2 MiB response cap (configurable, validated); automatic redirection forced off
  and failed closed; no event/detail/body/URL logging.
- **ICS trust boundary:** the feed address is a secret; a published token-in-path URL is
  DPAPI-protected and never logged or shown in full. Loopback/private hosts are allowed
  only with an explicit self-hosted opt-in (API-level `AllowPrivateHosts`; the future UI
  must warn before enabling it). Link-local and cloud-metadata ranges stay blocked even
  then. The provider never reads arbitrary paths beyond the configured local `.ics` file.
- **Residual transport risk:** the policy validates the resolved addresses immediately
  before each request, but the OS resolver performs its own lookup for the connection, so
  DNS rebinding between validation and connect is not fully eliminated. Address pinning in
  the HTTP handler (and the corresponding test) is a follow-up hardening item for the
  independent security review (PRV-311); it is recorded here rather than claimed as solved.
- **Cancellation:** caller cancellation propagates through HTTP headers, every redirect hop,
  the body read and local-file reads; it is never converted into a failure status.
- **OAuth follow-up:** PKCE + loopback redirect, state/nonce checks, Credential Locker
  storage of refresh tokens, token refresh and revocation must get their own threat-model
  review before a real provider ships (PRV-301/302/307/311).

## Deferred / follow-ups

| Item | Why deferred | Tracked as |
|---|---|---|
| Encrypted on-disk event cache (PO decision 2026-11-08) | Needs its own privacy/threat-model update, key management, retention/delete design; ADR 0005 Q7 now records the PO decision | PRV-308 + ADR 0005 Q7 update |
| UI wiring: `ProviderEventSource` in the flyout/CmdPal composite, add-account/settings surface | No UI in this slice; surfaces do not reference the provider library yet | PRV-306 + PRV-308 |
| OAuth adapters (Google/Graph), CalDAV/Yandex transports, Credential Locker token storage | Separate transports and review scope | PRV-301/302/303/304/307 |
| Background scheduling, backoff/jitter, `429 Retry-After` | Not required for the framework slice | PRV-308 |
| Storage layout `settings.json`/`providers\` per ADR 0005 §4.2 | Kept the existing `accounts\` layout in this slice; ADR 0005 now records the amendment and PRV-306/PRV-307 own the migration | PRV-306/PRV-307 |

## Review

- Spec review round 1 (`187985d`): request changes — B1 tokenized URL at rest, B2 spec/ADR
  reconciliation, B3 missing secret delivery; majors on transport/cache/store/tests.
- Revision 2 fixes B1–B3 and the code majors; CI run 42506197925 is green. Spec review
  round 2 returned request changes only for the remaining ADR/backlog/evidence
  reconciliation (ADR 0005 still said implementation not started; deferral pointers).
  Those documents are now reconciled (ADR 0005 implementation amendment/Q7, BACKLOG
  PRV-306/307/308, this spec's evidence); a final independent spec re-review is pending.
- Code review of revision 2 is pending; no approval is claimed by the author.
- Reviewer must be independent of the author; the same agent must not also perform the code
  review.
- Code reviewer: **pending** for the revision-2 head; independent of the author and of the
  spec reviewer.
- CI: **pending** for this revision (not pushed from the fix work).
