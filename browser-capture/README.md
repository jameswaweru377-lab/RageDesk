# RangeDesk Browser Capture — opt-in preview (v0.2)

RangeDesk supports Chrome, Edge and Firefox desktop browser extensions through
the existing native messaging bridge. The approved Windows GUI is unchanged.

Two modes are available:

- **Manual (default):** right-click a direct link and choose **Download with
  RangeDesk**. The RangeDesk Add Download screen opens for your review.
- **Automatic (explicitly opt in):** click the RangeDesk extension toolbar
  icon to display the blue **ON** badge. While the RangeDesk app is **already
  running**, a recognized direct-file download initiated in your browser is
  paused. The extension asks the local native host to hand it to the GUI.
  Only if the GUI creates, persists and starts the transfer does the native
  host acknowledge and allow the browser copy to be canceled. If not, the
  browser resumes its original download. Click again to disable.

Automatic capture is **off on first installation**. Its ON/OFF state is local
to each browser profile; no telemetry, remote service or cookies are used.
The native host does not launch hidden services, scrape pages or extract media.

## Setup on a Windows computer

1. Extract the entire RangeDesk-Preview-win-x64.zip together. Keep
   RangeDesk.exe, NativeHost/ and BrowserCapture/ folders in place.
2. For Chrome: open chrome://extensions. For Edge: open edge://extensions.
   Enable Developer Mode, select **Load unpacked**, and select the
   BrowserCapture/chromium directory. Copy the extension's 32-character ID.
   For Firefox preview: temporarily load BrowserCapture/firefox/manifest.json
   using about:debugging. Public distribution requires Firefox signing.
3. From the extracted folder in your own PowerShell session, register the
   host with:
     - .\BrowserCapture\setup.ps1 -ChromeExtensionId YOUR_EXTENSION_ID
     - .\BrowserCapture\setup.ps1 -EdgeExtensionId YOUR_EXTENSION_ID
     - .\BrowserCapture\setup.ps1 -Firefox
   Chrome/Edge IDs must be 32 characters a-p. No admin privileges normally
   required. Follow device policies if PowerShell execution is restricted.
4. Restart browser and open RangeDesk. Click the RangeDesk extension toolbar
   icon to enable auto capture. The ON badge confirms the setting. Eligible
   direct-file downloads can now be handed off without copying a link.
5. Click toolbar icon again to disable. Manual capture continues to work.
   Remove outdated native-host registration when moving/uninstalling the app.

## Safety and supported downloads

Eligible examples are direct .zip, .7z, .rar, .exe, .msi, .iso, .pdf, Office
documents and common standalone video/audio and image file URLs. Signed URL
queries can work if the **URL path** has a supported extension. All other
downloads continue in the browser.

- Browser pause comes first. Native host replies accepted only after the GUI
  starts a matching download, not merely after a URL enters an inbox.
  Rejection, timeout and missing native host resume the browser download.
  The handoff expires after 20 seconds; extension/native-host crashes may
  require manually resuming a paused browser download.
- Accepted start does not guarantee eventual success. Authenticated, single-
  use or protected downloads can fail in RangeDesk without session data.
  No browser credentials are silently forwarded.
- Extensions transmit only the direct download URL and action. No cookies,
  Authorization headers, page contents, full history, or downloaded bytes.
  URLs can contain sensitive signed queries, stored only in the local Windows
  user profile; avoid using on shared/untrusted accounts.
- No blob:/data: URLs, intercepted HTTPS bodies, DRM/HLS/DASH, password-
  protected files, universal media extraction, or untested browser families.
- Automatic filenames derive from URL paths and can differ from Content-
  Disposition names. Existing completed files and partial checkpoints are
  never overwritten. Generic /download?id=... URLs stay in the browser;
  manual context-menu capture remains available.
- A download that finishes before it can be paused stays in the browser.
  If cancel fails after the GUI starts, the extension attempts to resume
  the browser copy; the user should check for duplicate downloads.

## Verification and distribution status

Windows CI runs .NET tests, native-host protocol smoke checks, and mocked
Chrome/Firefox download-event tests via Node.js. These confirm intended
acknowledgment, browser fallback, and URL eligibility in isolation, NOT
real browser installation, service worker or user acceptance.

Before public release test Chrome/Edge/Firefox extension installation,
native manifest registration, permissions, MV3 service-worker lifetime,
completed-before-pause races, redirects, single-use/authenticated links,
closed app and restart behavior, session lock and download integrity.
This is a development preview, not universal production-certified capture.
