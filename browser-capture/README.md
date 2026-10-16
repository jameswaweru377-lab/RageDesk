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

## Chrome first: guided setup without the Chrome Web Store

Chrome's local **Load unpacked** installation remains a user action. Google
does not permit a normal Windows application to silently install arbitrary
unreviewed extensions. RangeDesk streamlines the remaining steps:

1. Download and extract the full latest Windows preview ZIP. Keep
   RangeDesk.exe, NativeHost/ and BrowserCapture/ together. Open RangeDesk.
2. Go to **RangeDesk Settings → Chrome browser** and choose
   **Open Chrome extensions**. In Chrome, enable Developer mode, click
   **Load unpacked** and select the folder shown by RangeDesk's
   **Open extension folder** button (BrowserCapture/chromium).
3. Return to **RangeDesk Settings → Chrome browser** and click
   **Connect Chrome**. This writes a current-user native messaging manifest
   and registry entry without PowerShell or administrator privileges.
   RangeDesk can check that this **registration** is in place; it cannot
   independently assert that Chrome has installed the extension.
4. Restart Chrome, pin the RangeDesk toolbar icon, right-click it and
   select **Check RangeDesk connection**. An **OK** badge means Chrome
   successfully contacted the RangeDesk native messaging host. A **!**
   badge means the connection failed; recheck the extracted package and
   the connection in RangeDesk Settings.
5. The toolbar icon click toggles automatic capture; a blue **ON** badge
   enables eligible direct-file takeover while the RangeDesk desktop app
   is running. It is **OFF by default** and right-click manual capture
   remains available. No store listing, developer fee or Google review
   is required for this development-only unpacked extension.

**Development extension ID:**
pknbhiklbdmhgdohieapjoipkdklahlp. The unpacked Chrome manifest includes
a **public** development key to keep its ID stable across extract locations;
no private key is included. Existing unpacked installations without this
key may have a different ID, so remove the older unpacked instance and
load the updated extension. A future signed Chrome Web Store release may
have a different ID and will need a registration migration.

**Uninstall:** Click **Disconnect** in RangeDesk Settings → Chrome browser
to remove only the RangeDesk-owned current-user native host registration,
then remove the extension in Chrome. The download catalog and checkpoints
are left alone. Moving the application folder invalidates the old native
host path; click Connect Chrome again in its new location.

## Chrome Phase 2: in-player "Download video" overlay (development preview)

The Chrome extension now looks for ordinary HTML5 video elements on permitted
HTTP and HTTPS webpages, including accessible embedded frames, and offers a
small **Download video** overlay when a source has a **direct**, usable URL.
The control offers only sources actually listed on the video element:

- Direct .mp4, .m4v, .webm, .ogv, .ogg and .mov URLs exposed in the current
  video or its source elements; relative URLs are resolved against the page.
- Extensionless HTTP(S) video endpoints only when their HTML5 source element
  explicitly declares a supported video MIME type (e.g. video/mp4).
- Duplicate sources are suppressed and source options are capped at eight.
  The displayed resolution is taken only from the **currently playing**
  video when its actual dimensions are available, not guessed for variants.
  No invented quality choices, transcodes, format conversions or 4K claims.
- Selecting a source sends that URL through the existing native messaging
  bridge into RangeDesk's **Add Download** review flow. Confirm and start it
  there. RangeDesk may be closed while the link is queued, but must be
  reopened to review it.

**Existing v0.3 users:** extract the new v0.4 preview ZIP, then use
chrome://extensions → RangeDesk Browser Capture → **Reload** if still
loaded from that same extension folder. If switching folders, remove the
old unpacked instance and load BrowserCapture/chromium from the new
extracted package. The development extension ID is unchanged, but
reconnect Chrome from RangeDesk Settings if the executable path moved.

**New site permission:** the extension now has content scripts matching
regular HTTP(S) pages to display the button inside the page, so Chrome
may display broad site-access permissions. Detection inspects only
HTML5 video element source attributes and player dimensions locally.
No browsing history, video bytes, cookies, authentication headers,
network traffic, page text or detected URL lists are transmitted.
**Only a direct video URL the user explicitly clicks is sent to the
RangeDesk Windows account through native messaging.** If this broad
in-page access is unacceptable, leave the video overlay feature
uninstalled and use manual context-menu capture.

**Known limitations:** no detection in DRM-protected players; no bypass of
paywalls or signed-in session protections; no HLS (.m3u8), MPEG-DASH
(.mpd), MediaSource/blob, canvas-only players or proprietary media APIs.
Cross-origin frames require Chrome permission and content-script access.
A source URL may not be downloadable outside its site if the server
requires login cookies, short-lived tokens or special headers. Some sites
may prevent showing the overlay or conceal actual video URLs. No
background page scanning, playlist parsing or conversion is implemented.
These are preview features and still require testing against real pages.

## Edge and Firefox previews (unchanged)

Chrome's guided native setup is currently Chrome-only. For Edge, load
BrowserCapture/chromium from edge://extensions, copy its ID and use the
existing BrowserCapture/setup.ps1 -EdgeExtensionId ID command. For Firefox
temporarily load BrowserCapture/firefox/manifest.json via about:debugging,
then use BrowserCapture/setup.ps1 -Firefox. Firefox public distribution
requires signing. No Windows executable can force an unreviewed extension
onto a consumer's Chrome installation.

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
