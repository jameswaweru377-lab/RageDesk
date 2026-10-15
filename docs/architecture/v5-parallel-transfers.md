# RangeDesk v5 — Connection and transfer architecture

## Sources and provenance
This is an original .NET HTTP/HTTPS implementation informed by the user's historical RangeDesk v4 source snapshots, `2026-10-15-range-engine-plan.md`, and `RangeDesk_v4_release.md` in their Google Drive. No IDM proprietary implementation, disassembly or executable bytes are copied into this repository.

## Implemented in current draft branch
- 1, 2, 4, 8, 16, or 32 *requested* HTTP range connections per download; default in the New Download Advanced tab is 8.
- HTTP GET `Range: bytes=0-0` probe. Parallel mode requires 206, exact `Content-Range`, known size, identity content and a **strong ETag**. Neither `Accept-Ranges` alone nor mere Content-Length authorizes segmented mode.
- Each worker owns one fixed disjoint byte-range file in `<target>.rdsegments/`. A durable manifest records URL, total size, ETag and worker count. Resuming requests only its missing suffix and validates returned `Content-Range`, total and ETag.
- Each stream is capped at its assigned end; too-short and too-long HTTP responses are rejected. Worker failures cancel the group, preserve incomplete segments and never publish a partial destination as complete.
- The assembled `<target>.rdassembling` file is checked for length, flushed, and atomically moved to the final path without overwrite. Segments are removed only after success.
- Existing v5 single-stream partials retain precedence. When range support or a strong validator is absent for a new transfer, the engine falls back to one stream. The actual active worker count appears in the UI.
- One `SharedBandwidthLimiter` is shared by all workers of the same download. Optional per-download limits are selected in Advanced; zero means unlimited.
- The GUI still computes speed and ETA from measured traffic via the rolling rate estimator; it never assumes all requested workers were opened.

## Test scope
Controlled .NET HttpMessageHandler fixtures test exact bytes for multiple workers, resumed segment offsets, mismatched validator rejection, 200/direct fallback, 16- and 32-range coverage and SHA-256 output equality. Shared limit tests verify a shared budget, cancellation, and unlimited mode. These tests do not prove speed gains on particular public servers; field testing remains necessary.

## Remaining engineering work
- V4-style idle-worker slow-tail splitting, Retry-After respecting transient 429/503 responses, finite adaptive retries.
- Durable reorderable queue, scheduled starts, per-site presets and rules; these require persistent local schemas and migration tests.
- Per-worker diagnostic history, bandwidth graphs, richer segment inspector and optional user-supplied checksum verification.
- Browser capture for Edge/Chrome/Firefox, optional credentials and proxy support only with a separate security and user-permission model.
- Real-device Windows 11 layout testing and external server benchmarks before stable release.

Do not equate number of requested connections with a promised number of active connections or guaranteed faster throughput.
