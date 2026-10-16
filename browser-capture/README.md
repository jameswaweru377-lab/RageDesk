# RangeDesk Browser Capture — opt-in preview (v0.8)

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
No browsing history, video bytes, cookies, authentication headers or
page text are transmitted. Optional v0.6 per-tab HLS request metadata
observation is described separately below.
**Only a direct video URL the user explicitly clicks is sent to the
RangeDesk Windows account through native messaging.** If this broad
in-page access is unacceptable, leave the video overlay feature
uninstalled and use manual context-menu capture.

**Known limitations:** no detection in DRM-protected players; no bypass of
paywalls or signed-in session protections; only the limited HLS MPEG-TS VOD
pipeline described above; no MPEG-DASH (.mpd), MediaSource/blob,
canvas-only players or proprietary media APIs.
Cross-origin frames require Chrome permission and content-script access.
A source URL may not be downloadable outside its site if the server
requires login cookies, short-lived tokens or special headers. Some sites
may prevent showing the overlay or conceal actual video URLs. No
unconsented background page scanning or media conversion is implemented.
These are preview features and still require testing against real pages.

## Chrome v0.8: choose qualities directly over the video player

No more copying page links or manually using Add Download → Media for
compatible videos. The yt-dlp/FFmpeg prerequisites remain exactly the same as
in v0.7; the installed tools **must still be in MediaTools** beside the
RangeDesk application.

1. Start the current RangeDesk Windows executable and verify Chrome is
   connected under Settings → Chrome browser. Reload the unpacked extension
   under chrome://extensions and confirm **version 0.8.0**.
2. On a webpage with a visible HTML5 video player, click the floating
   **↓ Download video** button. That **user click** sends the page URL to
   the native host for one non-DRM yt-dlp format analysis. The actual reported
   qualities are shown inside Chrome, along with optional direct/HLS links.
3. Check **I have permission to save this video** and click the quality you
   want. RangeDesk queues that format in the existing Windows application,
   re-analyzes the URL to validate the format choice, then downloads it to
   your configured downloads directory. If video/audio arrive as separate
   streams, yt-dlp and FFmpeg merge them where supported.
4. Watch **RangeDesk → Add Download → Media** for status and Stop. This
   route still uses yt-dlp's independent temporary files, not the native
   transfer catalog, adaptive workers or native pause/resume checkpoints.
5. If the site provides no eligible formats, shows a protected player, or
   the yt-dlp/FFmpeg tools are missing or incompatible, Chrome reports
   that analysis failed. **Open advanced Media options** is retained as a
   fallback; it should no longer be necessary for normal successful flows.

**Permissions/privacy:** the page is analyzed only after the user clicks
the floating Download video control. The media-format list contains
yt-dlp's actual reported formats; no website login cookies, browser history,
Authorization headers or decrypted DRM content are sent. Selecting a
quality requires an explicit checkbox in the video menu. The native host
verifies a supported HTTP(S) URL and only accepts known format-ID
characters. The app then re-analyzes the webpage before executing any
external download. Queued links remain only in the Windows user profile.
A large or changing site may take time to analyze and Chrome/yt-dlp may
report an error; these preview features need real-browser acceptance.

This improvement is designed for videos the user may legitimately save.
YouTube and many other sites can change extraction behavior or impose
separate content and download restrictions; **not every video is
downloadable, and DRM decryption is not provided**. Firefox and other
browser-family extensions still require additional integration work.

## Chrome v0.7 + Windows Media tab: authorized page video extraction

This adds a separate, explicitly user-initiated path for video pages that do
not expose a direct URL, such as pages using MediaSource/blob video. It
integrates two **external, optional tools**: the open-source yt-dlp extractor
and FFmpeg for audio/video merging. RangeDesk itself **does not bundle or
silently install either executable**, nor does it reverse-engineer DRM.

**One-time setup (Windows preview):**

1. Download the official Windows yt-dlp.exe release from
   https://github.com/yt-dlp/yt-dlp/releases and check its published SHA256
   against the official release checksum list. Review the tool's third-party
   executable licenses before use.
2. Download a reputable Windows FFmpeg distribution via
   https://ffmpeg.org/download.html and verify its publisher/source.
3. In RangeDesk, open **Add Download → Media → Open tool folder**. Put
   **yt-dlp.exe** and **ffmpeg.exe** directly in that MediaTools folder (not
   inside additional ZIP subdirectories).
4. In Chrome, reload the unpacked BrowserCapture/chromium extension so
   **RangeDesk Browser Capture 0.7.0** appears. Reload the video webpage.
5. On an ordinary video player, select **Download video → Analyze this webpage
   (Media tools)**. The page URL (not cookies or tokens from the player) goes
   to RangeDesk's **Add Download → Media** tab.
6. Click **Analyze available formats**. The quality dropdown shows **only
   actual formats reported by yt-dlp**, including formats with separate audio.
   Check **I have permission to save this video**, choose a format and click
   **Download selected quality**. The media tools save into the configured
   downloads folder, and FFmpeg merges separate video/audio into an MKV
   container when compatible. Use **Stop** to cancel the running process.
   The result retains the real extension returned by yt-dlp.

You can also paste an authorized page URL directly into Add Download → Media.
If a webpage requires authentication, private playback rights, paywall
access, DRM, video decryption or server-side authorization not available to
the local extractor, RangeDesk reports failure rather than extracting
browser secrets. Some YouTube videos may work using yt-dlp, but availability
depends on site permissions, current extractor versions and runtime
requirements. This is **not** universal YouTube access.

