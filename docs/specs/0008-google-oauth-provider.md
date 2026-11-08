# 0008 - Google Calendar OAuth 2.0 provider (read-only, PKCE loopback)

Status: proposed - revision 2 (2026-11-08). Offline implementation/tests are complete on
`feat/google-oauth`; independent **spec review: approve with notes** and
**code review: approve with notes** are recorded in the PR (comments 6725911278/6725911584).
CI run 42512729287 at head `4eb0c9a` is green: 501/501 tests, line coverage 58.6% (floor
30%), ARM64 build. Live end-to-end verification with a real Google account and a PO-owned
Google Cloud desktop client id/redirect is **not** part of this change; the accepted merge
deferrals are listed below.
Backlog: [PRV-301](../BACKLOG.md) (Google Calendar API v3 + OAuth), with PRV-306 (account/
calendar-selection UX), PRV-307 (token storage), PRV-309 (fixture harness), PRV-311 (security
review). Related: [ADR 0005](../adr/0005-external-calendar-providers-and-store-gate.md)
4.1-4.3/4.5 (authority), [ADR 0002](../adr/0002-calendar-providers-and-network.md), spec
[0006](0006-provider-framework.md) (contracts, sync service, DPAPI secret store).

## Context
Spec 0006 delivered the framework, DPAPI secrets, the sync service and the read-only ICS
provider; Google was a deliberate `NotConfigured`/`NotImplemented` skeleton. Google Calendar is
a P0 Store-gate source, so this slice adds the first real OAuth transport: a reusable
Authorization Code + PKCE (S256) loopback flow and read-only Google Calendar API v3
`events.list`. It is testable offline with a fake HTTP handler and injected browser launcher;
no client id/secret is embedded and tests make no live request. The provider is **not** wired
into the extension or flyout; composition/UI remain PRV-306.

## Scope
In scope: `OAuth/` (PKCE, loopback flow, token client/store, options, browser launcher),
`Google/` (provider + mapping), offline tests, the minimal contract extensions for OAuth scope
and the provider web link, and this spec.
Out of scope: settings/account UI and surface wiring (PRV-306); live verification with a real
client id/account (PO-owned Google Cloud); `syncToken`/`410` incremental sync (PRV-301
follow-up - this slice fetches the requested range fully); calendar list/selection (one
configured calendar id; PRV-306); Graph/Yandex/CalDAV; write/RSVP; encrypted cache (PRV-308);
localization; telemetry; Store submission.

## Decisions

### OAuth security (generic, reusable for Graph)
- **PKCE:** 64 random bytes -> 86-char base64url verifier (RFC 7636 allows 43-128);
  `code_challenge = base64url(SHA-256(ASCII(verifier)))`; 32 random bytes -> 256-bit state.
  State comparison is constant-time and single-use; a mismatch does not consume it.
- **Loopback:** `TcpListener` on `IPAddress.Loopback` (port 0 or a fixed configured port) binds
  **before** the browser; never `0.0.0.0`/`localhost`. Exactly one callback: `GET`, exact
  redirect path, `Host: 127.0.0.1:<port>`, one non-duplicated `state`, then either
  `error`/`access_denied` or one `code`. Request heads are capped at 16 KiB.
- **Browser:** injectable `IOAuthBrowserLauncher`; the default `SystemBrowserLauncher` uses the
  Windows shell (`ProcessStartInfo` + `UseShellExecute`), never an embedded WebView.
- **Request:** `response_type=code`, `client_id`, `redirect_uri`, `scope`, `state`,
  `code_challenge`, `code_challenge_method=S256`; Google adds `access_type=offline` and
  `prompt=consent`. No client secret is sent anywhere; the code is exchanged exactly once.
- **Timeout/cancel:** authorization budget defaults to 5 minutes (configurable <=10); caller
  cancellation propagates and closes the listener on success/error/denial/timeout/cancellation.
- **No logging:** authorization URL, callback query, code, verifier, state, tokens and headers
  are never logged; result `ToString` values are redacted and messages are fixed safe strings.

