// RangeDesk automatic direct-file capture is OFF by default. Toolbar button
// toggles this browser-local preference; right-click manual capture remains.
importScripts("media-discovery.js");
const mediaDiscovery = globalThis.RangeDeskMediaDiscovery;
const MEDIA_STATE_KEY = "rangedeskVideoWatchByTab";
const HOST = "com.rangedesk.capture";
const api = typeof browser === "undefined" ? chrome : browser;
const ELIGIBLE = /\.(zip|7z|rar|tar|gz|bz2|xz|exe|msi|msix|iso|pdf|docx|xlsx|pptx|mp4|mp3|mkv|webm|png|jpg|jpeg|webp|apk)$/i;
const handoffs = new Set();

function allowedDirectUrl(value) {
  try {
    const u = new URL(value);
    return (u.protocol === "http:" || u.protocol === "https:") &&
      !u.username && !u.password && value.length <= 4096 &&
      ELIGIBLE.test(u.pathname);
  } catch { return false; }
}

async function updateBadge() {
  const { autoCapture = false } = await api.storage.local.get("autoCapture");
  await api.action.setBadgeText({ text: autoCapture ? "ON" : "" });
  await api.action.setBadgeBackgroundColor({ color: "#087EDB" });
  await api.action.setTitle({
    title: autoCapture ? "RangeDesk: auto-capture ON (click to disable)" :
      "RangeDesk: auto-capture OFF (click to enable)"
  });
}

api.runtime.onInstalled.addListener(() => {
  api.contextMenus.create({ id: "rangedesk-link", title: "Download link with RangeDesk", contexts: ["link"] });
  api.contextMenus.create({ id: "rangedesk-media", title: "Download direct media file with RangeDesk", contexts: ["video", "audio"] });
  api.contextMenus.create({ id: "rangedesk-check", title: "Check RangeDesk connection", contexts: ["action"] });
  api.contextMenus.create({ id: "rangedesk-watch-hls", title: "Detect HLS video on this tab (permission required)", contexts: ["action"] });
  api.contextMenus.create({ id: "rangedesk-stop-hls", title: "Stop video detection on this tab", contexts: ["action"] });
  void updateBadge();
});
if (api.runtime.onStartup) api.runtime.onStartup.addListener(() => void updateBadge());

api.action.onClicked.addListener(async () => {
  const { autoCapture = false } = await api.storage.local.get("autoCapture");
  await api.storage.local.set({ autoCapture: !autoCapture });
  await updateBadge();
});

api.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId === "rangedesk-watch-hls" ||
      info.menuItemId === "rangedesk-stop-hls") {
    await changeMediaWatch(info.menuItemId === "rangedesk-watch-hls", tab);
    return;
  }
  if (info.menuItemId === "rangedesk-check") {
    // A real Chrome -> native-host protocol check, not a registry guess.
    // Does not request browser history, cookies or download URLs.
    let available = false;
    try {
      const reply = await api.runtime.sendNativeMessage(HOST, { action: "ping" });
      available = reply && reply.accepted === true;
    } catch { /* Missing host, disabled extension or invalid registration. */ }
    await api.action.setBadgeText({ text: available ? "OK" : "!" });
    await api.action.setBadgeBackgroundColor({ color: available ? "#087EDB" : "#D34A56" });
    await api.action.setTitle({ title: available
      ? "RangeDesk native host connected — click to toggle automatic capture"
      : "RangeDesk is not connected. Use Settings > Chrome browser in RangeDesk." });
    return;
  }
  const url = info.menuItemId === "rangedesk-link" ? info.linkUrl :
              info.menuItemId === "rangedesk-media" ? info.srcUrl : null;
  if (typeof url !== "string" || !/^https?:\/\//i.test(url)) return;
  try {
    await api.runtime.sendNativeMessage(HOST, { action: "add_link", url });
  } catch {
    // Never log signed URLs or browser credentials.
    console.warn("RangeDesk manual link capture is unavailable.");
  }
});

