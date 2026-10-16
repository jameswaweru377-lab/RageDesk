# RageDesk V6 — Reliable recovery foundation

## Objective
Make completed files trustworthy and interrupted downloads recoverable without
relying on UI-only progress, unsafe range reuse or an AI-generated build claim.
This is a reliability milestone, not an automatic speedup claim.

## Acceptance criteria
1. Fixed and adaptive checkpoints restore actual persisted bytes and total size
   after app restart; invalid overlaps, wrong URLs or oversized pieces never
   become displayed progress.
2. Pausing or failing segmented transfers preserves checkpoint directories and
   refreshes the displayed progress from actual saved lengths.
3. Single-stream HTTP transport retries bounded recoverable failures (408,
   429, 500, 502, 503, 504, read stalls and premature EOF), honoring Retry-After
   and resuming only with a strong validator and valid Content-Range.
4. Nontransient 4xx, changed validators and invalid ranges are not silently
   retried; no partial or unverified file is published as complete.
5. Fixed segmented HTTP responses that never provide a clean EOF do not succeed
   just because the expected byte count was written.
6. Adaptive donors flush the already received prefix to disk before persisting
   a changed piece boundary and giving the suffix to another worker.
7. Windows CI .NET tests, browser regressions, and portable packaging pass.
   Actual Windows 11 interruption and benchmark checks remain manual gates.

## Scope decisions
- Preserve all existing checkpoint formats and keep adaptive mode opt-in.
- Do not modify the in-flight browser media download path.
- Do not add cloud telemetry, external model calls or privacy-sensitive logging.
- No branch merges or production release in this milestone.

## Following milestone
Instrument a controlled localhost server with repeatable slow-worker,
high-latency, per-host throttling and failed-read scenarios, then implement a
guarded automatic connection tuner only if its gains and failure rates justify
default enablement. Never claim 10x gains without paired measurements.
