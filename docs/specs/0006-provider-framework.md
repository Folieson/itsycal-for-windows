# 0006 — Calendar provider framework and read-only ICS subscriptions

Status: proposed — revision 1 (2026-11-08). The framework and the ICS subscription
provider are implemented on branch `feat/provider-framework`; an independent spec review
and CI are still required before acceptance.
Backlog: [PRV-301](../BACKLOG.md) (provider platform). This is the first slice only;
OAuth/Graph/CalDAV transports and provider settings UI are explicitly out of scope.
Related: [ADR 0002](../adr/0002-calendar-providers-and-network.md) (network/privacy
policy), [ADR 0003](../adr/0003-surfaces-and-host-strategy.md) (shared core),
[ADR 0004](../adr/0004-rebrand-independent-product.md) (Calwren identity).
Specs [0003](0003-compact-calendar-ui.md)/[0004](0004-standalone-compact-flyout.md) own
the calendar surfaces; this spec owns only the provider boundary below them.

## Context

The PO gated Microsoft Store distribution on external calendar providers. A Calwren
user's calendar is usually in Google/Gmail, Microsoft Graph (Outlook/M365/Exchange
Online), Yandex Calendar or a CalDAV server (iCloud/Fastmail/Nextcloud); exported `.ics`
files cover the rest. The current Windows product reads only the Windows appointment
store and the local `%LOCALAPPDATA%\ItsycalForWindows\calendars` folder, so it cannot
answer "what is on my calendar" on a clean Windows 11 install.

ADR 0002 permits network access only for features the user explicitly enables, requires
provider tokens to be DPAPI-encrypted and never logged, and forbids telemetry. The Core
`IEventSource` boundary already normalizes events for the UI. This slice adds the
provider layer *below* that boundary — account metadata, secret storage, provider
contracts, a sync service and the first real provider — without OAuth network calls or a
settings UI.

The four OAuth/CalDAV providers are deliberately registered skeletons. They return
`ProviderStatus.NotConfigured`/`NotImplemented` instead of invented events, so a settings
surface or a sync attempt gets an honest state while the transports are built next.

## Scope

In scope: the `ItsycalForWindows.Providers` library (accounts, DPAPI secrets, provider
contracts, registry, sync service, skeletons), the read-only ICS subscription provider
over HTTP(S) and local `.ics`, unit tests, the solution wiring, and this spec.

Out of scope: OAuth authorization flows, Google Calendar/Graph/Yandex/CalDAV network
implementations, token refresh, provider settings UI, persistent offline cache, event
writes, Windows appointment store changes, flyout/CmdPal UI changes, localization,
telemetry, and Store distribution.

## Acceptance criteria

- [ ] **AC-1 — project and solution:** `ItsycalForWindows.Providers` builds as a
  `net10.0-windows10.0.26100.0` class library for x64/ARM64, references only Core plus
  the BCL DPAPI package, has no UI/packaging/third-party auth dependency, is listed in
  `windows/ItsycalForWindows.sln`, and is referenced by the app and test projects.
- [ ] **AC-2 — account metadata:** `ProviderKind` covers Google, Microsoft Graph,
  Yandex CalDAV, generic CalDAV and ICS subscription. `ProviderAccount` has a stable
  `Id`, kind, display name, optional account name/remote id, enabled flag and
  created/last-sync timestamps, and no token/password property.
- [ ] **AC-3 — account/secret store:** `IAccountStore`/`DpapiAccountStore` list, add,
  find, remove, enable/disable and record last sync. Accounts are `accounts.json`;
  secrets are a separate `secrets.dat` encrypted with `ProtectedData` (CurrentUser plus
  application entropy). Writes are atomic (temp file + replace), the directory and files
  get a protected current-user-only ACL, and corrupt/truncated files read as empty
  instead of throwing. `IProviderSecretStore` exposes save/try-get/remove; an empty
  secret removes the entry, and removing an account removes its secret.
- [ ] **AC-4 — provider contract and registry:** `ICalendarProvider` exposes kind,
  display name, `ProbeAsync` and `GetEventsAsync(account, range, ct)`. Probe returns an
  explicit `ProviderStatus` (state, capabilities, safe message) and does no network I/O;
  fetch returns normalized `ProviderEvent`s plus a status; expected failures are statuses,
  not exceptions, while caller cancellation propagates. `ProviderEventRange` keeps the
  Core half-open `[from, to)` contract. `IProviderFactory`/`ProviderRegistry` resolve by
  kind, reject duplicates, wrong-kind providers and unknown kinds deterministically.