// Only isolated Chrome content scripts running on an HTTP(S) webpage may
// request this user-clicked direct-video link handoff. No page JS can call
// native messaging directly; no cookies, headers or page content are sent.
const DIRECT_VIDEO = /\.(mp4|m4v|webm|ogv|ogg|mov)$/i;
const STREAMING_MANIFEST = /\.(mpd|m3u|ism)$/i;
const VIDEO_FORMATS = new Set(["MP4", "M4V", "WebM", "Ogg video", "MOV"]);
const HLS_SUFFIX = /\.m3u8$/i;

function allowedVideoHandoff(message, sender) {
  if (!message || message.action !== "rangedesk_video_source" ||
      sender?.id !== api.runtime.id || !Number.isInteger(sender?.tab?.id) ||
      typeof sender?.url !== "string" || !/^https?:\/\//i.test(sender.url) ||
      typeof message.url !== "string" || message.url.length > 4096) return false;
  try {
    const uri = new URL(message.url);
    const path = uri.pathname.toLowerCase();
    if (!["http:", "https:"].includes(uri.protocol) || uri.username || uri.password ||
        STREAMING_MANIFEST.test(path)) return false;
    // Accept recognized direct video paths, or extensionless endpoints that
    // the video element explicitly declared as a known video MIME type.
    const suffix = /\.[a-z0-9]+$/i.exec(path)?.[0] || "";
    return (HLS_SUFFIX.test(path) && message.format === "HLS") ||
      DIRECT_VIDEO.test(path) ||
      (!suffix && VIDEO_FORMATS.has(message.format));
  } catch { return false; }
}

// Chrome's frame URL can lag document.URL following YouTube History API
// navigation. Authenticate the content-script sender before accepting a current
// top-level tab URL or the tightly restricted YouTube SPA fallback. Never accept
// a different origin, embedded credentials or a URL supplied by page JavaScript.
function safePageUrl(raw) {
  if (typeof raw !== "string" || raw.length > 4096) return null;
  try {
    const result = new URL(raw);
    if (!["http:", "https:"].includes(result.protocol) ||
        result.username || result.password) return null;
    result.hash = "";
    return result;
  } catch { return null; }
}

function isYouTubeVideoPage(url) {
  if (url.protocol !== "https:" ||
      !["youtube.com", "www.youtube.com", "m.youtube.com",
        "www.youtube-nocookie.com", "youtube-nocookie.com"].includes(url.hostname))
    return false;
  if (url.pathname === "/watch") return url.searchParams.has("v") &&
    /^[A-Za-z0-9_-]{11}$/.test(url.searchParams.get("v"));
  return /^\/(?:shorts|live|embed)\/[A-Za-z0-9_-]{11}\/?$/.test(url.pathname);
}

function allowedPageAction(message, sender) {
  if (!message || sender?.id !== api.runtime.id ||
      !Number.isInteger(sender?.tab?.id)) return false;
  const incoming = safePageUrl(message.url);
  const frame = safePageUrl(sender.url);
  if (!incoming || !frame || incoming.origin !== frame.origin) return false;
  // sender.origin is browser-provided and must agree when available.
  if (typeof sender.origin === "string" &&
      sender.origin !== incoming.origin) return false;
  if (incoming.href === frame.href) return true;

  // Frame-specific identity remains mandatory for embedded third-party players.
  // The top-level tab URL, when present, is browser supplied and can be newer
  // than sender.url when a site uses history.pushState/replaceState.
  if (sender.frameId === 0) {
    const currentTab = safePageUrl(sender.tab.url);
    if (currentTab && incoming.href === currentTab.href) return true;
    if (currentTab && currentTab.href !== incoming.href) return false;
  }

  // Restricted fallback when Chrome lacks a fresh tab URL or an iframe has
  // changed its YouTube video via same-origin SPA navigation. This needs the
  // browser-provided origin, a real known video route and a valid frame ID.
  // It never authorizes arbitrary same-origin paths or cross-site requests.
  return Number.isInteger(sender.frameId) &&
    sender.origin === incoming.origin &&
    isYouTubeVideoPage(incoming) &&
    isYouTubeVideoPage(frame);
}

