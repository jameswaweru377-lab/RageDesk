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

api.runtime.onMessage.addListener((message, sender, reply) => {
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
