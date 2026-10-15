# RangeDesk v5 — Approved visual implementation contract

Status: **approved by product owner** (15 Oct 2026); code implementation in progress.

## Non-negotiable visual reference
The approved screenshot in the project conversation is the source of truth, not the previous v3 or v4 window. Keep the compact Windows-native download-manager desktop shape: deep navy sidebar and application canvas, cobalt/azure buttons and selection, clear light text, thin dividers, dense rows, subtle rounded panels, real data for all transfer metrics. Do not introduce unrelated dashboards, large cards, marketing copy, or IDM-derived decoration.

## Required main window
1. Left fixed navigation with Downloads, Completed, Queue, Scheduled, grouped Categories, grouped Labels, Site Presets, Automation, and Settings. Allow collapsing to icons while preserving accessible names.
2. Top command bar: combined search/paste, Add Download, Resume, Pause, Stop, quick controls, and settings. Disabled states must reflect command availability.
3. Compact sortable/filterable list with checkbox selection, index, filename/source host, state, real percentage/progress, transferred/total, current speed, ETA, active connections, and added time. Selection and scroll position must not reset when progress snapshots arrive. Unknown-length transfers must not fabricate percentages or ETAs.
4. Bottom inspector bound to selected real transfer: Overview / Connections / Segments / Graph / Log. No synthetic speeds or retry metrics.
5. New Download dialog: General, Advanced, Rules, Automation tabs. URL, path, filename, checksum, bounded connection count, bandwidth limit, category and labels where implemented. Explicitly unavailable features must be disabled, marked planned and excluded from release promises.
6. Keep responsive at 1280x720 and 1920x1080, 100–200% Windows DPI, keyboard navigation, visible focus states, readable 4.5:1 contrast where practicable, no clipped controls.

## Delivery stages
- v5-a: visual shell, theme tokens, compact real list, top actions, collapsible navigation.
- v5-b: real-time inspector and New Download dialog, preserving v4 engine contracts.
- v5-c: categories, labels and persistent queue ordering, then scheduling/rules with dedicated tests.
- v5-d: browser capture integration only with explicit permission model and browser-specific tests.

## Test gates
- All original v4 loopback and checkpoint tests pass.
- Pause/cancel/resume/retry, stale ETag, bad Content-Range, disk collision and durable checkpoint recovery behave exactly as before.
- Snapshot UI never invents active connections, progress, ETA, speed or checksum verification.
- Screenshot comparison and manual Windows smoke checks before any v5 release.
- No merge to main until CI and visual/device acceptance are both recorded.
