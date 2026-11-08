# Signing and SmartScreen for the standalone flyout

Status: reference plan, 2026-11-08. Companion to [docs/SIGNING.md](../SIGNING.md); covers `ItsycalForWindows.Flyout.exe` and the SmartScreen prompt.
## 1. Why the prompt appears
The flyout is published **unpackaged** ([windows-build.yml](../../.github/workflows/windows-build.yml#L222)):
users download a folder with `ItsycalForWindows.Flyout.exe`, not an MSIX. Browsers mark
downloads with Mark-of-the-Web (`Zone.Identifier`); Explorer-extracted zips usually keep it.
SmartScreen then checks **publisher reputation** (signed by a known publisher?) and **file-hash
reputation** (has this binary been downloaded cleanly?) -
[Microsoft: SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).
The dev certificate is self-signed and trusted only on dev machines, so elsewhere the chain is
untrusted and the exe is "unknown publisher"; Microsoft's table says **self-signed behaves the
same as unsigned**.

| Dialog | Trigger | Relevance |
|---|---|---|
| UAC "unknown publisher" | binary asks for administrator rights | not expected; the tray app does not elevate |
| SmartScreen "Windows protected your PC" | MOTW + low publisher/hash reputation | **the observed prompt**; unsigned/self-signed = "Unknown publisher" |
| MSIX `0x800B0109` | certificate does not chain to a trusted root | MSIX path only |
| Microsoft Store install | Store identity and re-signing | no SmartScreen; not used yet |

Signing fixes tamper evidence, a stable publisher identity, the verified publisher name, and
MSIX install trust. It does **not** create reputation: Store = no warning; OV/EV = warning on
first download until reputation; unsigned/self-signed = "Windows protected your PC"; EV no
longer bypasses SmartScreen, so do not buy EV for this. It also does not bypass Mark-of-the-Web,
Smart App Control (which can block unsigned files even without MOTW), or policy. There is no
consumer reputation-submission shortcut (enterprise-only via Microsoft Security Intelligence),
and a local `Get-AuthenticodeSignature = Valid` only proves the dev certificate is trusted there.
## 2. Distribution options

| Option | What users see | Cost | Effort (est.) | Verdict |
|---|---|---|---|---|
| (a) Raw exe/zip | "Unknown publisher" on every new hash; zip does not remove MOTW | $0 | none | **reject** |
| (b) MSIX sideload, trusted cert | installs after the cert is trusted; first downloads can still prompt | cert cost | packaging + cert (1-2 wk) | fallback |
| (c) WinGet `msix` | delivery, not trust: Windows still requires a trusted package signature (untrusted -> `0x800B0109`); removes the browser/MOTW path | $0 | manifest + signing (1 d) | good once signed |
| (d) Microsoft Store (MSIX) | **no SmartScreen**; Store re-signs and hosts updates | $0 for individuals ([free registration](https://learn.microsoft.com/en-us/windows/apps/publish/whats-new-individual-developer)) | packaging 1-3 d + certification 1-2 wk | **primary** |
| (e) SignPath Foundation | trusted signature; publisher displays "SignPath Foundation"; first downloads may still prompt | $0 for eligible OSS | application 2-6 wk + CI 1 d | **fallback** |
| (f) Azure Artifact Signing (ex Trusted Signing) | trusted signature; reputation accumulates; first downloads may still prompt | ~$10/month Basic | identity validation 1-2 wk + CI 1 d | if eligible |
| (g) Certum OSS / other OSS programs | trusted OV signature; same first-download caveat | free/discounted; verify | identity verification; verify | if eligible |

- **Store is the only documented no-SmartScreen path.** Store packages are re-signed by Microsoft
  and "never subject to SmartScreen download warnings"
  ([SmartScreen doc](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)).
  Direct-download MSIX is not; Microsoft says to expect prompts for a new certificate
  ([distribution paths](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path)).
  Verify Store availability for the maintainer's market.
- **SignPath Foundation** is the best $0 non-Store option, but its certificate is issued to
  SignPath Foundation (it becomes publisher), the project needs verifiable reputation, and each
  release needs manual approval ([terms](https://signpath.org/terms)); it signs MSIX and requires
  `Publisher` to match the certificate subject (verify the exact DN)
  ([reference](https://docs.signpath.io/artifact-configuration/reference)).
- **Artifact Signing** is ~$10/month Basic, no hardware token, short-lived certs, GitHub Actions
  support; eligibility is organizations in the US/Canada/EU/UK (3+ years of tax history) and
  individuals in the US/Canada
  ([MSIX signing doc](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)).
  Verify eligibility; Certum's OSS program exists in card/cloud variants; verify its terms and price.
- **WinGet does not create trust.** A `portable`/`zip` entry ships the same binary; only `msix`
  gets package-signature enforcement, making a trusted certificate mandatory. `SignatureSha256`
  is the hash of the signature block
  ([schema](https://github.com/microsoft/winget-cli/blob/master/schemas/JSON/manifests/v1.28.0/manifest.installer.1.28.0.json)).

**Recommendation:** primary = Store MSIX (free); fallback = SignPath Foundation (free) with the
same MSIX; paid Artifact Signing only if the free paths fail and the PO approves ~$10/month.
WinGet should consume the Store listing or signed MSIX; until one ships, publish no raw exe.
## 3. What to change in this repo
### 3.1 Fix the publisher identity first (blocking)
The manifest uses `Publisher="CN=ItsycalForWindows-Dev"`
([Package.appxmanifest](../../windows/src/ItsycalForWindows/Package.appxmanifest#L13)), matching
the dev certificate. Store/SignPath/CA certificates have different subjects, and MSIX requires
`Publisher` to equal the certificate subject; the package family name derives from
`Name + Publisher`. Every trusted option is a **one-time identity migration**: pick the final
Publisher (full `Subject`, not just CN), update both manifests, regenerate the PFN and
`winget/Folieson.ItsycalForWindows/*/...installer.yaml`, and publish a migration note (uninstall
the dev package; `%LOCALAPPDATA%` survives). Make `Build-Release.ps1` **throw** on a subject
mismatch instead of warning ([Build-Release.ps1:277](../../windows/scripts/Build-Release.ps1#L277))
and mirror that gate in `release.yml`.
### 3.2 Package the flyout
Either fold the flyout into the existing extension MSIX (preferred if the tray process can run
with package identity) or add a second MSIX. A separate package needs its own
`Package.appxmanifest`, assets, and `Identity Publisher` equal to the final signer (Store needs
the Store-assigned identity). The project sets `WindowsPackageType=None`
([csproj](../../windows/src/ItsycalForWindows.Flyout/ItsycalForWindows.Flyout.csproj)); tray icon
and startup must be re-verified on an installed build.
### 3.3 CI signing and timestamping
- The flyout job now signs with the **persistent dev certificate** when `SIGNING_PFX_*` is
  available ([windows-build.yml](../../.github/workflows/windows-build.yml#L275)); fork PRs stay
  unsigned. The release job signs only the MSIX ([release.yml](../../.github/workflows/release.yml#L109)).
  Add *trusted* signing after publish and after MSIX packaging.
- Use a service, not an exportable PFX: SignPath's GitHub integration or
  `azure/trusted-signing-action` with GitHub OIDC. Since 1 June 2023 CAs may not issue
  exportable OV/EV keys; they must live on FIPS 140-2 Level 2 / CC EAL4+ hardware or an HSM
  ([DigiCert](https://knowledge.digicert.com/general-information/new-private-key-storage-requirement-for-standard-code-signing-certificates-november-2022)),
  so `TRUSTED_SIGNING_PFX_*` only fits a legacy certificate; new CA certs need a cloud/HSM signer.
- Timestamp every signature: `/fd SHA256 /tr <RFC3161 URL> /td SHA256` (the MSIX script uses
  DigiCert, [Build-Release.ps1:333](../../windows/scripts/Build-Release.ps1#L333)); timestamping
  keeps it valid after expiry
  ([MSIX signing doc](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)).
  The MSIX signature covers its payload; sign the exe separately only if it is also offered
  outside the package.
### 3.4 Verification gates (fail the release, do not warn)

```powershell
$sig = Get-AuthenticodeSignature .\ItsycalForWindows.Flyout.exe
$sig.Status; $sig.SignerCertificate.Subject; $sig.TimeStamperCertificate   # Valid / matches manifest / not null
signtool verify /pa /v /fd SHA256 .\ItsycalForWindows.Flyout.exe
signtool verify /pa /v /fd SHA256 .\ItsycalForWindows_<version>_x64.msix
(Get-AppxPackageManifest (Get-AppxPackage ItsycalForWindows)).Package.Identity.Publisher
```
Make CI fail unless a release tag has a `Valid`, timestamped signature with the expected subject,
and unless the signer thumbprint differs from the persistent dev certificate's (keep the dev
thumbprint public in the workflow/docs, never a private key). Fail if a release asset matches
`*flyout*.exe` outside a signed MSIX.
### 3.5 Release checklist
- [ ] `BUILD_KIND=trusted` (or a Store package) and the final Publisher identity.
- [ ] `Get-AuthenticodeSignature`/`signtool`: `Valid`, expected subject, timestamp present.
- [ ] MSIX `Publisher` equals the signer subject; `PackageFamilyName` matches WinGet.
- [ ] No `_dev.cer`, dev-signed artifact, or unsigned flyout exe on a public release.
- [ ] Clean Windows 11 VM: download from the public URL, run, record the actual prompt.
- [ ] WinGet: real `InstallerSha256`/`SignatureSha256`, `winget install` tested; Store: certification passed.
### 3.6 Development builds
Keep the persistent dev certificate (`SIGNING_PFX_*`): CI signs dev MSIX/exe with it and attaches
the public `_dev.cer`; the flyout job already does this
([windows-build.yml](../../.github/workflows/windows-build.yml#L275)). It makes local installs and
signature inspection reproducible but does **not** make SmartScreen quiet. Label the artifact
`-dev`, put the trust step and expected prompt in the notes, and never promote a dev-signed artifact.
## 4. User-facing behaviour and security
- **Dev build:** expect "Windows protected your PC - Microsoft Defender SmartScreen prevented an
  unrecognized app from starting", `Publisher: Unknown publisher`, `Don't run` / `Run anyway`
  (under "More info" on some builds). Trusting the dev certificate in `LocalMachine\TrustedPeople`
  installs/inspects the package but does not remove the prompt; tell testers to compare
  `SHA256SUMS.txt`, never disable SmartScreen or add Defender exclusions.
- **Public direct download:** the same dialog can appear for a new release, but with the verified publisher name; it disappears as reputation accumulates - do not promise a warning-free first run.
- **Store:** no SmartScreen dialog. **UAC:** not expected; if seen, the binary asks for elevation.
- **Security:** private keys only in CI secrets/HSM; no PFX/CER in the repo (`*.pfx`, `*.cer` are
  ignored); prefer OIDC over long-lived cloud secrets; restrict signing to tags and a protected
  environment. Track expiry, never re-sign published assets, and on a leak revoke/rotate/notify.
## 5. Ordered tasks and the PO decision
Blocking, in order:
1. Stop publishing the raw flyout exe/zip; mark the CI artifact "development only" and add the SmartScreen warning to the README.
2. PO chooses the channel (Store / SignPath / paid Artifact Signing) and approves the one-time Publisher/PFN migration.
3. Fix the identity: final Publisher (full DN), both manifests, WinGet PFN; subject mismatch becomes a build error.
4. Package the flyout as MSIX (or fold it into the extension MSIX) and verify install, tray icon, and startup on a clean VM.
5. Add service-based signing + timestamping to CI for the flyout exe and MSIX.
6. Add the verification gates (Valid, subject, timestamp, dev-thumbprint denylist, MSIX `signtool verify`, Publisher/PFN match) as release-blocking checks.
7. Regenerate the WinGet `msix` manifest (`InstallerSha256`, `SignatureSha256`, PFN) and test `winget install` on a clean VM.
8. If Store: create the Partner Center account, reserve the name, apply the Store identity, submit, and pass certification.

Polish:

9. Sign dev builds with the persistent dev cert, attach `_dev.cer`, and test the exact SmartScreen wording users see.
10. Update README/release notes and (for SignPath) add the required "Code signing policy" section and approver roles.
11. Add the rotation/incident runbook and scope secrets to the release environment.

**PO decision (cost/application):** choose **Store MSIX** (free; needs a Store listing and market availability), **SignPath Foundation**
(free; application, reputation, per-release approval, publisher "SignPath Foundation"), or **paid Artifact Signing/OV**
(~$10/month or ~$300-500/year; limited eligibility; old PFX path unusable). Until then: development-only, no public raw exe.
## Sources
- [SmartScreen reputation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation), [distribution paths](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/choose-distribution-path), [MSIX signing](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)
- [Free individual Store registration](https://learn.microsoft.com/en-us/windows/apps/publish/whats-new-individual-developer), [MSIX submission](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/create-app-submission)
- [SignPath conditions](https://signpath.org/terms), [artifact reference](https://docs.signpath.io/artifact-configuration/reference), [Azure Artifact Signing](https://learn.microsoft.com/en-us/azure/trusted-signing/), [DigiCert rule](https://knowledge.digicert.com/general-information/new-private-key-storage-requirement-for-standard-code-signing-certificates-november-2022), [WinGet schema](https://github.com/microsoft/winget-cli/blob/master/schemas/JSON/manifests/v1.28.0/manifest.installer.1.28.0.json)
