const HOST = "com.rangedesk.capture";
browser.runtime.onInstalled.addListener(() => {
  browser.contextMenus.create({ id: "rangedesk-link", title: "Download link with RangeDesk", contexts: ["link"] });
  browser.contextMenus.create({ id: "rangedesk-media", title: "Download direct media file with RangeDesk", contexts: ["video", "audio"] });
});
browser.contextMenus.onClicked.addListener(async (info) => {
  const url = info.menuItemId === "rangedesk-link" ? info.linkUrl :
              info.menuItemId === "rangedesk-media" ? info.srcUrl : null;
  if (typeof url !== "string" || !/^https?:\/\//i.test(url)) return;
  try {
    const reply = await browser.runtime.sendNativeMessage(HOST, { action: "add_link", url });
    if (!reply || !reply.accepted) console.warn("RangeDesk did not accept the link.");
  } catch { console.warn("RangeDesk native host is not configured for Firefox."); }
});
