# RageDesk React renderer (first migration slice)

The UI in this folder is **not** a new product design. It reproduces the approved RageDesk cobalt dashboard from draft PR #40 in an offline Vite/React renderer.

## Local development

- Node.js 22.12+ (or another current supported LTS compatible with Vite 7).
- `npm install` and `npm run build` in `frontend/`; `npm run typecheck` validates TS.
- `npm run dev` previews layouts in a browser, but **no native host means no download data**. Fake transfer progress is never shown.
- To see real data on Windows, build `frontend/dist` first, build/publish `RangeDesk.App` normally, and launch `RangeDesk.exe --react-preview`. This opens an additional opt-in WebView2 window while WPF remains the working owner of the engine.
- Offline assets are copied to `web/` beside the publish executable. Production requires the shared Evergreen WebView2 Runtime. If it is missing, the preview displays an error and the original WPF app continues.

## First-slice limitations

- React dashboard is read-only. Search, status filters, sorting, category/site navigation, table/card view and sidebar expand/collapse are local UI interactions. Add Link, New Download, Pause/Resume, Scheduler, Automation and Settings remain in WPF.
- The existing WPF progress popup and browser capture/confirmation remain unchanged.
- The preview is **not enabled by default**. The native bridge accepts only version-1 `dashboard.ready`, returns capped safe read-only snapshots, and does not share download URLs, tokens or filesystem paths.
- Source/category counts in React reflect existing C# category/preset semantics. Recent Activities currently lists latest entries and observed status; it does not invent fine-grained historical transfer logs.
- Speed chart reflects the download selected in WPF; clicking a React row changes only React selection in this first slice.

## Release and validation

Do not merge into the default branch, replace the WPF MainWindow or claim visual parity until:
- front-end production build and existing Windows CI are green;
- approval screenshots are compared at 100/125/150/200% scaling in Windows;
- native command flow, duplicate events, startup performance and runtime missing fallback are validated;
- dependencies are locked with a committed npm lockfile before production release;
- user approves the subsequent migration slices.