- [ ] **AC-5 — sync service:** per-account refresh records last attempt/success and
  last error, updates `MarkSynced` only after a successful provider fetch, isolates one
  provider's failure from the others, honors caller cancellation, makes duplicate
  concurrent refreshes of the same account return `AlreadyRunning` without a second
  request, skips disabled accounts, and uses an optional cache hook before probing.
- [ ] **AC-6 — skeleton providers:** Google, Microsoft Graph, Yandex CalDAV and generic
  CalDAV return explicit `NotConfigured`/`NotImplemented` statuses from both probe and
  fetch, return no fabricated events and make no network request.
- [ ] **AC-7 — ICS subscription over HTTP(S):** fetch with an injected `HttpClient`
  (tests use a fake handler), conditional `ETag`/`If-Modified-Since` requests, 304
  handling with the last good body re-parsed for the requested range, bounded redirects,
  a per-attempt timeout, a response-size cap, and no request for an empty range.
  Non-ICS content, oversized bodies, timeouts, non-success statuses and too many
  redirects return explicit non-ready statuses with no events. Only absolute http(s)
  URLs are accepted; URLs with embedded user-info are rejected without a request.
- [ ] **AC-8 — ICS subscription over a local file:** a configured absolute `.ics` path
  (or `file:` URI) is read within the same size cap, parsed with the existing
  `IcsParser`, filtered to the requested range, and returns `Unavailable`/`NotConfigured`
  for missing files without throwing.
- [ ] **AC-9 — privacy and security:** no telemetry, no event-content logging, no token
  or password in `ToString`, account JSON or error text; logs/errors never include a full
  RemoteId or feed body. The ICS provider accepts only http(s), rejects URL credentials,
  caps redirects/time/size, and treats the user-supplied feed address as the trust
  boundary (private/loopback addresses remain allowed for local feeds). Event content is
  never sent anywhere except to the provider the user configured.
- [ ] **AC-10 — tests and spec:** the test project references the Providers project and
  all new behavior is covered by offline unit tests; the spec maps each AC to tests and
  records that independent review is pending. CI remains the merge gate.

## Test plan

| AC | Test (file / name) | Kind |
|---|---|---|
| AC-1 | `ProviderRegistryTests.Catalog_RegistersEveryProviderKind`; solution and project-reference review | unit + review |
| AC-2 | `ProviderAccountTests.Create_AssignsStableIdAndTrimsMetadata`, `Create_WithExplicitIdKeepsThatId`, `Serialization_HasNoSecretFieldsOrValues`, `ToString_ContainsOnlyPublicMetadata`, `ToString_RedactsQuerySecretsInRemoteUrls` | unit |
| AC-3 | `DpapiAccountStoreTests.Add_List_Find_Remove_RoundTripsAccounts`, `Add_RejectsDuplicateIdAndInvalidKind`, `SetEnabledAndMarkSynced_PersistAcrossInstances`, `Secrets_AreEncryptedAtRestAndRoundTrip`, `Secrets_AreSeparateFromTheAccountFile`, `EmptySecretAndExplicitSecretRemove_ClearTheEntry`, `RemoveAccount_AlsoRemovesItsSecret`, `CorruptAccountsFile_ReadsAsEmptyAndCanBeRecovered`, `TruncatedAccountsFile_ReadsAsEmpty`, `CorruptSecretsFile_ReadsAsNoSecretAndCanBeReplaced`, `MissingFiles_ReadAsEmpty`, `Writes_LeaveNoTemporaryFiles`, `AccountsFile_HasAProtectedUserOnlyAcl`; `ProviderAccountTests.Secret_ToStringRedactsEverySecretValue`, `Secret_IsEmptyRecognizesEveryEmptyShape` | unit |
| AC-4 | `ProviderAccountTests.ProviderEventRange_IsHalfOpenAndFiltersOverlaps`, `ProviderStatus_FactoriesAreExplicit`, `ProviderEventResult_ProjectsToCoreEvents`; `ProviderRegistryTests.TryGet_ResolvesRegisteredKind`, `UnknownKind_ReturnsFalseAndThrowsOnGet`, `DuplicateKind_IsRejected`, `WrongKindFactory_IsRejected` | unit |
| AC-5 | `ProviderSyncServiceTests.RefreshAsync_SyncsEveryAccountAndIsolatesFailures`, `SyncAccountAsync_UsesCacheAndDoesNotCallProviderAgain`, `SyncAccountAsync_DuplicateCallReturnsAlreadyRunning`, `SyncAccountAsync_ProbeNotConfiguredDoesNotFetch`, `SyncAccountAsync_DisabledAccountIsSkipped`, `SyncAccountAsync_UnknownKindFailsWithoutThrowing`, `SyncAccountAsync_UpdatesLastSyncOnlyAfterSuccess`, `SyncAccountAsync_FailureDoesNotUpdateLastSync`, `SyncAccountAsync_PreCanceledTokenThrows`, `SyncAccountAsync_CancellationDuringProviderPropagates`, `SyncAccountAsync_EmptyRangeSkipsTheProvider`, `RefreshAsync_PreCanceledTokenThrows` | unit |
| AC-6 | `SkeletonProviderTests.SkeletonWithoutConfiguration_ReturnsNotConfigured`, `SkeletonWithAddress_ReturnsNotImplemented`, `SkeletonGetEvents_ReturnsEmptyAndExplicitStatus`, `Skeleton_ProbeHonorsCancellation` (all four kinds) | unit |
| AC-7 | `IcsSubscriptionProviderTests.GetEventsAsync_Http200ParsesEvents`, `Http304ReturnsCachedEventsAndSendsValidators`, `FollowsRedirectAndParsesTheTarget`, `TooManyRedirectsIsInvalidResponse`, `OversizedResponseIsRejected`, `NonIcsResponseIsRejected`, `MalformedIcsReturnsNoEventsWithoutThrowing`, `TimeoutIsReportedWithoutThrowing`, `CredentialsInUrlAreRejectedWithoutARequest`, `EmptyRangeReturnsEmptyWithoutRequest`, `PreCanceledTokenThrows`, `ProbeAsync_ValidHttpUrlIsReadyWithoutNetwork`, `ProviderHttpOptions_RejectsInvalidLimits`, `ProviderHttpOptions_DefaultsAreValid` | unit |
| AC-8 | `IcsSubscriptionProviderTests.GetEventsAsync_LocalFileParsesAndFiltersTheRange`, `MissingLocalFileIsUnavailable`, `ProbeAsync_MissingLocalFileIsNotConfigured` | unit |
| AC-9 | `DpapiAccountStoreTests.Secrets_AreEncryptedAtRestAndRoundTrip` (raw file scan), `ProviderAccountTests.*Secret*`, `IcsSubscriptionProviderTests.CredentialsInUrlAreRejectedWithoutARequest`; static review: no telemetry/logging of content or URLs | unit + review |
| AC-10 | `dotnet build` and `dotnet test` commands in the PR; this spec | review |

