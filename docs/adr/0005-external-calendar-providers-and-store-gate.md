# ADR 0005 — External calendar providers and the Store gate

- Status: accepted (PO decision 2026-11-08) for the gate and the read-only, opt-in
  direction; provider scope and sequencing are proposed here and depend on the answers in
  [Open questions](#10-open-questions-for-the-po). **Implementation started:** the first
  slice is the provider framework + ICS subscriptions (PR #52); see the implementation
  amendment below.
- **Implementation amendment (2026-11-08, first slice PR #52):** the framework implements
  account metadata in `%LOCALAPPDATA%\ItsycalForWindows\accounts\accounts.json` and
  per-account DPAPI secrets in `secrets.dat` (ProtectedData `CurrentUser`, per-account
  entropy, quarantine/cross-process lock), not the `settings.json`/`providers\` layout or
  Credential Locker assumed in §4.6/§5. The deviation is deliberate for the framework
  slice and is tracked in [PRV-306/PRV-307](../BACKLOG.md); OAuth tokens remain a PRV-307
  target and no provider UI/surface is wired yet. The encrypted on-disk event cache is a
  PO decision (below, Q7) and remains the next slice (PRV-308), not part of this framework.
- Date: 2026-11-08
- Deciders: product owner
- Amends: [ADR 0002](0002-calendar-providers-and-network.md) (remote-provider follow-ups),
  [ADR 0004](0004-rebrand-independent-product.md) (the optional v2.0 Store listing).
  Does not supersede either.
- Tracking: [PRV-300 … PRV-311 and REL-303](../BACKLOG.md); roadmap
  [v1.3 provider platform](../ROADMAP.md).

## 1. Context

### What the app can show today

The Windows product is local-only. Its event sources are the Windows appointment store
(`AppointmentsEventSource`, read-only) and the `*.ics` folder under
`%LOCALAPPDATA%\ItsycalForWindows\calendars` (`IcsEventSource`), merged by
`CompositeEventSource` behind the `IEventSource` contract. [ADR 0002](0002-calendar-providers-and-network.md)
records why the store is not a viable primary source on current Windows 11: Mail and
Calendar was retired, the new Outlook does not populate the store, and the store is empty on
a clean machine. The ICS folder is a useful bridge, but it depends on the user exporting a
calendar and re-exporting it; it is not "my calendar is already here".

Neither surface (Command Palette extension or standalone flyout) can sign in to a real
calendar account. There is no OAuth flow, no token store, no CalDAV client and no HTTP
calendar subscription.

### Why that blocks a Store release

The popular Windows user's calendar lives in one of a small number of places: a Google
account (Gmail or Google Workspace), a Microsoft account or Microsoft 365 tenant
(Outlook.com, Exchange Online), a Yandex account, or a CalDAV provider (iCloud, Fastmail,
Nextcloud, mailbox.org, a self-hosted server). A calendar app that can only read an empty
OS store and a folder of exported files is, from the point of view of a Store reviewer or a
first-run user, **broken**. Store policy requires a product to be fully functional
(policy 10.1.2), testable (10.3), to declare capabilities that relate to its functions
(10.6) and, when it touches personal information, to publish a privacy policy and secure
that data (10.5). A local-only calendar would fail the value and functionality expectation
before any provider discussion.

The PO therefore decided on 2026-11-08: **Microsoft Store publication — and the
trusted-signing/Store submission that goes with it — is gated until external calendar
providers are ready: Exchange/Outlook, Gmail, Yandex Mail/Calendar and other major/popular
sources, so the app is fully functional at launch.** This ADR records that decision,
defines what "ready" means, and sequences the work.

### Constraints that already bind this decision

- **Read-only.** The roadmap keeps event creation/editing/deletion and RSVP out of scope
  until a write-safety design exists. Provider v1 must not request write scopes.
- **No telemetry, no calendar content logging.** [ADR 0002](0002-calendar-providers-and-network.md)
  and the repository rules forbid telemetry or third-party crash reporting and require
  event content never to be logged. Network access is limited to the self-update check and
  to providers the user explicitly enables.
- **Token handling.** ADR 0002 chose per-provider DPAPI-protected credentials under
  `%LOCALAPPDATA%`, with revocable tokens and lazy refresh. This ADR refines that choice
  (§4.2) but does not weaken it.
- **Two surfaces, one core.** [ADR 0003](0003-surfaces-and-host-strategy.md) makes the
  standalone WinUI 3 flyout the primary surface and keeps the Command Palette extension as
  an optional integration. Both consume `ItsycalForWindows.Core`; provider logic must not
  be duplicated between them.
- **Identity migration at v2.0.0.** [ADR 0004](0004-rebrand-independent-product.md) makes
  v2.0.0 the Calwren identity migration and the point where a Store listing, if pursued,
  uses the new Publisher/package family name. Store work must not force a second package
  identity break.
- **External lead times dominate.** Google OAuth verification for calendar scopes,
  Microsoft Entra app registration/consent, Store account setup and certification test
  accounts are calendar-time tasks that cannot be compressed by more engineering.

## 2. Decision

1. **The Store gate is explicit.** No Microsoft Store submission of a Calwren package is
   prepared for certification or submitted until every exit criterion in
   [§8](#8-store-gate-exit-criteria) is met, including P0 providers, the independent
   security review and the privacy/compliance pack. The gate is not waived for a
   "listing reservation", a deadline or a local-only build with a promise in the
   description. Trusted signing used for a non-Store channel is governed by
   [Q5](#10-open-questions-for-the-po): signing itself is not Store submission, but no build
   may be submitted to the Store before the gate opens.
2. **The provider epic precedes Store submission.** Provider work is planned as
   [PRV-300 … PRV-311](../BACKLOG.md) and is sequenced before `REL-303` (Store gate). It
   may run in parallel with the v2.0 brand/identity preparation; it may not run after the
   Store package is built.
3. **Read-only first.** Provider v1 uses read scopes only. No event creation, edit,
   deletion, RSVP, calendar-list changes or "mark as read" writes. A write provider is a
   separate decision with its own ADR, scopes (`Calendars.ReadWrite` on Graph, calendar
   write on Google/CalDAV), conflict handling and a much larger test matrix.
4. **User-enabled and per-account.** A provider account exists only after the user adds it
   and enables it. No account, no provider traffic. Enabling/disabling is per account, not
   per provider type, and disabling stops the scheduler immediately. The local experience
   (appointment store, local `.ics`, settings) works with no account and no network.
5. **Secure OAuth and token storage.** Authorization Code with PKCE (S256) on a loopback
   redirect, system browser only, public client with **no client secret shipped**, tokens
   in the Windows Credential Locker (OAuth) or DPAPI (app passwords/tokenized URLs) — see
   §4. Scope minimization, revocation, sign-out and account deletion are part of the
   feature, not follow-ups.
6. **No telemetry and no calendar content logging.** Provider diagnostics log a provider
   id, an opaque account id, a status category, a count and a duration. Never a title,
   location, description, attendee, meeting URL, request/response body, authorization
   header, token or full subscription URL. Event content is not written to disk in v1;
   the in-memory cache is the only cache (§5.4).
7. **One provider boundary, one implementation.** Providers implement the existing
   `IEventSource` contract and live in a new `ItsycalForWindows.Providers` library shared
   by both surfaces (§5.1). `IEventSource` itself stays unchanged; provider status is a
   separate, UI-facing contract because "never throw, empty list on failure" cannot express
   "sign in again".
8. **"Major/popular" is the matrix in §3, not an open-ended promise.** What is in for v1
   and what is explicitly out is recorded there; "other providers" means the validated
   CalDAV/ICS paths, not every calendar service on the market.
9. **External registrations are project assets.** The Google Cloud project/OAuth consent
   screen, the Entra app registration and the privacy-policy URL are owned by the project
   and the PO, not by an individual contributor's personal account, so verification,
   revocation and support survive personnel changes ([Q3](#10-open-questions-for-the-po)).

## 3. Provider matrix

Priorities: **P0** = required before the Store gate opens; **P1** = planned for the same
epic but may ship in the first post-gate update. Candidates that are not committed are listed
in §3.1.

| Priority | Source and users | Protocol / API | Authentication | Scope | v1 behaviour |
|---|---|---|---|---|---|
| **P0** | **Google Calendar** — Gmail, Google Workspace, googlemail | [Google Calendar API v3](https://developers.google.com/workspace/calendar/api/v3/reference) (`calendarList`, `events.list`) | OAuth 2.0 Authorization Code + PKCE (S256), loopback redirect, system browser; public client, no secret shipped | `https://www.googleapis.com/auth/calendar.readonly` | Read calendars/events for the visible range; incremental via `syncToken`; full resync on `410 Gone`; primary calendar id is the account label (no extra `userinfo` scope) |
| **P0** | **Microsoft Graph** — Outlook.com, Microsoft 365, Exchange Online | [Microsoft Graph v1.0](https://learn.microsoft.com/graph/api/resources/calendar) `me/calendars`, `me/calendarView`, `calendarView/delta` | OAuth 2.0 Authorization Code + PKCE, public client, loopback redirect; `common` authority for MSA + work/school (or `consumers`/`organizations`) | `Calendars.Read` delegated (`offline_access` for refresh) | Read calendar view with recurrence expanded; `Prefer: outlook.timezone`; page through `@odata.nextLink`; delta token with periodic full resync as the window rolls |
| **P0** | **Yandex Calendar** — Yandex Mail/360 users (PO-named) | CalDAV at `caldav.yandex.ru` (port 443; `8443` documented alternative) | HTTP Basic over TLS with a Yandex **app password** ("Calendar" type); 2FA users must create one | Provider-defined by the app password (calendar) | Preset of the generic CalDAV engine: server/principal pre-filled, app-password instructions and deep link, no OAuth in v1 (no documented public calendar OAuth scope) |
| **P0** | **Generic CalDAV** — iCloud, Fastmail, Nextcloud, mailbox.org, Synology, Radicale | [CalDAV RFC 4791](https://datatracker.ietf.org/doc/html/rfc4791) + [RFC 6764](https://datatracker.ietf.org/doc/html/rfc6764) discovery; `PROPFIND`, `REPORT calendar-query`, `sync-collection` where supported | URL + username + app password (HTTP Basic over TLS); presets for iCloud (`caldav.icloud.com`), Fastmail (`caldav.fastmail.com`), Nextcloud (`/remote.php/dav`), mailbox.org (`dav.mailbox.org`), custom URL | Provider-defined by the app password | Principal/calendar-home discovery, time-range query, ETag/`sync-token` incremental where available, full resync fallback; TLS validation never disabled |
| **P1** | **ICS subscriptions** — published Google/Outlook private links, team feeds, `.ics` URLs | HTTP(S) GET of `text/calendar`, existing `IcsParser` | None in v1; token-in-path URLs treated as secrets; reject `user:pass@` userinfo | N/A | `ETag`/`If-None-Match` and `Last-Modified`/`If-Modified-Since`, `304` handling, configurable refresh (15–360 min, default 60, jitter), size cap, no credentials in URL |
| **P0** | **Cross-cutting: settings/auth UX** | Shared accounts/settings model + WinUI/Command Palette surfaces | — | — | Add/enable/disable/sign-out/remove account, status, last sync, error state, revoke instructions, no account content in logs |
| **P0** | **Cross-cutting: token storage** | Windows Credential Locker + DPAPI | — | — | Per-account secrets, no plaintext, rotation/lazy refresh, redaction, deletion on sign-out/remove |
| **P0** | **Cross-cutting: sync scheduler** | On-demand + in-process background refresh | — | — | Visible-range fetch, in-memory cache, backoff/jitter, `429 Retry-After`, offline and error states, no event content at rest |
| **P0** | **Cross-cutting: verification, privacy, security** | Test harness, privacy policy, independent review | — | — | PRV-309/310/311; blocking before the gate |
| — | **Windows appointment store** | `Windows.ApplicationModel.Appointments` | OS | Read-only | Existing compatibility source; stays |
| — | **Local `.ics` folder** | Local files, existing parser | None | — | Existing zero-setup path; stays |

Outlook.com, Microsoft 365 and Exchange Online share the Graph provider above; **on-premises
Exchange is the one Microsoft calendar that is out of v1** because Graph does not reach it
(see §3.1). Google Workspace is the same Google provider with tenant admin policy on top;
iCloud is the iCloud preset of the generic CalDAV engine. Token storage is cross-cutting
(§4.2): Google/Microsoft refresh tokens go to the Windows Credential Locker; CalDAV/Yandex
app passwords and tokenized ICS URLs go to DPAPI per account.

### 3.1 What "all major/popular" means in this decision

In for v1 (P0/P1): Google (Gmail/Workspace), Microsoft (Outlook.com, Microsoft 365,
Exchange Online), Yandex, iCloud, Fastmail, Nextcloud, mailbox.org, generic CalDAV and
published ICS subscriptions. Teams/Outlook calendar data is reached through Graph; it is
not a separate provider. Apple Calendar on Windows is reached through iCloud CalDAV.

Explicitly **out of v1** (documented, not silently missing):

| Source | Why out | Path if it becomes required |
|---|---|---|
| **Exchange on-premises** | Graph is a cloud service and is not available for on-prem mailboxes. The only calendar API is EWS; Exchange Online EWS is being disabled (October 2026 → April 2027), and on-prem EWS needs its own auth/version matrix and a test environment. | Documented follow-up: EWS with modern auth for Exchange 2016/2019, only with a real user requirement and a test server ([Q1](#10-open-questions-for-the-po)). Do not ship Basic auth. |
| **Outlook desktop local data (PST/OST)** | No supported read API; COM automation of Outlook is fragile, security-sensitive and inappropriate for a packaged background app. | Not planned. |
| **Google via CalDAV** | Google's supported calendar surface for new integrations is the Calendar API v3; CalDAV would add a second auth model for one provider. | Use PRV-301. |
| **Proton Calendar** | No public calendar API/CalDAV for third-party clients at the time of this decision. | Re-evaluate if Proton publishes an API or a supported subscription/export path; do not promise it now. |
| **Facebook events, Meetup, Eventbrite, Calendly** | No in-scope supported read API; each would be a separate integration. | ICS export where the service offers one (PRV-305). |
| **Write / RSVP / event editing** | Out of product scope until a write-safety design; read-only promise in the roadmap. | Separate ADR, new scopes, conflict and undo design. |
| Zoho, GMX, Yahoo, AOL, HEY and similar | They may work through generic CalDAV, but were **not validated**; promising them would overstate support. | Validate in PRV-304/PRV-309 and update this matrix; do not advertise before validation. |

## 4. Security and privacy

### 4.1 OAuth: authorization code + PKCE on loopback

- **Flow:** authorization code with PKCE (`code_challenge_method=S256`), `state` with at
  least 128 bits of entropy, per-attempt `code_verifier`; the code is exchanged exactly
  once. No implicit flow, no resource-owner password flow, no out-of-band (OOB) flow and no
  custom URI scheme in v1.
- **Redirect:** an ephemeral loopback listener on `http://127.0.0.1:{port}` (and `[::1]`
  where supported), bound **before** the system browser is opened, accepting one request,
  only the registered path, only GET, with a short timeout (≤5 minutes) and a Host check
  for the loopback address. The listener closes on success, error, cancel or timeout.
- **Browser:** only the user's default system browser via the OS launcher. No embedded
  WebView/WebView2 for OAuth: Google disallows embedded user-agents for its authorization
  endpoint, and a system browser keeps cookies, MFA, conditional access and password
  managers in the trusted browser.
- **Public client:** the app ships no client secret. A secret embedded in a desktop binary
  is not a secret and creates a false sense of protection. Client ids are configuration,
  not credentials. Refresh tokens are the credential and are protected per §4.2.
- **Provider registration:** redirect URIs are registered exactly for the loopback scheme
  (Google's desktop-app flow uses loopback with a random available port; Microsoft
  public-client registrations must be verified against current Entra behavior). No wildcard
  redirects and no `http://localhost` outside the loopback pattern.
- **Errors:** `access_denied`, `admin_policy_enforced` (Google Workspace), tenant
  `admin_consent_required`/`AADSTS65001` (Microsoft 365), `invalid_grant` on refresh and
  provider outages map to explicit account states, not a silent empty calendar.

### 4.2 Token and secret storage

| Secret | Storage | Rationale |
|---|---|---|
| Google/Microsoft refresh tokens | Windows Credential Locker (`PasswordVault`), one entry per account, resource name `Calwren.Provider.<provider>.<accountId>`; access tokens in memory only | OS-managed per-user store, revocable by deletion, no plaintext file. The locker documents a 20-credential-per-app limit, which caps OAuth accounts at 20; the UI reports the limit instead of silently degrading. |
| CalDAV/Yandex app passwords, ICS token-in-path URLs | DPAPI (`ProtectedData.Protect`, `CurrentUser`, per-account entropy), one encrypted record per account under `%LOCALAPPDATA%\ItsycalForWindows\providers\` | These are broader, longer-lived secrets (an Apple app-specific password works beyond calendar; a Yandex app password is account-scoped). Keeping them device-local avoids syncing them through the Credential Locker's Microsoft-account roaming. ADR 0002's DPAPI baseline is preserved. |
| Account metadata (provider, display name, enabled, scopes, last sync, error category, delta/sync tokens) | `settings.json` next to existing settings (no event content) | Not secret, but written atomically with a cross-process lock because both surfaces may run. |
| Event content | In-memory cache only | The product promise is no event content at rest; see §5.4. |

Additional rules: never log or include secrets in exception messages, crash dumps, toast
text or telemetry (there is no telemetry); redact URLs to scheme+host in logs; clear
secrets on sign-out and account removal; rotating/refreshing a token replaces the stored
entry atomically; if the Credential Locker is unavailable (for example an unpackaged
development run), fall back to DPAPI and label the storage mode in diagnostics.

### 4.3 Scope minimization and consent

- Google: `calendar.readonly` only. No `userinfo`, Gmail, Drive or contacts scope; the
  account label comes from the primary calendar id, not from a profile call.
- Microsoft: `Calendars.Read` delegated (+ `offline_access` for a refresh token). No
  `User.Read`, Mail, Files or `Calendars.ReadWrite`; the account label comes from the
  calendar owner address returned by the calendar list.
- CalDAV/ICS: the provider decides what an app password can reach. The UI states that an
  app password may grant more than calendar access at that provider, and recommends a
  dedicated app password where the provider supports scoping (Yandex "Calendar", iCloud
  app-specific password).
- ICS subscriptions: no `user:pass@` userinfo and no interactive HTTP auth in v1. A published
  feed's opaque token in the URL path is allowed only over HTTPS and is treated as a secret
  (DPAPI, redacted, never shown in full), because providers such as Google and Outlook issue
  exactly that kind of subscription link.
- Consent text in the app says exactly what will be read, where it goes and how to revoke
  it, before the browser opens.

### 4.4 Sign-out, revocation and deletion

- **Sign out** removes the local refresh token/access token, disables the account and
  clears its in-memory cache. Where the provider has a revocation endpoint (Google
  `https://oauth2.googleapis.com/revoke`), the app calls it best-effort; the result is
  reported without blocking the local removal.
- **Remove account** deletes the secret entry, the account metadata and the cached events,
  and states that the provider-side grant may still exist with instructions to revoke it
  (Google Account permissions, Microsoft account privacy/Entra "My apps", Yandex app
  passwords, iCloud app-specific passwords, Nextcloud/Fastmail sessions).
- **App uninstall** cannot run provider revocation; the privacy policy explains how to
  revoke at each provider.
- No "delete my account in the cloud" flow exists because the product creates no cloud
  account; the Store privacy text must say this explicitly ([Q6](#10-open-questions-for-the-po)).

### 4.5 Logging, diagnostics and network behaviour

- Allowed diagnostic fields: timestamp, provider id, opaque account id (hash), operation,
  HTTP status category, item count, duration, error category and a redacted host.
- Forbidden: event subject/location/description/attendees, meeting links, request or
  response bodies, authorization codes/tokens, full CalDAV/ICS URLs with tokens, email
  addresses in logs (account ids are hashed; the account label is UI-only).
- Debug builds may add a provider trace only behind an explicit developer flag with the
  same redaction rules; release builds have no verbose provider logging.
- Network: HTTPS only (TLS 1.2+), OS certificate validation and system proxy, no
  certificate bypass, bounded redirects (≤3), response size/time caps, per-provider
  `HttpClient` with a product user agent. No provider request is made until an account is
  enabled; disabling stops scheduler work. The updater remains the only other network
  feature and is independent.

### 4.6 Threat model

| Threat | Mitigation | Residual risk |
|---|---|---|
| Malicious/oversized ICS: RRULE expansion bomb, huge lines/files, malformed UTF-8, adversarial VTIMEZONE | Size cap (existing 2 MB), line-length and occurrence caps, fail-closed on unsupported RRULE (REL-201), parser fuzzing/regression corpus, no code execution from calendar text | A crafted event can still make one refresh slow; bounded by caps and cancellation |
| Malicious CalDAV/ICS URL: SSRF to localhost/link-local/cloud metadata, redirect to attacker, credentials leaked to another origin | HTTPS only; reject URL userinfo; strip `Authorization` on any cross-origin redirect; block link-local/metadata ranges; cap redirects; allow private/LAN hosts only when the user explicitly enters a self-hosted server (with a warning); no `file:`/other schemes | A user can point the app at their own LAN service by design; that service is user-chosen |
| Local secret theft (offline disk, another user, backup) | Credential Locker/DPAPI per-user encryption; no plaintext; no event content at rest; deletion on sign-out | Any process running as the same user can ask the OS to decrypt; same-user malware is out of scope and documented |
| Loopback redirect hijack / authorization code interception | Bind before browser, PKCE S256, high-entropy `state`, single request, path/Host validation, short timeout; no custom scheme/OOB | A local process racing the port can receive a code it cannot redeem without the verifier |
| Token replay / refresh-token theft in memory | Access tokens short-lived and memory-only; refresh lazily; rotate when the provider returns a new refresh token; single-flight refresh per account | Memory scraping by same-user malware is not preventable in-process |
| Malicious provider response: JSON bombs, script/HTML in event text, hostile meeting links | Response size/depth caps, schema validation, render event text as text (never HTML), only `http(s)` meeting links, no auto-open | Provider content is untrusted by definition; keep rendering inert |
| Account mix-up (token from account A used for B) | Per-account storage keys; verify the provider's returned account id/email on connect and refresh; re-auth on mismatch; show the account label before enabling | Provider id changes (rare) force re-auth |
| Enterprise TLS interception / proxy | Use OS trust and system proxy; do not pin or bypass certificate errors; document proxy support and failure states | Some corporate proxies break loopback OAuth or provider endpoints; the error state explains it |
| Settings tampering by the user | Settings are user-controlled; worst case is a bad CalDAV URL or disabled provider | No privilege boundary is crossed; secrets stay OS-protected |
| Store privacy/capability mismatch | Privacy policy, capability declarations (`internetClient`, possibly `privateNetworkClientServer` for LAN CalDAV) and certification notes match the implemented flows; no undeclared background traffic | Store review may still question a full-trust desktop package; certification notes must justify each capability |

## 5. Architecture

### 5.1 Options considered

| Option | Shape | Pros | Cons | Verdict |
|---|---|---|---|---|
| **A. New `ItsycalForWindows.Providers` project** | Class library referencing `ItsycalForWindows.Core`; provider adapters, OAuth, secret store, sync and status; both apps reference it | Keeps `Core` free of network/OAuth/UI dependencies; one security-review scope; both surfaces share one implementation; testable without WinUI/Command Palette; can add per-provider packages later | One more project and reference in both apps; needs a clean abstraction for settings/status | **Recommended** |
| B. Providers inside `Core` | Add Google/Graph/CalDAV/HTTP/OAuth to the existing shared library | Fewer projects; both apps already reference Core | Turns the calendar core into a network/OAuth library; broadens the trust boundary and test surface; every Core consumer inherits HTTP dependencies; harder to review and later split | Not recommended |
| C. One project per provider | `Providers.Google`, `Providers.Graph`, `Providers.CalDav`, … | Strong isolation, optional binary size, independent versions | Duplicates OAuth/sync/HTTP infrastructure or adds a fourth shared library; 4+ projects and build/test overhead for a small team; premature before the provider contracts stabilize | Defer; split only if a dependency conflict appears |
| D. Third-party calendar aggregator/cloud | Send credentials to a hosted aggregator that returns events | Fastest provider coverage | Sends calendar data and tokens to a third party, contradicts the privacy promise, adds cost and a data processor; Store privacy burden | Rejected |
| E. Wait for Windows appointment-store sync | Rely on Outlook/Google accounts populating the OS store | No provider code | Store is empty on current Windows 11 (ADR 0002); not under our control | Rejected |

### 5.2 Recommended shape

```
Command Palette extension ─┐
                            ├─> ItsycalForWindows.Providers ──> ItsycalForWindows.Core
Standalone flyout app ──────┘        │                              (IEventSource, models,
                                     │                               IcsParser, EventRange)
                                     ├─ Accounts + registry (provider metadata, per-account config)
                                     ├─ OAuth (Google, Microsoft) + loopback receiver + PKCE
                                     ├─ CalDAV engine (Yandex/iCloud/Fastmail/Nextcloud/mailbox presets)
                                     ├─ ICS subscription source (HTTP conditional GET + IcsParser)
                                     ├─ Secrets (Credential Locker / DPAPI)
                                     └─ Sync coordinator + in-memory event cache + status
```

- **`IEventSource` stays the data contract.** Each account/calendar becomes an
  `IEventSource`; `CompositeEventSource` merges them with the local sources exactly as it
  does today. No provider type leaks into the calendar UI.
- **Status is a separate contract** (`IProviderStatusSource`/`ProviderAccountState`):
  configured, enabled, syncing, synced-at, sign-in-required, reauth-required, offline,
  rate-limited, error, unsupported. `GetEventsAsync` keeps its "never throw, empty on
  failure" rule; the status is what the settings surface reads.
- **Registry:** a `ProviderRegistry` maps a stable provider id (`google`, `microsoft`,
  `yandex`, `caldav`, `ics`) to a descriptor (display name, auth kind, capabilities,
  preset list) and a factory. Adding a provider is a descriptor + adapter, not a switch in
  both apps.
- **Composition:** `CalendarSourceFactory` builds the enabled accounts' sources plus the
  Windows store and local ICS sources and returns one composite; both surfaces use it.
  Account composition is deterministic (registration order), so de-duplication is stable.
- **Cross-process concerns:** the two packages can run at the same time. Settings writes
  are atomic and serialized by a named mutex; token refresh is single-flight per account
  across processes (named mutex); delta/sync tokens are metadata and tolerate a stale
  writer because a rejected token triggers a full resync.

### 5.3 Sync scheduler and refresh

- **On demand:** the UI asks for the visible range (month grid plus the selected day);
  adapters serve a fresh in-memory cache or fetch. A stale-while-revalidate policy (serve
  cached, refresh in the background) keeps the flyout instant.
- **Background:** only while a surface process is running and at least one account is
  enabled. No Windows service, scheduled task or startup daemon in v1. Default intervals:
  Graph/Google 15 minutes, CalDAV 30 minutes, ICS 60 minutes, each with jitter; a manual
  "Refresh now" is always available.
- **Range and window:** fetch the visible range plus a bounded lookback/lookahead (for
  example −7…+60 days) so a month view and multi-day events are complete without pulling a
  year of history. The window is a constant, not a setting, in v1.
- **Incremental:** Google `syncToken`, Graph delta with periodic full resync as the window
  rolls, CalDAV `sync-token`/ETag where advertised. A `410`/invalid token triggers one full
  resync, not a retry loop.
- **Failure policy:** exponential backoff with jitter (for example 1, 2, 5, 15, 30 minutes),
  `Retry-After` on `429`/`503`, cancellation on sign-out/close, maximum two concurrent
  provider requests, one failing account never hides another account's or the local
  events. Auth failures stop automatic retries until the user re-authenticates.
- **Offline:** last in-memory results stay visible for the session with an "offline"
  status; after a restart the provider shows "not synced yet" and retries when online.
  There is no event-content cache on disk in v1 ([Q7](#10-open-questions-for-the-po)).

### 5.4 Cache and persistence

- Per-account, per-range in-memory cache with a short TTL (about 5 minutes) and a bounded
  event count; evicted on disable/sign-out/remove and on process exit.
- Persisted: account metadata, scopes, enabled flag, last-sync timestamp, delta/sync token
  and the secret-store reference — never event fields, attendee lists or bodies.
- If a future requirement needs offline-across-restart behaviour, an encrypted cache is a
  separate privacy decision (ADR + privacy-policy update), not a silent implementation
  detail.

### 5.5 Settings and authentication surfaces

**Recommended:** one shared account/settings model and one full **`AccountsWindow`**
(WinUI 3) hosted by the standalone flyout package, plus a **Command Palette settings page**
that mirrors status and launches the window.

- The shared `AccountsWindow` owns the multi-step flows the Command Palette Adaptive Card
  surface cannot express well: browser sign-in with progress/cancel, CalDAV/ICS forms
  with validation, per-account errors, sign-out/remove confirmation, scope explanation.
- The Command Palette settings page lists accounts, states, last sync and a "Manage
  accounts" command; it does not duplicate the OAuth flow. If the standalone package is
  not installed, it offers install guidance and read-only status.
- Why not Command Palette only: the host renders a settings page as a list of host
  controls/Adaptive Cards; a modal browser-wait flow, calendar picker and per-account
  error recovery would be cramped and host-limited. The palette remains the quick
  enable/disable/status surface.
- Why not duplicate both: two auth UIs drift and double the security review.
- **Sequencing hedge:** if the provider epic must ship before the standalone package is
  installable, PRV-306 delivers a minimal in-extension flow (system browser, status page,
  toast on completion) and the full window follows with the flyout; this is a scheduling
  fallback, not the target architecture.
- Settings content: provider list with account label, enable toggle, status/last sync,
  "Sign in"/"Re-authenticate", "Sign out", "Remove account", scope summary and revocation
  instructions; CalDAV/ICS forms; no event content.

### 5.6 Error states (user-visible)

`Not configured` · `Disabled` · `Sign-in required` · `Re-authentication required` ·
`Syncing` · `Synced <time>` · `Offline` · `Rate limited (retry <time>)` ·
`Provider error (category)` · `Unsupported/needs attention`. Errors are shown per account
in settings and as a compact aggregate in the flyout (for example "1 calendar needs
attention"); they never replace the local calendar or the last good events in the same
session.

## 6. Backlog and sequencing

Detailed rows, dependencies and sizes are in [BACKLOG.md](../BACKLOG.md); this is the
order the ADR commits to. Each implementation row starts from a `docs/specs/NNNN-*.md` spec
with numbered acceptance criteria and a test mapping (repository rule); this ADR defines the
architecture and the gate, not the per-provider specs.

1. **PRV-300** provider epic/architecture (registry, contracts, project skeleton),
   **PRV-307** secret storage and **PRV-306** the shared account/settings model start
   first because every provider depends on them.
2. Parallel provider tracks after the contracts exist:
   - **PRV-301** Google (OAuth + syncToken) and **PRV-302** Microsoft Graph (OAuth +
     delta) are independent and both P0.
   - **PRV-304** generic CalDAV engine unlocks **PRV-303** Yandex (preset) and the
     iCloud/Fastmail/Nextcloud/mailbox.org presets; P0.
3. **PRV-308** sync scheduler/cache lands with the first adapter and hardens across all
   providers; **PRV-305** ICS subscriptions can run in parallel (P1, not a gate blocker).
4. **PRV-309** verification harness and **PRV-310** privacy/compliance pack run alongside;
   **PRV-311** independent security review is scheduled before the gate and re-run after
   material changes.
5. **REL-303** Store gate closes only when §8 is satisfied; Store submission follows the
   v2.0 identity/signing preparation (ADR 0004), not before.

Indicative size (S ≤ ~1 week, M ~2–3 weeks, L ~4–6 weeks of one engineer including tests;
not a schedule commitment):

| ID | Item | Size | Depends on |
|---|---|---|---|
| PRV-300 | Provider epic, registry, shared contracts, project skeleton | M | — |
| PRV-301 | Google Calendar (API v3, OAuth PKCE, syncToken) | L | PRV-300, PRV-307 |
| PRV-302 | Microsoft Graph (Outlook/M365/Exchange Online, delta) | L | PRV-300, PRV-307 |
| PRV-303 | Yandex Calendar preset (app password, 2FA guidance) | S | PRV-304 |
| PRV-304 | Generic CalDAV engine + presets | L | PRV-300, PRV-307 |
| PRV-305 | ICS subscriptions (conditional GET, refresh, secrets) | M | PRV-300 |
| PRV-306 | Settings/auth UX (shared model, AccountsWindow, CmdPal page) | L | PRV-300, PRV-307 |
| PRV-307 | Secret storage (Credential Locker/DPAPI, rotation, redaction) | M | PRV-300 |
| PRV-308 | Sync scheduler, in-memory cache, backoff/status | M | PRV-300, first adapter |
| PRV-309 | Verification harness/fixture matrix (no live secrets in CI) | M | PRV-301/302/304 |
| PRV-310 | Privacy/compliance pack (policy, Store answers, deletion/revocation docs) | M | PRV-306 |
| PRV-311 | Independent security review (OAuth, tokens, threat model) | M | PRV-301/302/304/307 |
| REL-303 | Store gate: certification pack, test accounts, submission block | S | all P0 + PRV-311 |

## 7. What can ship before the Store gate

The gate applies to **Microsoft Store publication**, not to all development or
distribution:

- **Local experience:** the current appointment store, local `.ics` folder, onboarding,
  compact UI (specs 0003/0004), updater and settings continue to ship as they do today.
- **Rebrand and package identity (v2.0.0):** the Calwren name, icon, Publisher/PFN
  migration and trusted signing can be prepared and shipped through a non-Store channel
  (GitHub releases / WinGet) if signing is available and [Q5](#10-open-questions-for-the-po)
  allows it. The identity migration must not be repeated for the Store later.
- **WinGet:** may be used for a trusted release before the gate only with the PO's
  decision; the same "fully functional at launch" judgement applies to a public WinGet
  listing, so the recommendation is to keep WinGet and Store on the same provider gate
  even though only the Store is formally blocked.
- **Store preparation that is not submission:** Partner Center account/reservation,
  privacy policy, screenshots, age rating, certification notes and test accounts can be
  prepared, but no package is submitted for certification.
- **Must not ship/market before the gate:** Store listing, Store metadata claiming
  Google/Outlook/Yandex support, and any "works with your calendar" claim for providers
  that are not implemented and verified.

## 8. Store gate exit criteria

All of the following must be true and evidenced in the tracking issue/PR before
`REL-303` closes:

1. **P0 providers accepted on an installed, signed MSIX with package identity** — Google
   (personal and a Google Workspace account with admin restrictions handled), Microsoft
   (Outlook.com and a Microsoft 365 tenant; admin-consent-required case handled), Yandex
   (2FA account with app password), and generic CalDAV validated against iCloud, Fastmail,
   Nextcloud and mailbox.org (at least one account each; a self-hosted Nextcloud counts).
2. **End-to-end account lifecycle** — first sign-in, token refresh after expiry, provider
   revocation detected as re-auth-required, sign-out, remove account, no orphaned secrets,
   no event content in logs or on disk.
3. **Local-first integrity** — with no account configured the app behaves exactly as
   today; one failing/disabled account does not hide local or other-account events; error
   and offline states are visible and recoverable.
4. **Security review (PRV-311)** — independent review of OAuth, loopback, secret storage,
   redirects, SSRF/ICS handling and logging; no blocking findings, or explicitly deferred
   with PO agreement.
5. **Privacy/compliance (PRV-310)** — privacy policy live at a project-owned URL; Store
   privacy answers match the actual data flows; revocation/deletion instructions for every
   provider; capability declarations justified; no telemetry verified by a network capture
   with all accounts disabled.
6. **Verification harness (PRV-309)** — deterministic tests for adapters and parsers using
   recorded fixtures; live-account checks documented as manual verification; CI never
   stores real tokens/accounts.
7. **Certification pack** — dedicated test accounts and app passwords in the Partner
   Center notes ([Q6](#10-open-questions-for-the-po)), screenshots from the installed build,
   age rating, support contact, and a build produced from the v2.0 identity with the
   trusted certificate.
8. **PO sign-off** — the PO confirms the launch matrix, the out-of-scope list and the
   Store text; `REL-303` is closed explicitly, not by elapsed time.

## 9. Consequences and risks

Positive: the app can truthfully say it reads the user's real calendars; one provider
abstraction serves both surfaces; security/privacy review happens before a Store audience
sees the app; the gate prevents a "broken at launch" Store review that would be expensive
to reverse.

Negative: the provider epic is large and has external lead times (Google verification,
Entra registration, Apple/Yandex app-password UX); read-only limits the value for some
users; two surfaces plus two packages increase settings/synchronization complexity; the
Store date is now dependent on OAuth approvals outside the team's control.

| Risk | Mitigation |
|---|---|
| Google OAuth verification for `calendar.readonly` delays launch (privacy policy, demo video, scope justification) | Start the Google Cloud project and verification early (PRV-301 kickoff), keep the scope minimal, prepare the demo video from the installed build; do not rely on "Testing" mode for public users. |
| Entra app registration/tenant consent blocks Microsoft 365 users | Use delegated `Calendars.Read` (user-consentable where tenant policy allows), document the admin-consent path, surface `admin_consent_required` clearly; do not require tenant-wide admin consent for the basic flow. |
| On-prem Exchange users are excluded | Documented out-of-scope with the EWS follow-up ([Q1](#10-open-questions-for-the-po)); no silent claim of Exchange support. |
| Yandex app-password/2FA friction reduces completion | Preset with a direct deep link and step-by-step guidance; clear error when an ordinary password is used; app password revocation instructions. |
| OAuth loopback blocked by firewall/enterprise policy | Detect bind/open failures and explain; system browser and OS proxy support; no silent fallback to an embedded browser. |
| Token/secret leak through logs or a crash report | Redaction tests, no telemetry, code-review checklist, independent security review, DPAPI/Credential Locker only. |
| Provider API changes (Graph delta, Google syncToken, CalDAV quirks) | Adapter tests with recorded fixtures, bounded dependency on optional features (delta/sync-token are optimizations with full-resync fallback), provider status states. |
| Store review questions the full-trust package or network use | Certification notes explaining the local-first design, per-account opt-in, privacy policy and no-telemetry posture; correct capability declarations. |
| Provider work delays v2.0 brand release | Ship the rebrand/trusted signing/WinGet path independently if the PO allows; Store waits. The gate is on Store publication, not on the identity migration. |

## 10. Open questions for the PO

These need answers before the spec/implementation of the affected rows; the ADR records a
recommendation but does not decide them.

1. **On-prem Exchange in v1?** Graph cannot reach on-prem mailboxes; the alternative is
   EWS with modern auth and a real test server (large, version-dependent, and Exchange
   Online EWS is being retired). **Recommendation:** document on-prem Exchange as
   unsupported in v1 and track an EWS follow-up only if a real user/customer needs it. Is
   that acceptable, or is on-prem Exchange a launch requirement?
2. **Read-only vs write?** Confirm provider v1 is read-only (no event create/edit/delete,
   no RSVP, no `Calendars.ReadWrite`). **Recommendation:** read-only for v1; write is a
   separate ADR and milestone. If write is required at Store launch, the scope and timeline
   change materially.
3. **Who owns the OAuth/cloud registrations?** The Google Cloud project + OAuth consent
   screen verification, the Entra app registration (single vs multi-tenant, publisher
   verification), the privacy-policy domain and the Store developer account need a legal
   identity and a long-lived owner. Yandex needs no OAuth client in v1 (app password), but
   the Yandex test account and its app password are certification assets too.
   **Recommendation:** the project/PO owns them under a project identity, not a personal
   account. Who will create them, and is there a legal entity/domain available now?
4. **Is Yandex P0 for the gate and acceptable as app-password-only?** The PO named Yandex;
   it has no documented public calendar OAuth, so v1 uses a CalDAV app password with 2FA
   guidance. **Recommendation:** yes on both. If Yandex must support OAuth or a smoother
   sign-in, that is a separate investigation with Yandex.
5. **May WinGet/trusted signing ship before the provider gate?** The Store is blocked, but
   a trusted WinGet/sideload v2.0 release could ship local-only if signing is available.
   **Recommendation:** keep WinGet behind the same P0 provider gate because a public WinGet
   listing suffers the same "broken at launch" judgement; allow local/dev/CI builds
   unrestricted. Which policy does the PO want?
6. **Store certification accounts and privacy text.** Will the PO provide dedicated test
   accounts (Google personal + Workspace, Microsoft MSA + M365 tenant, Yandex with 2FA,
   one CalDAV account) and app passwords for the Partner Center certification notes, plus
   the privacy-policy URL? Reviewers cannot complete OAuth without working test accounts.
   Also confirm the Store text may say "no account required; providers are optional", and
   whether certification requires an **account-deletion statement** even though the app
   creates no account. Our plan is to state that provider connections and local data can be
   removed at any time and that revoking the provider-side grant happens at the provider;
   is that sufficient?
7. **Offline cache.** **PO decision (2026-11-08): encrypted disk cache is required** — cached
   events must survive restart, encrypted at rest with a DPAPI-wrapped key + authenticated
   encryption per account, purged on sign-out/removal, never logged. The framework's
   in-memory cache is a placeholder only; PRV-308 owns the encrypted cache/TTL/purge work and
   supersedes the earlier in-memory-only recommendation.
8. **Account limits.** The Credential Locker documents a 20-credential-per-app limit
   (OAuth accounts), and DPAPI records have no hard cap. **Recommendation:** support up to
   10 OAuth accounts and 10 CalDAV/ICS accounts in v1 with a clear limit message. Is that
   enough for the target users?

## 11. Alternatives considered

- **Ship the Store listing now with a local-only app and add providers later.** Rejected:
  the PO's gate exists precisely to avoid this; Store reviews and early reviews would
  judge the app broken, and a post-launch provider update does not repair first impressions.
- **Ship Google only, then Microsoft.** Rejected for the gate: Outlook/Microsoft 365 is
  the largest Windows work-calendar population; a Google-only launch would still look
  incomplete to a large share of users. They can be built in parallel and gated together.
- **Use a hosted aggregator (Nylas/Cronofy-style) to get all providers at once.**
  Rejected: sends tokens and calendar data to a third party, adds a data processor and
  cost, contradicts the privacy promise, and does not remove Store privacy obligations.
- **Use an embedded WebView for OAuth.** Rejected: Google disallows embedded user-agents
  for authorization, it breaks password managers/MFA/conditional access, and it invites
  credential phishing.
- **Ship a client secret to simplify OAuth.** Rejected: desktop binaries cannot keep
  secrets; PKCE + public client is the correct native-app model.
- **Use EWS for Exchange Online as well.** Rejected: Exchange Online EWS is being disabled
  (October 2026 → April 2027); Graph is the supported path.
- **Write-first integration (create events, RSVP).** Rejected for v1: read-only is the
  product promise and write safety/conflict handling is a separate design.
- **Cache event content on disk for offline-first.** Deferred: conflicts with the current
  no-content-at-rest promise; would need encryption, a privacy-policy update and a
  retention/delete design ([Q7](#10-open-questions-for-the-po)).

## References

- [ADR 0002 — Calendar providers and the network policy](0002-calendar-providers-and-network.md)
- [ADR 0003 — Surfaces: hybrid standalone flyout + Command Palette extension](0003-surfaces-and-host-strategy.md)
- [ADR 0004 — Rebrand as an independent Windows calendar at v2.0.0](0004-rebrand-independent-product.md)
- [Backlog](../BACKLOG.md) · [Roadmap](../ROADMAP.md) · [Signing and distribution](../SIGNING.md)
- Provider interfaces: `windows/src/ItsycalForWindows.Core/Services/IEventSource.cs`,
  `IcsEventSource.cs`, `CompositeEventSource.cs`, `AppointmentsEventSource.cs`;
  settings in `windows/src/ItsycalForWindows/Helpers/SettingsManager.cs`; packaging in
  `windows/src/ItsycalForWindows/Package.appxmanifest`.
- Google: [Calendar API v3](https://developers.google.com/workspace/calendar/api/v3/reference),
  [OAuth 2.0 for desktop apps](https://developers.google.com/identity/protocols/oauth2/native-app),
  [scopes](https://developers.google.com/workspace/calendar/api/auth),
  [sync](https://developers.google.com/workspace/calendar/api/guides/sync),
  [app verification](https://support.google.com/cloud/answer/9110914)
- Microsoft: [Graph calendar](https://learn.microsoft.com/graph/api/resources/calendar),
  [delta query](https://learn.microsoft.com/graph/delta-query-events),
  [auth code flow](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-auth-code-flow),
  [EWS retirement in Exchange Online](https://learn.microsoft.com/exchange/clients-and-mobile-in-exchange-online/deprecation-of-ews-exchange-online)
- Standards: [RFC 8252 — OAuth 2.0 for Native Apps](https://datatracker.ietf.org/doc/html/rfc8252),
  [RFC 7636 — PKCE](https://datatracker.ietf.org/doc/html/rfc7636),
  [RFC 4791 — CalDAV](https://datatracker.ietf.org/doc/html/rfc4791),
  [RFC 6764 — CalDAV discovery](https://datatracker.ietf.org/doc/html/rfc6764),
  [RFC 6578 — collection sync](https://datatracker.ietf.org/doc/html/rfc6578)
- Providers: [Yandex Calendar desktop/CalDAV setup](https://www.yandex.com/support/yandex-360/customers/calendar/web/en/sync/sync-desktop.html),
  [Yandex app passwords](https://id.yandex.com/security/app-passwords),
  [iCloud app-specific passwords](https://support.apple.com/en-us/102654),
  [Fastmail CalDAV](https://www.fastmail.com/help/clients/caldav.html),
  [Nextcloud Calendar](https://docs.nextcloud.com/server/latest/user_manual/en/groupware/calendar.html)
- Windows: [Credential Locker](https://learn.microsoft.com/windows/apps/develop/security/credential-locker),
  [DPAPI ProtectedData](https://learn.microsoft.com/dotnet/api/system.security.cryptography.protecteddata),
  [Microsoft Store Policies 7.20](https://learn.microsoft.com/windows/apps/publish/store-policies)
  (10.1.2 functionality, 10.3 testability, 10.5 privacy, 10.6 capabilities)