### Token client and storage
- `OAuthTokenClient` uses an injectable `HttpClient` and a validated HTTPS endpoint, caps the
  response body and rejects redirects. It maps 400 (`invalid_grant`/`invalid_client`/other),
  401/403, 429, 5xx, network failure, timeout and malformed JSON to explicit
  `OAuthTokenStatus` values. Caller cancellation propagates; there is no logging path.
- `OAuthToken` carries access token, optional refresh token, absolute UTC expiry, token type
  and granted scope; every value is redacted in `ToString`. `expires_in` becomes `Now + seconds`.
- `IOAuthTokenStore`/`OAuthTokenStore` sits over `IProviderSecretStore` (DPAPI per account).
  `ProviderSecret` gains optional `Scope`; access/refresh/expiry/scope are stored, a refresh
  response without a refresh token/scope keeps stored values, and sign-out/removal deletes the
  record. No token enters `accounts.json`/`ProviderAccount`.
- `OAuthOptions` validates absolute HTTPS endpoints without user-info, a `127.0.0.1` loopback
  redirect, at least one scope, bounded timeouts/size and no reserved-parameter override.
  `GoogleOAuthOptions` has an empty default client id, `calendar.readonly` only, loopback
  default, one calendar id (`primary`), fetch caps and `access_type=offline`/`prompt=consent`.
  A missing client id is "not configured"; `ToOAuthOptions()` throws only when a flow starts.

### Google Calendar provider
- **Probe (no network):** not configured without client id/token; ready with a usable access
  token or a refresh token; unauthorized for an expired access token without refresh. The
  secret arrives via `ProviderRequest.Secret` (sync-service contract), with token-store fallback.
- **Fetch:** `GET https://www.googleapis.com/calendar/v3/calendars/{calendarId}/events` with
  `timeMin`/`timeMax` as RFC 3339 UTC from the local half-open range, `singleEvents=true`,
  `orderBy=startTime`, `maxResults=2500`, `timeZone=UTC` and `pageToken` pagination. One
  calendar id: `ProviderAccount.RemoteId` when set, else `GoogleOAuthOptions.CalendarId`
  (`primary`); calendar selection is PRV-306.
- **Mapping:** id/summary/start/end (`dateTime` or all-day `date`), location, description,
  `hangoutLink` or `conferenceData` video entry -> `CalendarEvent.OnlineMeetingLink`; `htmlLink`
  -> additive `ProviderEvent.RemoteLink` (Core has no web-link field; only absolute `http(s)`
  links are kept and never logged). All-day dates map to local midnight with an exclusive
  next-day end, matching Core/`IcsParser`.
- **Errors:** 401 refreshes once and retries once (second 401, no refresh token or refresh
  failure -> `Unauthorized`); 403 `Unauthorized`; 429/5xx/network/timeout `Unavailable`;
  404/unexpected redirect/malformed JSON/oversize `InvalidResponse`. Page/event caps are
  explicit `InvalidResponse`; cancellation propagates.
- **Caps/privacy:** 2 MiB per page, 10 pages, 10 000 events (configurable, validated); no
  event content, calendar id, URL, token, code or header is logged or persisted here.

