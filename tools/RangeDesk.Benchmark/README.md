# RangeDesk real-server benchmark

The CI-only job uses Cloudflare's public speed-test download API:
`https://speed.cloudflare.com/__down?bytes=8388608`.

Runs the **actual RangeDesk download engine** on the Windows hosted runner once for each of 1, 4, and 8 requested connections, downloads at most 8 MiB per case (24 MiB total), records real transferred bytes, download duration, average MiB/s, process CPU time, observed worker count and SHA-256 of the received file. It validates the expected length; cross-run checksum equality is only reported as an observation, not assumed.

This is a public-CDN smoke benchmark, not a rigorous proof of speedup. It cannot simulate artificial stalls, reproducible baseline server load, disk I/O under stress, or real Windows 11 user-network characteristics. If the CDN lacks strong ETags / validated byte ranges, RangeDesk deliberately falls back to one connection: **never claim parallel-speed gains from that case**. Network failures are recorded as unavailable and do not break product CI.

Repeat under comparable conditions with a **user-controlled HTTP range server** and prepublished checksum for controlled benchmarks. Do not hammer free public test endpoints.
