// RangeDesk automatic direct-file capture is OFF by default. Toolbar button
// toggles this browser-local preference; right-click manual capture remains.
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
  void updateBadge();
});
if (api.runtime.onStartup) api.runtime.onStartup.addListener(() => void updateBadge());

api.action.onClicked.addListener(async () => {
  const { autoCapture = false } = await api.storage.local.get("autoCapture");
  await api.storage.local.set({ autoCapture: !autoCapture });
  await updateBadge();
});

api.contextMenus.onClicked.addListener(async info => {
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