## Acceptance criteria
- [ ] **AC-1 - PKCE:** verifier 43-128 base64url chars (generated 86), S256 challenge matches RFC 7636 including the test vector, state >=256 bits, constant-time single-use validation.
- [ ] **AC-2 - loopback flow:** binds only `127.0.0.1` before launch, builds the required query, accepts exactly one callback, validates path/Host/state, handles denial/provider errors, times out, propagates cancellation, uses the injectable system-browser launcher and exchanges the code once with no client secret.
- [ ] **AC-3 - token client:** HTTPS endpoints, correct code/refresh forms, `expires_in` mapping, explicit 400/401/403/429/5xx/network/timeout/invalid-JSON/oversize statuses, no redirects, caller cancellation and redacted output.
- [ ] **AC-4 - token storage:** access/refresh/expiry/scope round-trip via `IProviderSecretStore`; rotation preserves an omitted refresh token/scope; remove deletes; account JSON has no token fields; formatted output redacts.
- [ ] **AC-5 - Google probe:** not configured without client id/token; ready with valid access token or refresh token; unauthorized for expired access without refresh; no network in probe.
- [ ] **AC-6 - fetch/mapping:** correct endpoint, query, UTC bounds, Bearer header, pagination; timed/all-day mapping, location, description, hangout/conference and `htmlLink`; out-of-range events skipped.
- [ ] **AC-7 - errors/cancellation:** 401 refresh+retry once; second 401/refresh failure unauthorized; 403/429/5xx/network/404/redirect/malformed JSON/oversize explicit; cancellation propagates at every stage.
- [ ] **AC-8 - caps:** page, event and response-size caps reject oversized responses with `InvalidResponse` and no partial fabricated list.
- [ ] **AC-9 - privacy/config:** no embedded client id/secret; only `calendar.readonly`; no token/code/verifier/event content/URL in logs or statuses; HTTPS and loopback validated; no token in account metadata.
- [ ] **AC-10 - tests/build:** all new behaviour covered offline; x64 solution and ARM64 provider builds pass; CI remains the canonical `dotnet test` gate.

## Test plan

| AC | Test (file / representative names) | Kind |
|---|---|---|
| AC-1 | `OAuthPkceTests.Create_ProducesVerifierChallengeAndStateWithinRfc7636`, `ComputeCodeChallenge_MatchesTheRfc7636Example`, `TryValidateAndConsumeState_AcceptsOnlyOnce`, `MismatchDoesNotConsume`, `ToString_RedactsVerifierChallengeAndState` | unit |
| AC-2 | `LoopbackOAuthFlowTests.AuthorizeAsync_BuildsThePkceUrlAndExchangesTheCodeOnce`, `StateMismatchFailsWithoutTokenExchange`, `AccessDeniedIsExplicitAndSendsNoTokenRequest`, `TimeoutIsExplicit`, `CallerCancellationPropagates`, `BrowserLaunchFailureIsExplicit`, `MissingCodeIsInvalidCallback`, `WrongHostIsRejected`, `AcceptsOnlyOneCallback`, `ResultAndUrlNeverExposeTheVerifier` | unit |
| AC-3 | `OAuthTokenClientTests.ExchangeCodeAsync_SendsThePkcePublicClientForm`, `RefreshAsync_SendsTheRefreshGrantWithoutASecret`, `RefreshAsync_MapsHttpFailuresToExplicitStatuses`, `MalformedJsonIsInvalidResponse`, `MissingAccessTokenIsInvalidResponse`, `NetworkFailureIsNetworkError`, `TimeoutIsExplicit`, `CallerCancellationPropagates`, `RejectsRedirects`, `OversizedResponseIsInvalidResponse`, `Options_RejectNonHttpsOrCredentialedEndpoints`, `Options_RejectNonLoopbackRedirectUris` | unit |
| AC-4 | `OAuthTokenStoreTests.SaveAndGet_RoundTripsAccessRefreshExpiryAndScope`, `SaveAsync_WithoutARefreshTokenKeepsTheStoredRefreshToken`, `RemoveAsync_DeletesTheStoredToken`, `GetAsync_ReturnsNullForAnEmptyOrNonOAuthSecret`, `OAuthTokenFromSecret_MapsTheStoredValues`, `ProviderAccountJson_NeverCarriesOAuthTokenFields` | unit |
| AC-5 | `GoogleCalendarProviderTests.ProbeAsync_WithoutAClientId_IsNotConfiguredWithoutNetwork`, `WithoutAToken_IsNotConfiguredWithoutNetwork`, `WithAUsableAccessToken_IsReadyWithoutNetwork`, `WithOnlyARefreshToken_IsReady`, `WithAnExpiredAccessTokenAndNoRefreshToken_IsUnauthorized`, `PreCanceledTokenThrows` | unit |
| AC-6 | `GoogleCalendarProviderTests.GetEventsAsync_BuildsTheExpectedRequest`, `UsesTheAccountRemoteIdAsTheCalendarId`, `MapsATimedEvent`, `MapsAnAllDayEvent`, `MapsConferenceDataWhenHangoutLinkIsAbsent`, `SkipsEventsOutsideTheRange`, `PaginatesWithNextPageToken` | unit |
| AC-7 | `GoogleCalendarProviderTests.GetEventsAsync_401RefreshesOnceAndRetriesAndStoresTheNewToken`, `401AgainAfterRefreshIsUnauthorized`, `RefreshFailureIsUnauthorized`, `ExpiredAccessTokenRefreshesBeforeTheFetch`, `WithA401AndNoRefreshTokenIsUnauthorized`, `MapsHttpFailuresToExplicitStates`, `NetworkFailureIsUnavailable`, `MalformedJsonIsInvalidResponse`, `RedirectIsInvalidResponse`, `EmptyRangeReturnsEmptyWithoutARequest`, `CancellationDuringTheRequestPropagates`, `PreCanceledTokenThrows` | unit |
| AC-8 | `GoogleCalendarProviderTests.TooManyPagesIsInvalidResponse`, `TooManyEventsIsInvalidResponse`, `OversizedResponseIsInvalidResponse` | unit |
| AC-9 | `OAuthTokenStoreTests.ProviderAccountJson_NeverCarriesOAuthTokenFields`, `OAuthToken_ExpiryHonorsTheSkewAndRedactsItsValues`, `OAuthTokenClientTests.TokenResultToString_RedactsTheTokenAndCode`, `LoopbackOAuthFlowTests.AuthorizeAsync_ResultAndUrlNeverExposeTheVerifier`; static review: no logging, no client secret, only `calendar.readonly` | unit + review |
| AC-10 | Build, test and spec evidence below; `ProviderCatalog_WithGoogleOptions_CreatesAConfiguredProvider` | build + unit |

