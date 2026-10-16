// Chrome isolated-world HTML5 video overlay. No page JS injection, DRM
// detection, network interception, credentials or automatic media downloading.
(() => {
  "use strict";
  const sources = globalThis.RangeDeskVideoSources;
  if (!sources || !document.documentElement || !chrome.runtime?.id) return;

  const host = document.createElement("div");
  host.setAttribute("data-rangedesk-video-controls", "");
  host.style.cssText = "position:fixed!important;z-index:2147483646!important;pointer-events:none!important;display:none;";
  const shadow = host.attachShadow({ mode: "closed" });
  const style = document.createElement("style");
  style.textContent = [
    ":host{color-scheme:dark}",
    ".panel{font:12px/1.4 system-ui,Segoe UI,sans-serif;pointer-events:auto;min-width:142px;color:#ebf4ff;}",
    ".toggle{all:unset;cursor:pointer;box-sizing:border-box;display:block;padding:9px 12px;border-radius:9px;",
    "background:#086ccc;color:#fff;font-weight:700;box-shadow:0 3px 16px #0009;white-space:nowrap;}",
    ".toggle:focus-visible,.choice:focus-visible{outline:2px solid #9ddcff;outline-offset:2px}",
    ".menu{margin-top:5px;padding:8px;border:1px solid #31506e;background:#0a1b30;border-radius:10px;",
    "box-shadow:0 6px 24px #000a;max-width:255px;max-height:270px;overflow-y:auto;}",
    ".header{font-weight:700;font-size:12px;margin:2px 5px 7px;}",
    ".hint{color:#b3c7df;font-size:11px;margin:2px 5px 8px;}",
    ".choice{all:unset;cursor:pointer;box-sizing:border-box;display:block;padding:9px 8px;width:100%;",
    "border-radius:7px;background:#163453;margin:3px 0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}",
    ".choice:hover{background:#245682}",
    ".status{font-size:11px;color:#b3c7df;margin:7px 5px 3px;max-width:235px;overflow-wrap:break-word;}",
    "[hidden]{display:none!important}"
  ].join("");
  const panel = document.createElement("div");
  panel.className = "panel";
  const toggle = document.createElement("button");
  toggle.className = "toggle";
  toggle.type = "button";
  toggle.textContent = "↓ Download video";
  toggle.setAttribute("aria-expanded", "false");
  toggle.setAttribute("aria-label", "Show direct video downloads in RangeDesk");
  const menu = document.createElement("div");
  menu.className = "menu";
  menu.hidden = true;
  const heading = document.createElement("div");
  heading.className = "header";
  heading.textContent = "Available direct sources";
  const hint = document.createElement("div");
  hint.className = "hint";
  hint.textContent = "Only exposed formats and qualities";
  const options = document.createElement("div");
  const status = document.createElement("div");
  status.className = "status";
  status.setAttribute("role", "status");
  menu.append(heading, hint, options, status);
  panel.append(toggle, menu);
  shadow.append(style, panel);
  document.documentElement.appendChild(host);

  let active = null;
  let expanded = false;
  let pending = false;
  let queuedRefresh = false;

  function available(video) {
    return sources.list(video, document.baseURI);
  }

  function eligible(video) {
    if (!video || !video.isConnected) return false;
    const rect = video.getBoundingClientRect();
    if (rect.width < 230 || rect.height < 130 ||
        rect.bottom < 0 || rect.top > window.innerHeight ||
        rect.right < 0 || rect.left > window.innerWidth) return false;
    return available(video).length > 0;
  }

  function pickVideo() {
    if (eligible(active)) return active;
    let chosen = null;
    let area = -1;
    for (const video of document.querySelectorAll("video")) {
      if (!eligible(video)) continue;
      const rect = video.getBoundingClientRect();
      const visibleWidth = Math.max(0, Math.min(rect.right, window.innerWidth) - Math.max(rect.left, 0));
      const visibleHeight = Math.max(0, Math.min(rect.bottom, window.innerHeight) - Math.max(rect.top, 0));
      const size = visibleWidth * visibleHeight;
      if (size > area) { area = size; chosen = video; }
    }
    return chosen;
  }

  function position() {
    if (!active || !eligible(active)) {
      host.style.display = "none";
      return;
    }
    const rect = active.getBoundingClientRect();
    host.style.left = Math.max(4, Math.min(window.innerWidth - 164, rect.right - 164)) + "px";
    host.style.top = Math.max(4, Math.min(window.innerHeight - 48, rect.top + 12)) + "px";
    host.style.display = "block";
  }

  function closeMenu() {
    expanded = false;
    menu.hidden = true;
    toggle.setAttribute("aria-expanded", "false");
    status.textContent = "";
  }

  function refresh() {
    const chosen = pickVideo();
    if (chosen !== active) {
      active = chosen;
      closeMenu();
    }
    position();
    if (expanded && active && !pending) renderChoices();
  }

  function scheduleRefresh() {
    if (queuedRefresh) return;
    queuedRefresh = true;
    requestAnimationFrame(() => { queuedRefresh = false; refresh(); });
  }

  async function sendVideo(source, button) {
    if (pending || !active) return;
    const stillPresent = available(active).some(item => item.url === source.url && item.format === source.format);
    if (!stillPresent) {
      status.textContent = "This source changed. Open the menu again.";
      return;
    }
    pending = true;
    button.disabled = true;
    status.textContent = "Sending to RangeDesk…";
    try {
      const answer = await chrome.runtime.sendMessage({ action: "rangedesk_video_source", url: source.url, format: source.format });
      status.textContent = answer?.accepted
        ? "Sent to RangeDesk. Review the Add Download window."
        : "RangeDesk could not accept the link. Check its connection.";
    } catch {
      status.textContent = "RangeDesk extension connection failed.";
    } finally {
      pending = false;
      button.disabled = false;
    }
  }

  function renderChoices() {
    options.replaceChildren();
    if (!active) return;
    const found = available(active);
    found.forEach((source, i) => {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "choice";
      // Text is never constructed from the full URL (signed tokens may be present).
      button.textContent = source.label + (found.length > 1 ? " · source " + (i + 1) : "");
      button.title = "Send this direct video source to RangeDesk for review";
      button.addEventListener("click", () => { void sendVideo(source, button); });
      options.appendChild(button);
    });
  }

  toggle.addEventListener("click", () => {
    if (!active) return;
    expanded = !expanded;
    menu.hidden = !expanded;
    toggle.setAttribute("aria-expanded", String(expanded));
    if (expanded) {
      status.textContent = "";
      renderChoices();
    }
  });
  panel.addEventListener("keydown", event => {
    if (event.key === "Escape") {
      closeMenu();
      toggle.focus();
    }
  });
  document.addEventListener("pointerover", event => {
    const target = event.target;
    if (target?.tagName === "VIDEO" && target !== active && eligible(target)) {
      active = target;
      closeMenu();
      position();
    }
  }, true);
  document.addEventListener("click", event => {
    if (expanded && !event.composedPath().includes(host)) closeMenu();
  }, true);
  window.addEventListener("scroll", scheduleRefresh, true);
  window.addEventListener("resize", scheduleRefresh);
  for (const eventName of ["loadedmetadata", "loadstart", "playing", "emptied"])
    document.addEventListener(eventName, scheduleRefresh, true);

  const observer = new MutationObserver(changes => {
    // Adding/removing our own shadow UI must not cause a page-wide observer loop.
    if (changes.some(change => !host.contains(change.target))) scheduleRefresh();
  });
  observer.observe(document.documentElement, {
    subtree: true, childList: true, attributes: true,
    attributeFilter: ["src", "type"]
  });

  scheduleRefresh();
})();
