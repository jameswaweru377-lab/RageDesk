const HOST = "com.rangedesk.capture";
chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({ id: "rangedesk-link", title: "Download link with RangeDesk", contexts: ["link"] });
  chrome.contextMenus.create({ id: "rangedesk-media", title: "Download direct media file with RangeDesk", contexts: ["video", "audio"] });
});
chrome.contextMenus.onClicked.addListener((info) => {
  const url = info.menuItemId === "rangedesk-link" ? info.linkUrl :
              info.menuItemId === "rangedesk-media" ? info.srcUrl : null;
  if (typeof url !== "string" || !/^https?:\/\//i.test(url)) return;
  chrome.runtime.sendNativeMessage(HOST, { action: "add_link", url }, (reply) => {
    const ok = !chrome.runtime.lastError && reply && reply.accepted === true;
    chrome.action.setBadgeText({ text: ok ? "OK" : "!" });
    chrome.action.setBadgeBackgroundColor({ color: ok ? "#087EDB" : "#D34A56" });
    // No URLs, session tokens or page contents are written to extension logs.
  });
});
