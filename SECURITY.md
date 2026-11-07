# Security policy

## Reporting a vulnerability

Please report security issues privately through
[GitHub's private vulnerability reporting](https://github.com/Folieson/itsycal-for-windows/security/advisories/new)
rather than a public issue. Include the affected version (see the release page), Windows and
PowerToys versions, and the smallest reproduction you can share.

Do not include calendar event titles, attendee names, or other personal calendar data in a
report. The extension logs no event content by design; describe the event shape instead
(for example "an all-day event spanning two days").

## What the extension does with data

- Reads events through the read-only Windows appointment store
  (`Windows.ApplicationModel.Appointments`) for the visible month only.
- Never writes to the calendar, never makes network calls, and sends no telemetry.
- Logs contain no event content; the settings file stores only display preferences.

## Release signing

Development pre-releases are signed with an ephemeral self-signed certificate whose public
key is attached to the release as `ItsycalForWindows_<version>_dev.cer`. The private key
exists only inside the CI runner and is deleted before the artifacts are published. Public
trusted-signing releases will be announced on the release page; until then, treat a
self-signed build as a development build that requires explicitly trusting the attached
certificate.

## Supported versions

Only the latest release receives fixes. Because the Command Palette extension API is young,
older builds may stop working after PowerToys updates; upgrade to the latest release first.
