# RageDesk frontend renderer migration — dashboard slice (2026-10-17)

**Status:** authorized technical direction; first dashboard preview only. **Not approved:** removing WPF, merging, releasing, cloud dependencies, remaking approved layouts, changing download/capture behavior.

## Audited baseline

- Based on `feature/ragedesk-approved-ui-20261017` at `476bf846`, which contains the currently approved dashboard and RageDesk Pro popup. The separate draft PR #40 and `main` remain untouched.
- .NET 10 WPF `MainWindow` creates and owns `DownloadEngine`, restores local catalog and settings, services automatic browser-capture inboxes, scheduler and queue, and owns progress windows. Do **not** instantiate a second MainWindow/engine.
- `MainWindow.xaml` and `V5Theme.xaml` define the approved layout. Window sidebar width 244 WPF DIPs; dark cobalt/navy; top toolbar; 4 summary cards; compact status filters and download table; bottom 3 sections (Network & Performance, Top Sources, Recent Activities). RageDesk Pro popup remains WPF and is **not** migrated in the dashboard slice.
- `RangeDesk.Core`, `RangeDesk.NativeHost`, scheduler companion, browser extension, installer ID and `%LOCALAPPDATA%\RangeDesk` paths remain unchanged.

## UI migration map

| Existing UI | First React dashboard | Later migration |
|---|---|---|
| Sidebar navigation, collapsible category/sites, queue badges | Faithful layout, local client-side selection/filtering; no C# actions | Full navigation commands / WPF parity |
| Search/add link/new download and resume/pause | Search client-side; commands visibly disabled in read-only preview | Explicit validated native command routes |
| Four metric cards | Live read-only C# snapshot from existing MainWindow | Smooth reconciliation with real notifications |
| Download table, progress status, sorting | Live snapshot, local sorting/filtering, no synthetic speed | Native-backed actions, selection, context menus |
| Network and performance, sources, activity | Live C# aggregate/selected download sample points | Native graphs/details parity |
| Settings, scheduling, automation, site groups | Existing WPF remains canonical | Move once a parity matrix passes |
| Separate popup, media confirmation/browser capture | Existing WPF remains canonical | Migrate later, independently |

## Incremental architecture

- `frontend/`: Vite + React + strict TypeScript + CSS. No CDN, remote fonts, analytics, remote images, or external runtime HTTP calls. Build assets are bundled into Windows publish at `web/`.
- Optional separate WPF window hosting WebView2, launched **only** using `--react-preview` while the existing `MainWindow` continues to own services. Default app remains unchanged; WebView2 missing/broken should not damage original UI.
- Local virtual host `https://ragedesk-app.example/` maps to bundled read-only files (reserved `.example` name avoids `.local` delays). Outbound navigation, popups and permissions are blocked, no devtools for preview. Versioned JSON envelope; only `dashboard.ready` client message is accepted in this slice and its source/origin is validated.
- Read-only snapshots include approved visible summary statistics, 500 maximum downloaded items, safe file names/hostnames and observed progress/worker totals. Do not send download URLs (which may contain tokens), local file paths, cookies, signed query strings, or user/browser credentials.
- Throttled UI refresh ≤ 2 updates/s only while optional preview visible. UI-only search/filter/sort never writes disk/network state.
- No duplicated engine, no modifications to queue, scheduler, checkpoints, native messaging, direct/YouTube capture, catalog or saved preferences.

## Acceptance gates and negative tests

1. `npm ci`, `npm run typecheck`, `npm run build` and production asset bundling work on CI Windows. The built dashboard is fully offline.
2. `dotnet test` and `dotnet publish` with WebView2 dependency pass, including native-host/browser-capture checks.
3. Preview never executes a transfer command; malformed/non-v1 or foreign-origin JS message cannot invoke privileged native actions.
4. If WebView2 Runtime or assets are absent, existing WPF remains usable. `main` defaults to WPF.
5. Dashboard preserves navigation order, sidebar/content arrangement, summary card labels, filters, download data columns and three bottom panels; no live fake samples.
6. Manual Windows QA: compare screenshots with approved design at 1280×720 and 1920×1080, 100/125/150/200% DPI, both themes, 0/1/500 downloads, very long file names, unknown sizes and paused/failed/completed transfers.
7. Measure cold/warm startup, idle and active CPU/RAM, progress update latency and memory under ≥100 rows. Do not claim parity or visual acceptance solely from CI.
8. No merge or release without subsequent authorization.

## Later decisions

- Confirm whether to host the final WebView2 dashboard in the existing main window (rather than a preview) after parity testing.
- Theme/settings, popup and command migration occur separately. Bridge command allowlist, confirmation and transactional error flow require review and tests before enabling writes.
- A change to the visual specification, remote-logo lookup, telemetry or cloud storage needs explicit approval.