api.runtime.onMessage.addListener((message, sender, reply) => {
  if (message?.action === "rangedesk_media_probe" ||
      message?.action === "rangedesk_media_download") {
    if (!allowedPageAction(message, sender)) {
      reply({ accepted: false, errorCode: "PAGE_IDENTITY_MISMATCH",
        message: "The video page changed or its frame could not be verified. Refresh this video and retry." });
      return false;
    }
    if (message.action === "rangedesk_media_download") {
      if (message.fileStem !== undefined &&
          (typeof message.fileStem !== "string" ||
           message.fileStem.length < 1 || message.fileStem.length > 80 ||
           !message.fileStem.trim() || message.fileStem !== message.fileStem.trim() ||
           !/^[\p{L}\p{N} _-]+$/u.test(message.fileStem))) {
        reply({ accepted: false, errorCode: "INVALID_FILENAME",
          message: "File name must use up to 80 letters, numbers, spaces, _ or -. No leading or trailing spaces." });
        return false;
      }
      if (message.rightsConfirmed !== true ||
          typeof message.formatId !== "string" ||
          !/^[A-Za-z0-9_.-]{1,64}$/.test(message.formatId) ||
          !["default", "videos"].includes(message.saveLocation ?? "default") ||
          (message.queueOnly !== undefined && typeof message.queueOnly !== "boolean")) {
        reply({ accepted: false, errorCode: "INVALID_SELECTION",
          message: "Select a valid quality and confirm permission." });
        return false;
      }
      void api.runtime.sendNativeMessage(HOST, {
        action: "media_download", url: message.url,
        formatId: message.formatId, rightsConfirmed: true,
        queueOnly: message.queueOnly === true,
        fileStem: message.fileStem ?? null,
        saveLocation: message.saveLocation ?? "default"
      }).then(result => reply({
        accepted: result?.accepted === true,
        jobId: typeof result?.jobId === "string" && /^[a-f0-9]{32}$/i.test(result.jobId)
          ? result.jobId : null,
        state: result?.accepted ? "queued" : "failed",
        errorCode: result?.errorCode || null,
        message: typeof result?.message === "string" && result.message.length <= 240
          ? result.message : "RangeDesk could not queue this download."
      })).catch(() => reply({ accepted: false, errorCode: "NATIVE_HOST_UNAVAILABLE",
        message: "Cannot reach RangeDesk. Open the app and check its Chrome connection." }));
      return true;
    }
    void api.runtime.sendNativeMessage(HOST, {
      action: "media_probe", url: message.url
    }).then(result => {
      if (result?.accepted !== true || !Array.isArray(result.formats)) {
        reply({ accepted: false, formats: [],
          errorCode: typeof result?.errorCode === "string" ? result.errorCode : "PROBE_FAILED",
          message: typeof result?.message === "string" && result.message.length <= 240
            ? result.message : "RangeDesk could not analyze this video." });
        return;
      }
      const formats = result.formats.filter(f =>
        typeof f?.id === "string" && /^[A-Za-z0-9_.-]{1,64}$/.test(f.id) &&
        typeof f.label === "string" && f.label.length <= 160
      ).slice(0, 60).map(f => ({
        id: f.id, label: f.label,
        height: Number.isInteger(f.height) ? f.height : 0,
        needsAudioMerge: f.needsAudioMerge === true
      }));
      reply({ accepted: formats.length > 0, formats,
        message: formats.length ? "" : "No usable formats found for this page." });
    }).catch(() => reply({ accepted: false, formats: [],
      errorCode: "NATIVE_HOST_UNAVAILABLE",
      message: "Cannot reach RangeDesk's native media analyzer." }));
    return true;
  }
  if (message?.action === "rangedesk_media_status") {
    if (!allowedPageAction(message, sender) ||
        typeof message.jobId !== "string" || !/^[a-f0-9]{32}$/i.test(message.jobId)) {
      reply({ accepted: false, errorCode: "INVALID_STATUS_REQUEST" });
      return false;
    }
    void api.runtime.sendNativeMessage(HOST, {
      action: "media_status", jobId: message.jobId
    }).then(result => reply({
      accepted: result?.accepted === true,
      jobId: result?.jobId === message.jobId ? result.jobId : null,
      state: typeof result?.state === "string" ? result.state : null,
      message: typeof result?.message === "string" && result.message.length <= 240
        ? result.message : "Checking download status…",
      downloadedBytes: Number.isSafeInteger(result?.downloadedBytes) && result.downloadedBytes >= 0
        ? result.downloadedBytes : null,
      totalBytes: Number.isSafeInteger(result?.totalBytes) && result.totalBytes > 0
        ? result.totalBytes : null,
      estimated: result?.estimated === true
    })).catch(() => reply({ accepted: false, errorCode: "NATIVE_HOST_UNAVAILABLE",
      message: "RangeDesk connection interrupted. The download may still be running." }));
    return true;
  }
  if (message?.action === "rangedesk_media_page") {
    // Use the same verified page identity as quality probes, including safe
    // YouTube navigation. Never let the page request arbitrary destinations.
    const requested = message.url;
    if (!allowedPageAction(message, sender)) {
      reply({ accepted: false, message: "Video page could not be verified." });
      return false;
    }
    void api.runtime.sendNativeMessage(HOST, {
      action: "add_link", url: requested
    }).then(result => reply({ accepted: result?.accepted === true }))
      .catch(() => reply({ accepted: false }));
    return true;
  }
  if (message?.action === "rangedesk_media_list") {
    if (sender?.id !== api.runtime.id || !Number.isInteger(sender.tab?.id) ||
        !Number.isInteger(sender.frameId)) {
      reply({ enabled: false, sources: [] });
      return false;
    }
    void watchedTabs().then(state => {
      reply(mediaDiscovery.listing(state[sender.tab.id], sender.url,
        sender.frameId, Date.now()));
    }).catch(() => reply({ enabled: false, sources: [] }));
    return true;
  }
  if (!message || message.action !== "rangedesk_video_source") return false;
  if (!allowedVideoHandoff(message, sender)) {
    reply({ accepted: false });
    return false;
  }
  void api.runtime.sendNativeMessage(HOST, {
    action: "add_link", url: message.url
  }).then(result => {
    reply({ accepted: result?.accepted === true });
  }).catch(() => {
    reply({ accepted: false });
  });
  // Keep the extension message channel open for the native host reply.
  return true;
});


