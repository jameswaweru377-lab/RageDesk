# RangeDesk Browser Capture — opt-in development preview

This is a **manual, user-initiated direct-link handoff**, not automatic browser takeover. It only sends the HTTP/HTTPS URL when the user right-clicks a link or direct video/audio source and selects **Download with RangeDesk**. No page scanning, cookie extraction, authentication header forwarding, DRM circumvention or background browsing observation is implemented.

## Manual setup on Windows 11

1. Extract the complete `RangeDesk-Preview-win-x64.zip`. Keep `RangeDesk.exe`, `NativeHost/`, and `BrowserCapture/` together; do not move or delete the NativeHost executable after registration.
2. For Chrome/Edge, visit `chrome://extensions` or `edge://extensions`, enable Developer mode, **Load unpacked**, and select `BrowserCapture/chromium`. Copy the shown 32-character extension ID. For Firefox testing, use `about:debugging` to load the temporary `BrowserCapture/firefox/manifest.json`. Distribution requires normal Firefox add-on signing.
3. In PowerShell, run from your extracted package: `./BrowserCapture/setup.ps1 -ChromeExtensionId <32-character-id>` (or `-EdgeExtensionId <id>` or `-Firefox`). The setup validates the ID and registers an HKCU native host manifest (no administrator rights). For a restricted execution-policy machine, use an appropriately approved, temporary process-level policy, or register the manifest manually; do not weaken device-wide policy.
4. Restart the browser. Right-click a direct download link or a direct media file. Select RangeDesk's context-menu action. Launch RangeDesk if necessary: the queued URL will appear in **Add Download** for human review. Confirm filename and download manually.

## Boundaries and security

- Native host accepts **one** UTF-8 JSON message, max 8 KiB, with `{"action":"add_link","url":"https://example.com/file.zip"}`. The native-messaging stream uses the standard four-byte little-endian length prefix. The host validates direct HTTP(S) URLs without embedded userinfo, then atomically queues them in the current user's local RangeDesk directory.
- The RangeDesk GUI reads queued links in small batches. **It never starts a transfer from a browser message automatically.** Pending URLs, including signed query parameters, are held briefly in the local user profile and deleted after pickup; do not use on shared or untrusted Windows accounts.
- This can only capture direct `http://` and `https://` downloads. It cannot capture protected HLS/DASH/DRM streams, browser-authenticated sessions, content not exposed as direct media URLs, or website crawling.
- A changed extension ID requires re-running setup; Chrome and Edge must be registered separately. Browser-specific native manifests restrict callers to the configured extension ID. Do not distribute private browser data or log full signed URLs.
- Windows CI can compile and exercise URL acceptance tests; **real browser-installation, native-messaging, browser compatibility and Windows permission tests are still required** before a public release.