Test count target: at least 20 new provider facts; the implementation currently adds
73 provider test cases across the files above. Tests are offline; no live OAuth, Graph,
Yandex or CalDAV network call is made.

## Privacy and security

- **Secrets:** DPAPI `CurrentUser` + `ItsycalForWindows.ProviderSecrets.v1` entropy;
  `secrets.dat` is separate from `accounts.json`; atomic replace; protected current-user
  ACL. No token/password in account JSON, `ToString`, logs, errors or URLs.
- **Network:** only providers the user configures; no telemetry/analytics/crash reporting;
  no event content leaves the machine except to the configured provider. HTTP(S) only;
  user-info in URLs rejected; manual redirect cap; per-attempt timeout; 2 MiB response cap
  (configurable, validated); no event/detail/body logging.
- **ICS trust boundary:** the feed URL is user configuration, so loopback/private hosts
  are allowed for local feeds; the provider does not read arbitrary paths beyond the
  configured `.ics` file. A malformed/non-ICS response is an explicit invalid state. A
  private feed URL can embed a token in its query string; it is treated as configuration,
  stored only in the protected user-only `accounts.json`, never logged, and
  `ProviderAccount.ToString` reduces it to scheme/host/path. OAuth/CalDAV credentials stay
  in the encrypted `secrets.dat`.
- **Scope of logging:** statuses and exception type names only; RemoteId is redacted to
  scheme/host/path in `ProviderAccount.ToString`.
- **OAuth follow-up:** PKCE + loopback redirect, state/nonce checks, token refresh and
  revocation must get their own threat-model review before a real provider ships.

## Out of scope / follow-ups

OAuth (Google PKCE, Graph `Calendars.Read`), Yandex/generic CalDAV transports, on-prem
EWS, token refresh/revocation UI, provider settings and per-source status UI, persistent
offline event cache, background sync scheduling, event writes, multi-calendar discovery,
localization, and a Store listing. Each is a follow-up slice; the skeletons and contracts
are the extension points.

## Review

- Spec reviewer: pending (independent of the author)
- Verdict: pending
- Date: —
- Blocking findings resolved: —