// Chrome grants broad request-URL metadata only after the user explicitly
// enables detection using the extension's context menu and accepts Chrome's
// permission prompt. Only the chosen tab is examined; no response bodies,
// headers, browsing history, cookies or unrelated-tab URLs are saved.
async function watchedTabs() {
  if (!api.storage?.session) return {};
  const data = await api.storage.session.get(MEDIA_STATE_KEY);
  const value = data?.[MEDIA_STATE_KEY];
  return value && typeof value === "object" ? value : {};
}

async function updateWatchedTabs(mutate) {
  if (!api.storage?.session) return;
  const tabs = await watchedTabs();
  await mutate(tabs);
  await api.storage.session.set({ [MEDIA_STATE_KEY]: tabs });
}

async function activeMediaTab(tab) {
  if (Number.isInteger(tab?.id) && typeof tab.url === "string") return tab;
  const open = await api.tabs.query({ active: true, currentWindow: true });
  return open.find(item => Number.isInteger(item.id)) || null;
}

async function changeMediaWatch(enable, clickedTab) {
  if (!api.tabs || !api.permissions || !api.storage?.session) return;
  // The permission call must follow a direct browser user gesture. Chrome
  // presents a prompt; rejection leaves existing download behavior unchanged.
  if (enable) {
    const granted = await api.permissions.request({ origins: ["<all_urls>"] })
      .catch(() => false);
    if (!granted) return;
  }
  const tab = await activeMediaTab(clickedTab);
  if (!tab) return;
  const candidate = mediaDiscovery.beginWatch(tab.url, Date.now());
  await updateWatchedTabs(async state => {
    if (enable && candidate) state[tab.id] = candidate;
    else delete state[tab.id];
  });
  // When no tabs are being watched, release the broad optional Chrome
  // permission instead of retaining background network visibility.
  if (!enable) await revokePermissionWhenIdle();
  try {
    await api.tabs.sendMessage(tab.id, { action: "rangedesk_media_updated" });
  } catch { /* Content script may be absent on restricted pages. */ }
}