**Differences from native RangeDesk transfers:** the external media process
runs while the app is open and has its own yt-dlp partial-file support. It
does not yet appear in RangeDesk's native transfer catalog, scheduler,
adaptive connection worker cards or native pause/resume checkpoints. The
Media tab shows broad status, not guaranteed throughput/progress, and
cancelled transfers may leave resumable .part files. No credentials, cookies,
YouTube login session, unsigned scripts or remote plugin components are
automatically imported. A title/format is obtained only from a user-clicked
page URL, and the extractor must run with its local capabilities.

## Chrome v0.6: optional HLS discovery for streaming video players

On many websites the HTML5 video element uses a blob URL and does not expose
a downloadable media URL. RangeDesk can now observe **only HLS .m3u8 request
URLs** for a single tab after the user explicitly opts in:

1. Open RangeDesk and Chrome. Pin the RangeDesk extension.
2. Right-click the RangeDesk toolbar icon and choose
   **Detect HLS video on this tab (permission required)**.
3. Chrome asks for broad website request access because streaming video may
   load from a different media/CDN domain than the webpage. **Do not approve
   if you do not want that permission.** The extension does not silently
   grant itself access.
4. Start playback or reload the permitted tab, and click the floating
   **Download video** overlay where it is available.
5. If a public HLS manifest URL was observed, it appears as **Observed HLS**
   without a fabricated resolution. Click it to queue a link for explicit
   review in RangeDesk. The Windows app still rejects unsupported streams.
6. Right-click the extension again and select
   **Stop video detection on this tab** to clear it. When no tabs are
   watched, the optional broad website permission is also relinquished.

Detection is limited to the explicitly watched tab, the exact observed
website origin and the initiating frame. It lasts at most **15 minutes**
and stores at most **8 playlist URLs** in **session-only extension state**
(which disappears when the browser session ends). Playlist URLs can contain
short-lived or sensitive signed tokens; **only the URL you click** is sent
to the local RangeDesk native host. No request bodies, cookies, custom
authorization headers, full browser history or video file data are copied
into RangeDesk. Chrome still grants network-request visibility broadly
while the optional permission is active; this is why it requires consent.

This discovers HLS URLs, not hidden video files universally. It does not
decrypt DRM, extract from YouTube's protected playback endpoints, enumerate
DASH (.mpd) video/audio tracks, convert streams, or guarantee that a
detected playlist can be downloaded. Some websites use query-only or
non-.m3u8 streaming endpoints, so they remain unsupported. Cross-origin
frames whose initiator differs from the enabled tab's origin are not
currently matched. Chrome/Edge Chromium preview only; Firefox support is
separate.

## Experimental HLS MPEG-TS VOD downloads — Phase 3 foundation

The Windows engine now recognizes **direct .m3u8 URLs** and handles a limited
class of unencrypted, finished HLS MPEG-TS video-on-demand playlists. This is
not universal streaming support.

- User clicks an exposed HLS URL in the Chrome video overlay (or pastes the
  URL), reviews the normal Add Download window and starts the transfer.
- A master manifest may publish variants; the preview selects its **highest
  BANDWIDTH** variant automatically. Individual quality selection is **not
  yet implemented**. Only ordinary HLS media playlists with explicit
  .ts segment URLs and #EXT-X-ENDLIST are handled.
- Output is a valid **.ts transport stream**, not a fake MP4 or a remux.
  RangeDesk suggests .ts as the destination filename.
- Segment files are received and validated before becoming committed bytes.
  Paused/failed downloads can resume from whole-segment checkpoints when
  the exact media playlist is unchanged. A changed playlist, truncated
  segment or invalid MPEG-TS body preserves incomplete data and fails.
- No HLS decryption, AES-128, DRM, byte-range playlists, fMP4
  initialization segments, live streams, separate audio tracks, DASH,
  FFmpeg conversion, cross-site authentication, YouTube extraction or
  hidden MediaSource players. Those require separate tested features.
- Some sites present an HLS URL on a video element; many Chrome websites
  use JavaScript MediaSource playback instead, so the in-page button will
  not appear there unless optional tab-scoped HLS discovery is enabled.
There is no cookie forwarding or captured response-body inspection.

Test on a user-owned unencrypted HLS sample before wider site support.
Only download videos you have the rights and permission to save.

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
- No blob:/data: URLs, intercepted HTTPS bodies, DRM or DASH, password-
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

## Chrome v0.9: truthful media job status and YouTube runtime checks

- The in-player button still appears automatically on eligible HTML5 video players.
  Opening it performs the user-requested yt-dlp probe; selecting a reported format
  and confirming rights submits a download to RangeDesk without copying a link.
- After acceptance Chrome receives a local job ID and can ask RangeDesk for the
  latest stage: queued, validating, downloading, merging, completed or failed.
  "Queued" is not described as proof that a transfer started.
- For YouTube, RangeDesk looks for Deno 2.3+ first in `MediaTools/deno.exe`,
  then on Windows PATH. yt-dlp uses that exact runtime. Install an official
  yt-dlp.exe with bundled EJS solver scripts; no remote components are downloaded.
- Errors are classified into structured codes (missing/outdated runtime, site
  restrictions, analyzer timeouts and tool failures). No signed stream URLs or
  browser credentials are surfaced to the web page.
- Progress reports only real bytes for the current transfer; percentages appear
  only when a non-estimated stream total is available. FFmpeg merging is a
  separate stage. Completed output must exist and be nonempty.
- RangeDesk must be running to accept new selected media downloads. Status
  snapshots are kept under the local user's RangeDesk app-data directory.
  This version does not support DRM bypasses, hidden authentication/cookies,
  automatic downloading without user approval or guaranteed YouTube availability.
