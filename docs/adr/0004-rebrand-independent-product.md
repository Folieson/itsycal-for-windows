# ADR 0004 — Rebrand as an independent Windows calendar at v2.0.0

- Status: accepted (PO decision 2026-11-08); product name TBD (see [Status](#status))
- Date: 2026-11-08
- Deciders: product owner
- Supersedes: nothing. Amends the branding assumption in
  [ADR 0001](0001-windows-command-palette-extension.md) and the v2.0 milestone in
  [docs/ROADMAP.md](../ROADMAP.md).
- Tracking: [BRAND-901 and BRAND-902](../BACKLOG.md)

## Context

The product started as a Windows port of ItsyCal and has outgrown that framing. It now has
three parts the upstream project does not: a standalone WinUI 3 tray flyout as the primary
compact surface, a PowerToys Command Palette extension as an optional integration, and a
shared `ItsycalForWindows.Core` library used by both
([ADR 0003](0003-surfaces-and-host-strategy.md)). It owns its acrylic visual language, its UI
specifications and a roadmap about Windows distribution, signing and calendar providers. The
macOS application and this repository share history and MIT-licensed material, not a current
code base or product plan.

The product still ships under the name **ItsyCal** ("Itsycal for Windows"). That is the name
of the upstream macOS application. The MIT license grants rights to the code and requires the
copyright and permission notice to be retained; it does not grant rights to the upstream
project's name, logo or other brand identifiers, registered or unregistered. An independent
Windows calendar presented as "ItsyCal for Windows" implies affiliation with, or endorsement
by, the upstream project. That implication is a trademark and consumer-confusion risk in the
markets where the product is distributed (US/EU), and it blocks a clean WinGet, Store and
product-domain identity.

The move from the self-signed development certificate to a trusted certificate (SignPath
Foundation / Certum OSS, and later the Microsoft Store) changes the MSIX `Publisher`. The
`Publisher` and package `Name` together determine the package family name, so trusted signing
already forces a one-time package-identity break: a package with a new PFN cannot upgrade the
old package in place. Combining the forced identity migration with the rebrand avoids a second
break and a second user migration later.

The project has no established user base yet; 1.x releases are development pre-releases. The
cost of asking the few existing users to uninstall and reinstall is lower now than after a
stable, trusted release.

## Decision

1. **v2.0.0 is the first release under an independent identity.** It introduces a new product
   name, original branding and icon, a new MSIX `Publisher`, a new package name and package
   family name, and a new WinGet and Microsoft Store identity.
2. **The name is decided separately.** The name is TBD until brand research and trademark
   clearance complete ([BRAND-902](../BACKLOG.md)); the PO makes the final choice. Candidate
   names stay local/private: no candidate list is committed to this repository, to avoid
   squatting and premature disclosure.
3. **The 1.x line becomes the bridge/legacy line.** Existing 1.x releases stay published under
   the old ItsyCal identity, with their tags, assets and history. They do not upgrade in place
   to 2.0; their release notes and documentation point to the new package and the
   uninstall/reinstall migration. No further feature work is planned on 1.x beyond migration
   discoverability.
4. **Attribution without affiliation.** ItsyCal is credited only as the inspiration and as
   MIT-licensed upstream material. `LICENSE.txt` keeps the upstream copyright and permission
   notice unchanged, and README, package and Store text must carry an explicit line such as
   "This project is not affiliated with or endorsed by ItsyCal or its authors." ItsyCal may be
   named in text only to describe inspiration and license provenance; its name, icon and other
   brand assets are not used as product branding.
5. **One identity migration, under the new brand.** Trusted signing (SignPath Foundation or
   Certum OSS) and any Store listing are completed under the new `Publisher` and package
   identity, not the old one. After the name is cleared, reserve the new domains, GitHub
   organization/repository name, NuGet package IDs, WinGet `PackageIdentifier` and Store
   product identity.
6. **Internal technical names may stay.** Code namespaces, assembly names, CLSIDs/COM class
   IDs and similar internal identifiers may keep their `ItsycalForWindows...` names to avoid a
   risky, low-value mass rename. They are technical identifiers, not product branding: every
   user-visible string, package display name/description, icon, tray text and Store/WinGet
   metadata uses the new brand.
7. **The repository is renamed, not abandoned.** After the name is chosen, rename the GitHub
   repository to the product name and keep its history, issues, tags and 1.x assets; GitHub
   redirects the old repository URLs. The old name is not reused for a different project.

## Alternatives considered

- **Keep the ItsyCal name with an explicit disclaimer.** Rejected: the disclaimer reduces but
  does not remove the affiliation implication and the trademark/confusion risk; the product
  would still lack an owned domain, Store and WinGet identity, and would remain dependent on
  the upstream project's tolerance. It also ties discovery to a macOS product with no Windows
  application.
- **Rebrand later, at v3.0.0.** Rejected: v2.0.0 already performs the Publisher/PFN migration
  that trusted signing forces. Deferring the name would either ship a trusted,
  Store-distributed release under the old brand (the riskiest moment) or force a second
  identity break, second migration documentation and second user reinstall. Icon, domain and
  Store reservations also need lead time.
- **Stay a fork under the upstream brand.** Rejected: the repository no longer contains or
  tracks the macOS code and the Windows UI and roadmap are independent; "fork" would
  misrepresent the code relationship, still require upstream coordination for branding, and
  provide no path to an owned WinGet/Store identity.

## Migration plan

1. **Decide the name (BRAND-902).** Run a private shortlist and knockout search, then
   trademark clearance in the US and EU in Nice classes 9 (software) and 42 (software services
   and SaaS), plus availability checks for domains, GitHub org/repository, NuGet, WinGet and
   the Store. The PO selects the name; no candidate names enter git.
2. **Reserve identifiers.** Register the product domain(s) and redirects, the GitHub
   organization/repository, NuGet IDs (if Core is published), the WinGet `PackageIdentifier`,
   the Store product name/reservation, and a signing certificate whose subject matches the new
   `Publisher`.
3. **Produce original brand assets.** New icon, tray and package logos, Store tiles,
   screenshots and README header. Do not derive them from the ItsyCal icon; keep the upstream
   asset only in the third-party notices/provenance text.
4. **Change the package identity.** Update `Package.appxmanifest` `Identity/@Name` and
   `@Publisher`, display name/description and package assets, accept a new PackageFamilyName,
   and set the package version to 2.0.0.0.
5. **Sign and submit under the new identity.** Complete trusted signing with the new
   Publisher, then the WinGet submission under the new identifier; a Store listing, if
   pursued, uses the same identity. Trusted/Store signing is not completed for the old brand.
6. **Document the migration.** README, release notes and the old repository state that 1.x
   cannot upgrade in place: uninstall the old package, install 2.0, and keep local data.
   Retain the existing `%LOCALAPPDATA%\ItsycalForWindows` folder as a technical path where
   practical so 1.x settings and `.ics` files remain readable, or document a one-time copy if
   the path changes.
7. **Rename and redirect.** Rename the repository, keep the 1.x tags and release assets
   published, put the migration notice in the old README/release notes, and leave the 1.x
   WinGet entry as the legacy/bridge package while 2.0 publishes under the new identifier.

## Consequences and risks

Positive:

- The product owns its identity across MSIX, WinGet, Store, domains and the repository, with
  no implied affiliation or dependence on the upstream brand.
- The signing-forced identity migration and the rebrand happen once, together, so `Publisher`,
  certificate subject and store identity are aligned from v2.0.0.
- The Windows product can define an icon and visual identity that match its own UI and
  roadmap.
- Clear attribution keeps the MIT license satisfied and gives upstream credit without
  implying endorsement.

Negative:

- The "ItsyCal" name recognition and search continuity are lost; the new name starts without
  discovery equity.
- Legal clearance (US/EU classes 9/42), domain/org/NuGet/WinGet/Store reservations and new
  brand/icon design add cost and lead time before v2.0.0 can ship.
- Existing 1.x users must uninstall and reinstall; some will not follow the migration, and
  support covers two identities during the transition.
- Release, update, CI, WinGet and Store metadata all change; a mis-sequenced identity change
  can break signing or updates.

| Risk | Mitigation |
|---|---|
| The chosen name conflicts with an existing mark | Clearance in classes 9/42 before any reservation or announcement; keep a fallback shortlist; no candidates in git. |
| Users cannot find or trust the new product | Migration notice in the old README/release notes and 1.x docs; GitHub/domain redirects; a clear lineage line in WinGet/Store text. |
| The identity break breaks upgrades or signing | Treat v2.0.0 as a clean install; never claim cross-identity upgrade; align Publisher, certificate subject and manifest before the first trusted build. |
| Name clearance delays the trusted release | Start research and clearance now, before the signing window closes; the fallback is to keep 1.x as the bridge and ship v2.0.0 only when the identity is coherent, not to ship a trusted 1.x under the old brand. |
| Internal rename churn | Keep namespaces/CLSIDs and brand only user-facing surfaces, as decided above. |

## Status

Accepted by the PO on 2026-11-08. The product name is **TBD** and is a separate
brand-research/clearance decision; v2.0.0 packaging and Store/WinGet identity work stay
blocked until the PO chooses a cleared name. Tracking: [BRAND-901](../BACKLOG.md) (P0,
v2.0.0 rebrand and identity migration) and [BRAND-902](../BACKLOG.md) (P1, name
research/clearance/IDs).
