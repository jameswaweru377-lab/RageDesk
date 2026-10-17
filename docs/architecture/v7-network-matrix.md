# RageDesk V7 network matrix — opt-in performance verification

Purpose: establish which connection counts improve **total verified throughput**
under controlled network/server constraints before changing user defaults.

## Lab protocol
This exercises the **actual** .NET DownloadEngine against an immutable,
SHA-256-checked file served by a real HTTP/1.1 TCP server on 127.0.0.1.

- 1, 2, 4, 8, 16 and 32 connections under a **per-connection cap**.
- The same six counts under a **shared aggregate bandwidth cap**. Expect
  saturation instead of 6x/32x gains, and do not require benchmark ratios to
  pass CI.
- The same six counts under fixed response **latency and deterministic jitter**.
- 1, 8 and 32 connections under a deliberate **TCP connection drop**; verify
  bounded retry and identical published bytes.
- 1, 8 and 32 connections with **pause + new-engine resume**, verifying
  checkpoint integrity and output hash.
- Preserve each scenario's exact observed range count, worker count, run time,
  file size, SHA-256 and any interrupted run status in JSON.

### Acceptance and negative cases
1. All 24 runs finish within bounded time. Each output exactly matches its
   known length and SHA-256; no existing destination is overwritten.
2. For requested connections greater than 1, observe actual HTTP range
   requests instead of silently counting connections on a fallback path.
3. A deliberately dropped response is detected exactly once, and recovery
   publishes no corrupt or partial final file.
4. After an intentional pause, the saved segment/direct journal is present;
   a new engine can resume safely using the same local test URL and strong ETag.
5. A failed case causes CI failure, with completed cases written to the
   checkpointed JSON report for diagnosis.

### Scope and nonclaims
This is **not** a comparison against IDM or proof of improved download speed
on a user's ISP. HTTP chunk delays approximate throttling; artificial jitter
is **not IP packet loss**. Deliberate TCP disconnection tests recovery, but
does not model packet reordering, congestion windows, true random loss or Wi-Fi.
A later external test can add Linux network namespace + tc/netem or a user-owned
router if environment permissions permit. Avoid running unsolicited high-volume
traffic against third-party test services.

V7 tuning remains **disabled**. Do not merge or release until the test report
and existing Windows/native-host/browser CI pass and any failures are resolved.