async function rememberObservedHls(details) {
  if (!api.storage?.session || !Number.isInteger(details?.tabId)) return;
  const state = await watchedTabs();
  const watched = state[details.tabId];
  const next = mediaDiscovery.observe(details, watched, Date.now());
  if (!next) return;
  await updateWatchedTabs(async current => {
    // Do not resurrect expired/stopped watches after an async event.
    const again = mediaDiscovery.observe(details, current[details.tabId], Date.now());
    if (again) current[details.tabId] = again;
  });
  try {
    await api.tabs.sendMessage(details.tabId, { action: "rangedesk_media_updated" },
      { frameId: details.frameId });
  } catch { /* A background tab/frame may have navigated away. */ }
}

async function revokePermissionWhenIdle() {
  const state = await watchedTabs();
  if (!Object.values(state).some(item => item && item.expiresAt > Date.now())) {
    try { await api.permissions.remove({ origins: ["<all_urls>"] }); }
    catch { /* Chrome's site controls remain available to the user. */ }
  }
}

async function dropWatch(tabId) {
  await updateWatchedTabs(async state => { delete state[tabId]; });
  await revokePermissionWhenIdle();
}

if (api.webRequest?.onCompleted) {
  api.webRequest.onCompleted.addListener(details => {
    // Filtering before accessing any browser storage avoids collecting
    // unrelated network addresses from tabs not under observation.
    if (mediaDiscovery.eligibleHls(details?.url))
      void rememberObservedHls(details).catch(() => {});
  }, { urls: ["http://*/*", "https://*/*"] });
}
if (api.tabs?.onRemoved)
  api.tabs.onRemoved.addListener(id => { void dropWatch(id).catch(() => {}); });
if (api.tabs?.onUpdated)
  api.tabs.onUpdated.addListener((id, changes) => {
    if (typeof changes?.url !== "string") return;
    void updateWatchedTabs(async state => {
      const watched = state[id];
      if (!watched) return;
      const origin = mediaDiscovery.originOf(changes.url);
      if (origin !== watched.origin) delete state[id];
      else watched.sources = []; // Navigation invalidates old sources.
    }).then(revokePermissionWhenIdle).catch(() => {});
  });

api.downloads.onCreated.addListener(item => { void capture(item); });

async function capture(item) {
  if (!item || !Number.isInteger(item.id) || handoffs.has(item.id) ||
      (item.state && item.state !== "in_progress") ||
      (item.byExtensionId && item.byExtensionId === api.runtime.id)) return;
  const url = item.finalUrl || item.url;
  if (typeof url !== "string" || !allowedDirectUrl(url)) return;
  const { autoCapture = false } = await api.storage.local.get("autoCapture");
  if (!autoCapture || handoffs.has(item.id)) return;
  handoffs.add(item.id);
  let paused = false;
  let canceled = false;
  try {
    // Browsers retain ownership until the GUI has persisted AND started the
    // matching transfer. Queueing a URL in the native host is not an ACK.
    await api.downloads.pause(item.id);
    paused = true;
    const answer = await api.runtime.sendNativeMessage(HOST, {
      action: "capture_download", url
    });
    if (answer && answer.accepted === true) {
      await api.downloads.cancel(item.id);
      canceled = true;
    }
  } catch {
    // Native host absent, RangeDesk closed, deadline expired or cancel failed:
    // the browser must retain the original download.
  } finally {
    if (paused && !canceled) {
      try { await api.downloads.resume(item.id); } catch { /* already finished/canceled */ }
    }
    handoffs.delete(item.id);
  }
}
