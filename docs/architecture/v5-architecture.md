# RangeDesk v5 — implementation architecture

## Baseline and provenance
Recover independent C#/.NET 10/WPF v4 source from checksum-pinned owner archive. GitHub recovery job must validate SHA-256 before importing. Original v4 engine logic is authoritative for transfer correctness; IDM reverse-engineering evidence is historical research only and must not be copied into implementation.

## Component architecture
- **RangeDesk.Core**: immutable download snapshots, download controller, interval planning, HTTP probe, range transport, durable state, and safe progress. No WPF dependency.
- **RangeDesk.App**: WPF shell, theme dictionary, virtualized compact list, command toolbar, selected item inspector, import/download dialog, Settings.
- **Metadata and rules subsystem (proposed)**: separate local stores for categories, labels, queued items, schedules, site presets, and user-defined rules. Version these schemas and migrate without breaking v4 checkpoints.
- **Browser integration (future)**: opt-in per browser with native-messaging host; require independent security review before enabling. Do not intercept browser activity or credentials by default.

## Binding and performance rules
- UI consumes immutable core snapshots; dispatcher marshaling is controlled in the view model.
- Reuse row objects keyed by download ID; preserve selection, filters, and scroll position across progress updates.
- List virtualization and 150–300 ms visual throttling for large queues; underlying transfer progress and journal accuracy remain independent of UI frequency.
- Show indeterminate progress when content length is unknown; display active worker count, not user-configured maximum as if measured.
- No network or filesystem operations on WPF dispatcher thread.

## Transfer correctness gates
- A valid 206 and matching Content-Range are required for range writes.
- Never merge partial bytes from different ETag/last-modified representations.
- Atomic durable checkpoints are written only after data flush.
- Final destination is never overwritten silently.
- Retry/backoff is finite; Stop, Cancel, Pause, Resume produce distinct tested state transitions.
- Connection setting v4 is 1–8; proposed up-to-16 requires transport tests and rate/retry regression tests.

## Security and privacy
- Local-first; download list/settings remain on device.
- Do not log query strings, cookies, Authorization headers, site tokens, or file contents.
- No cloud sync, browser monitoring, telemetry, or credential storage without separate authorization.
- Reject unsafe path resolution and protect against redirect-to-untrusted-origin credential forwarding.
- Proprietary IDM binaries, original analysis dumps and local work/cache folders are not included in public GitHub repository.

## CI/release
- Windows runner using global.json SDK. Restore, test and publish portable win-x64 self-contained build.
- Save TRX reports; artifacts for preview only after tests pass.
- Verify real WPF layout and installer behavior on Windows laptop before release.
- Never merge/release based only on screenshot or AI build claim.

## Current stage
Source recovery/CI setup, theme tokens and v5 design contract being staged on recovery branch. v4 code import and successful Windows CI remain required before integration/release.