## Verification (local, 2026-11-08)
Local .NET 10 SDK: `C:\Users\ponam\AppData\Local\Microsoft\dotnet\dotnet.exe` (not on `PATH`);
no Windows SDK, so the solution build uses spec 0006's workaround. `testhost` cannot start in
this shell, so tests run with xunit v2 console in-process; CI remains canonical.
```powershell
# x64 solution: 0 errors (test-analyzer warnings such as CA1707 remain; provider project is 0 warnings)
dotnet build windows/ItsycalForWindows.sln -c Debug -p:Platform=x64 -p:EnableMsixTooling=false -p:EnableCoreMrtTooling=false -p:CsWinRTWindowsMetadata=<nuget-packages>\microsoft.windows.sdk.net.ref\10.0.26100.68-preview\winmd\Windows.Foundation.FoundationContract.winmd
# ARM64 provider: 0 errors, 0 warnings
dotnet build windows/src/ItsycalForWindows.Providers/ItsycalForWindows.Providers.csproj -c Debug -p:Platform=ARM64
# xunit console fallback: Total: 501, Errors: 0, Failed: 0, Skipped: 0
dotnet exec <xunit-console-2.9.3>/xunit.console.dll windows/tests/ItsycalForWindows.Tests/bin/x64/Debug/net10.0-windows10.0.26100.0/ItsycalForWindows.Tests.dll -nocolor
```
New classes: `OAuthPkceTests` (10), `OAuthTokenClientTests` (27), `OAuthTokenStoreTests` (7),
`LoopbackOAuthFlowTests` (10), `GoogleCalendarProviderTests` (37) - 91 new offline cases; suite
total 501 after removing four obsolete Google skeleton cases. Tests use a fake
`HttpMessageHandler`, a fake browser launcher and a real ephemeral `127.0.0.1` socket; no live
provider, DNS or browser.

## Privacy and security
- **At rest:** tokens only in the existing DPAPI per-account record; account JSON carries no token; no event content is written to disk here.
- **In transit:** HTTPS-only validated endpoints; no client secret; size/time caps; Bearer header never logged. The reusable token client currently rejects a redirect post-hoc; a redirect-disabled handler + 307/auto-follow test is a required fix before UI/sign-in wiring (PRV-301/311).
- **Loopback:** bind before launch, one callback, state+PKCE, path/Host check, 5-minute budget, cancellation closes it. A local process racing the port cannot redeem the code without the verifier; same-user DoS/malware remains a documented residual risk.
- **Redaction:** `OAuthToken`, `OAuthTokenResult`, `OAuthFlowResult`, `OAuthPkce`, `ProviderSecret` and `ProviderEvent.RemoteLink` expose no credential/content; Google status messages contain no calendar id, URL or event text.
- **Remaining hardening:** independent security review (PRV-311) before live shipping; Credential Locker storage (ADR 0005 4.2/PRV-307) is not implemented - DPAPI is the current framework layout and PRV-307 owns the migration.

## Live verification (not claimed)
End-to-end sign-in requires a **PO-owned Google Cloud project** with an OAuth consent screen for `https://www.googleapis.com/auth/calendar.readonly` and a **Desktop app** client id (loopback redirect). This change ships no client id/secret and has made no live authorization, token or `events.list` request. Personal + Workspace (admin-policy) checks are manual PRV-301/PRV-309 steps on an installed signed MSIX; only the offline structural implementation is claimed.

## Out of scope / follow-ups

| Item | Tracking |
|---|---|
| Account/settings UI, consent text, sign-in/re-auth and surface wiring | PRV-306 |
| Live account/client-id verification, Workspace admin-policy case | PRV-301/PRV-309 |
| `syncToken` incremental sync and `410 Gone` resync | PRV-301 follow-up |
| Credential Locker for refresh tokens and final `providers\` layout | PRV-307 |
| Graph/Yandex/CalDAV transports; encrypted cache; security review | PRV-302/303/304, PRV-308, PRV-311 |
| Write/RSVP/event editing | separate ADR |

## Contract changes (accepted for this slice)

- `ProviderSecret.Scope` (OAuth scope storage; not secret) and `ProviderEvent.RemoteLink`
  (Google `htmlLink`; http(s) only) are additive optional members on framework contracts.
- `ProviderCatalog` gains optional Google options/token-store parameters; the Google skeleton
  is replaced by `GoogleCalendarProvider`; the skeleton test cases for Google are removed.
- Source-compatible for existing call sites; binary compatibility is not claimed. Spec 0006
  AC-6/test plan must note that Google is no longer a skeleton (follow-up docs touch).
- `GoogleCalendarProvider` currently has a token-store fallback; the PR #52 secret-delivery
  contract must be restored (or the exception documented) before UI wiring.

## Review

- **Spec review: approve with notes** (2026-11-08, independent; PR comment 6725911278).
  Notes: ADR/BACKLOG reconciliation for access-token persistence/Credential-Locker deviation;
  enforce exactly `calendar.readonly`; redirect-disabled token handler; explicit contract
  changes/spec 0006 annotation; test/evidence gaps.
- **Code review: approve with notes** (2026-11-08, independent; PR comment 6725911584).
  Majors: token-store read-modify-write race; post-hoc redirect detection; missing
  `client_secret`/`code_verifier` reservations; provider reads the token store directly.
- **Verdict: approved with notes for merge** of the unwired library; do **not** wire UI/sign-in
  until the major security/contract items and the `calendar.readonly` invariant are fixed.
- **Accepted merge deferrals:** scope invariant + redirect handler + token-store atomicity +
  secret-delivery restoration + `client_secret` reservations (PRV-301/307/311); syncToken/410
  (PRV-301); UI/calendar selection (PRV-306); Credential Locker/layout (PRV-307); encrypted
  cache (PRV-308); Graph/Yandex/CalDAV (PRV-302/303/304); live end-to-end (PRV-301/309).

