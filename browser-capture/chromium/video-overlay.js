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
    ".choice.selected{background:#245b89;outline:1px solid #8bd1ff;}",
    ".confirm-mask{position:fixed;inset:0;display:flex;align-items:center;justify-content:center;",
    "padding:18px;background:#000a;pointer-events:auto;z-index:2147483647;box-sizing:border-box;}",
    ".confirm{width:min(560px,calc(100vw - 32px));max-height:85vh;overflow:auto;box-sizing:border-box;",
    "background:linear-gradient(140deg,#0e2439,#091725);border:1px solid #2e5174;",
    "box-shadow:0 16px 65px #000c;border-radius:15px;padding:22px;color:#eaf4ff;",
    "font:14px/1.5 system-ui,Segoe UI,sans-serif;}",
    ".confirm-head{display:flex;justify-content:space-between;align-items:center;gap:14px;margin-bottom:16px;}",
    ".confirm-brand{font-size:21px;font-weight:750;}",
    ".confirm-body{display:grid;grid-template-columns:112px minmax(0,1fr);gap:14px;}",
    ".confirm-thumb{height:80px;border-radius:9px;background:#163755;object-fit:cover;width:112px;}",
    ".confirm-thumb[hidden]+.confirm-detail{grid-column:1/-1;}",
    ".confirm-detail{min-width:0;}",
    ".confirm-title{font-size:16px;font-weight:700;overflow-wrap:anywhere;}",
    ".confirm-sub{color:#adbfce;font-size:12px;margin:2px 0 12px;}",
    ".confirm-form{display:grid;grid-template-columns:84px minmax(0,1fr);gap:9px;align-items:center;}",
    ".confirm-label{font-size:12px;color:#d0e1f1;}",
    ".confirm-field{box-sizing:border-box;width:100%;min-width:0;min-height:37px;background:#12283b;",
    "border:1px solid #48647d;border-radius:6px;color:#f1f7fc;padding:7px 9px;font:13px system-ui;}",
    ".confirm-actions{display:flex;align-items:center;justify-content:flex-end;flex-wrap:wrap;",
    "gap:9px;margin-top:19px;}",
    ".confirm-btn{cursor:pointer;min-height:39px;border:1px solid #58738a;border-radius:7px;",
    "padding:9px 14px;background:#142b3c;color:#fff;font-weight:600;}",
    ".confirm-btn.primary{background:#087bed;border-color:#1595ff;}",
    ".confirm-btn:disabled{opacity:.55;cursor:not-allowed;}",
    ".confirm-btn:focus-visible,.confirm-field:focus-visible{outline:2px solid #8ed5ff;outline-offset:2px;}",
    ".confirm-note{font-size:12px;color:#b6c9db;margin-top:10px;overflow-wrap:anywhere;}",
    ".confirm-rights{display:flex;align-items:center;gap:8px;margin-top:12px;font-size:12px;}",
    ".job-pill{position:absolute;right:0;top:43px;box-sizing:border-box;width:min(325px,calc(100vw - 18px));",
    "pointer-events:auto;color:#eaf4ff;background:#0d2236;border:1px solid #396585;",
    "border-radius:11px;box-shadow:0 8px 26px #000b;padding:9px;}",
    ".job-pill-row{display:flex;align-items:center;gap:7px;}",
    ".job-pill-toggle{all:unset;cursor:pointer;flex:1;min-width:0;padding:5px;",
    "color:#eaf4ff;font:600 12px/1.35 system-ui,Segoe UI,sans-serif;}",
    ".job-pill-close{all:unset;cursor:pointer;padding:3px 8px;color:#c8d9e8;font-size:20px;}",
    ".job-pill-toggle:focus-visible,.job-pill-close:focus-visible{outline:2px solid #9ddcff;outline-offset:2px;}",
    ".job-pill-detail{padding:4px 6px 3px;color:#c0d6e7;font:12px/1.4 system-ui,Segoe UI,sans-serif;overflow-wrap:anywhere;}",
    "@media(max-width:560px){.confirm{padding:14px}.confirm-body{grid-template-columns:1fr;}",
    ".confirm-thumb{display:none}.confirm-form{grid-template-columns:1fr;gap:5px}}",
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
  toggle.setAttribute("aria-label", "Show video qualities and available downloads");
  const menu = document.createElement("div");
  menu.className = "menu";
  menu.hidden = true;
  const heading = document.createElement("div");
  heading.className = "header";
  heading.textContent = "Download video";
  const hint = document.createElement("div");
  hint.className = "hint";
  hint.textContent = "Real video qualities and available direct streams";
  const options = document.createElement("div");
  const status = document.createElement("div");
  status.className = "status";
  status.setAttribute("role", "status");
  menu.append(heading, hint, options, status);
  panel.append(toggle, menu);
  const confirmMask = document.createElement("div");
  confirmMask.className = "confirm-mask";
  confirmMask.hidden = true;
  const confirmPanel = document.createElement("div");
  confirmPanel.className = "confirm";
  confirmPanel.setAttribute("role", "dialog");
  confirmPanel.setAttribute("aria-modal", "true");
  confirmPanel.setAttribute("aria-label", "RangeDesk video download");
  const confirmHead = document.createElement("div");
  confirmHead.className = "confirm-head";
  const brand = document.createElement("div");
  brand.className = "confirm-brand";
  brand.textContent = "↓  RangeDesk · Download video";
  const confirmX = document.createElement("button");
  confirmX.className = "confirm-btn";
  confirmX.textContent = "×";
  confirmX.setAttribute("aria-label", "Close download confirmation");
  confirmHead.append(brand, confirmX);
  const confirmBody = document.createElement("div");
  confirmBody.className = "confirm-body";
  const thumb = document.createElement("img");
  thumb.className = "confirm-thumb";
  thumb.alt = "";
  const detail = document.createElement("div");
  detail.className = "confirm-detail";
  const confirmTitle = document.createElement("div");
  confirmTitle.className = "confirm-title";
  const confirmSource = document.createElement("div");
  confirmSource.className = "confirm-sub";
  const confirmForm = document.createElement("div");
  confirmForm.className = "confirm-form";
  const field = (name, control) => {
    const caption = document.createElement("label");
    caption.className = "confirm-label";
    caption.textContent = name;
    confirmForm.append(caption, control);
  };
  const qualityField = document.createElement("select");
  qualityField.className = "confirm-field";
  field("Quality", qualityField);
  const folderField = document.createElement("select");
  folderField.className = "confirm-field";
  for (const [value, label] of [["default", "RangeDesk Downloads (configured folder)"],
      ["videos", "RangeDesk Downloads / Videos"]]) {
    const option = document.createElement("option");
    option.value = value; option.textContent = label; folderField.append(option);
  }
  field("Save to", folderField);
  const nameField = document.createElement("input");
  nameField.className = "confirm-field";
  nameField.type = "text"; nameField.maxLength = 80;
  nameField.setAttribute("aria-label", "Download file name without extension");
  field("File name", nameField);
  const nameInfo = document.createElement("div");
  nameInfo.className = "confirm-note";
  nameInfo.textContent = "RangeDesk determines the final extension after download and merging. Choose a save folder in RangeDesk settings.";
  const rememberLabel = document.createElement("label");
  rememberLabel.className = "confirm-rights";
  const rememberFolder = document.createElement("input");
  rememberFolder.type = "checkbox";
  const rememberText = document.createElement("span");
  rememberText.textContent = "Remember this folder choice for this site";
  rememberLabel.append(rememberFolder, rememberText);
  const permissionLabel = document.createElement("label");
  permissionLabel.className = "confirm-rights";
  const confirmPermission = document.createElement("input");
  confirmPermission.type = "checkbox";
  const permissionText = document.createElement("span");
  permissionText.textContent = "I have permission to save this video";
  permissionLabel.append(confirmPermission, permissionText);
  detail.append(confirmTitle, confirmSource, confirmForm, nameInfo, rememberLabel, permissionLabel);
  confirmBody.append(thumb, detail);
  const confirmInfo = document.createElement("div");
  confirmInfo.className = "confirm-note";
  confirmInfo.setAttribute("role", "status");
  const actions = document.createElement("div");
  actions.className = "confirm-actions";
  const laterButton = document.createElement("button");
  laterButton.className = "confirm-btn";
  laterButton.textContent = "Download Later";
  const startButton = document.createElement("button");
  startButton.className = "confirm-btn primary";
  startButton.textContent = "↓ Start Download";
  const cancelButton = document.createElement("button");
  cancelButton.className = "confirm-btn";
  cancelButton.textContent = "Cancel";
  actions.append(laterButton, startButton, cancelButton);
  confirmPanel.append(confirmHead, confirmBody, confirmInfo, actions);
  confirmMask.appendChild(confirmPanel);
  const jobPill = document.createElement("div");
  jobPill.className = "job-pill";
  jobPill.hidden = true;
  const jobPillRow = document.createElement("div");
  jobPillRow.className = "job-pill-row";
  const jobPillToggle = document.createElement("button");
  jobPillToggle.type = "button";
  jobPillToggle.className = "job-pill-toggle";
  jobPillToggle.textContent = "↓ RangeDesk · Queued";
  jobPillToggle.setAttribute("aria-expanded", "false");
  jobPillToggle.setAttribute("aria-label", "Expand RageDesk download progress");
  const jobPillClose = document.createElement("button");
  jobPillClose.type = "button";
  jobPillClose.className = "job-pill-close";
  jobPillClose.textContent = "×";
  jobPillClose.setAttribute("aria-label", "Hide progress panel, keep download running");
  jobPillRow.append(jobPillToggle, jobPillClose);
  const jobPillDetail = document.createElement("div");
  jobPillDetail.className = "job-pill-detail";
  jobPillDetail.setAttribute("role", "status");
  jobPillDetail.hidden = true;
  jobPill.append(jobPillRow, jobPillDetail);
  shadow.append(style, panel, confirmMask, jobPill);
  document.documentElement.appendChild(host);

  let active = null;
  let expanded = false;
  let pending = false;
  let queuedRefresh = false;
  let observed = [];
  let detecting = false;
  let mediaFormats = [];
  let mediaAnalyzedUrl = "";
  let mediaLoading = false;
  let mediaRequestSequence = 0;
  let mediaError = "";
  let selectedQualityId = "";
  let qualityButtons = [];
  let rightsConfirmed = false;
  let confirmPageUrl = "";
  let confirmPending = false;
  let confirmJobSubmitted = false;
  let confirmReturnFocus = null;
  let mediaJobId = null;
  let mediaJobUrl = "";
  let mediaJobTimer = null;
  let mediaJobDone = false;

  jobPillToggle.addEventListener("click", () => {
    jobPillDetail.hidden = !jobPillDetail.hidden;
    jobPillToggle.setAttribute("aria-expanded", String(!jobPillDetail.hidden));
  });
  jobPillClose.addEventListener("click", () => {
    jobPill.hidden = true; // This only hides the browser panel, not the RangeDesk job.
    if (!expanded && mediaJobTimer !== null) {
      clearTimeout(mediaJobTimer);
      mediaJobTimer = null;
    }
  });
  function showMinimizedJob(queueOnly) {
    confirmMask.hidden = true;
    closeMenu();
    jobPillToggle.textContent = queueOnly
      ? "↓ RangeDesk · Saved for later" : "↓ RangeDesk · Download queued";
    jobPillDetail.textContent = queueOnly
      ? "Saved in RangeDesk Downloads. Use Resume there to start."
      : "Accepted by RangeDesk. Checking the download status…";
    jobPillDetail.hidden = true;
    jobPillToggle.setAttribute("aria-expanded", "false");
    jobPill.hidden = false;
    jobPillToggle.focus();
  }
  function updateJobPill(reply) {
    const phase = {
      queued: "Queued", validating: "Validating", starting: "Starting",
      downloading: "Downloading", merging: "Merging", finalizing: "Finishing",
      completed: "Completed", failed: "Failed", cancelled: "Cancelled"
    };
    jobPillToggle.textContent = "↓ RangeDesk · " +
      (phase[reply?.state] || "Checking");
    jobPillDetail.textContent = jobStatusText(reply);
  }

  function jobStatusText(reply) {
    const stage = String(reply?.state || "");
    const safe = typeof reply?.message === "string" ? reply.message.slice(0, 200) : "Checking media status…";
    const downloaded = reply?.downloadedBytes;
    const total = reply?.totalBytes;
    if (stage !== "downloading" || !Number.isSafeInteger(downloaded) || downloaded < 0)
      return safe;
    const bytes = n => (n / 1048576).toFixed(1) + " MB";
    if (Number.isSafeInteger(total) && total > 0)
      return safe + " " + bytes(downloaded) + " / " + (reply.estimated ? "~" : "") + bytes(total) +
        (!reply.estimated ? " (" + Math.min(100, Math.floor(downloaded * 100 / total)) + "% of current stream)" : "");
    return safe + " " + bytes(downloaded) + " received.";
  }

  function pollJob() {
    if (mediaJobTimer !== null) clearTimeout(mediaJobTimer);
    mediaJobTimer = null;
    if ((!expanded && confirmMask.hidden && jobPill.hidden) || mediaJobDone || !mediaJobId) return;
    if (mediaJobUrl !== currentPageUrl()) {
      mediaJobId = null; // The job still runs in RangeDesk; never associate it with another page.
      jobPill.hidden = true;
      return;
    }
    mediaJobTimer = setTimeout(async () => {
      try {
        const result = await chrome.runtime.sendMessage({
          action: "rangedesk_media_status", url: mediaJobUrl, jobId: mediaJobId
        });
        if ((!expanded && confirmMask.hidden && jobPill.hidden) || mediaJobUrl !== currentPageUrl()) return;
        if (result?.accepted && result?.jobId === mediaJobId) {
          status.textContent = jobStatusText(result);
          if (!confirmMask.hidden) confirmInfo.textContent = jobStatusText(result);
          if (!jobPill.hidden) updateJobPill(result);
          mediaJobDone = ["completed", "failed", "cancelled"].includes(result.state);
        } else {
          const problem = typeof result?.message === "string"
            ? result.message : "Checking RangeDesk connection; the download may still be running.";
          status.textContent = problem;
          if (!jobPill.hidden) jobPillDetail.textContent = problem;
        }
      } catch {
        const message = "RangeDesk connection interrupted. Check the Downloads page.";
        if (expanded) status.textContent = message;
        if (!jobPill.hidden) jobPillDetail.textContent = message;
      }
      if (!mediaJobDone) pollJob();
    }, 1500);
  }

  function currentPageUrl() {
    return typeof document.URL === "string" ? document.URL : document.baseURI;
  }

  async function analyzePageQualities() {
    const url = currentPageUrl();
    if (mediaLoading && url === mediaAnalyzedUrl) return;
    if (url === mediaAnalyzedUrl && mediaFormats.length > 0) return;
    const requestId = ++mediaRequestSequence;
    mediaAnalyzedUrl = url;
    mediaFormats = [];
    selectedQualityId = "";
    mediaError = "";
    rightsConfirmed = false;
    if (!confirmMask.hidden && confirmPageUrl !== url) closeConfirmation();
    mediaLoading = true;
    status.textContent = "Finding available video qualities…";
    if (expanded) renderChoices();
    try {
      const result = await chrome.runtime.sendMessage({
        action: "rangedesk_media_probe", url
      });
      // A YouTube SPA navigation can finish while an older probe is in flight.
      if (requestId !== mediaRequestSequence || currentPageUrl() !== url) return;
      if (!result?.accepted || !Array.isArray(result.formats)) {
        const safeCode = typeof result?.errorCode === "string" &&
          /^[A-Z][A-Z0-9_]{2,48}$/.test(result.errorCode) ? result.errorCode : "";
        const detail = typeof result?.message === "string" && result.message.length <= 240
          ? result.message : "Could not analyze this page. Check RangeDesk Media Tools.";
        mediaError = safeCode ? detail + " · " + safeCode : detail;
        return;
      }
      mediaFormats = result.formats.filter(f =>
        typeof f?.id === "string" && /^[A-Za-z0-9_.-]{1,64}$/.test(f.id) &&
        typeof f.label === "string").slice(0, 60);
      if (mediaFormats.length === 0)
        mediaError = "No downloadable quality was reported for this video.";
    } catch {
      if (requestId === mediaRequestSequence && currentPageUrl() === url)
        mediaError = "RangeDesk media analyzer is unavailable.";
    } finally {
      if (requestId === mediaRequestSequence) {
        mediaLoading = false;
        if (status.textContent === "Finding available video qualities…")
          status.textContent = "";
        if (expanded && currentPageUrl() === url) renderChoices();
      }
    }
  }

  function sanitizeFileStem(title) {
    const clean = String(title || "Video").replace(/ - YouTube$/i, "")
      .replace(/[^\p{L}\p{N} _-]/gu, " ").replace(/\s+/g, " ").trim().slice(0, 80);
    return clean || "Video";
  }

  function closeConfirmation() {
    if (confirmPending) return;
    confirmMask.hidden = true;
    if (confirmReturnFocus?.isConnected) confirmReturnFocus.focus();
  }
  function setConfirmationBusy(busy) {
    confirmPending = busy;
    startButton.disabled = busy || confirmJobSubmitted;
    laterButton.disabled = busy || confirmJobSubmitted;
    nameField.disabled = busy || confirmJobSubmitted;
    folderField.disabled = busy || confirmJobSubmitted;
    qualityField.disabled = busy || confirmJobSubmitted;
  }

  async function openConfirmation(format, button) {
    if (mediaLoading || pending) return;
    if (mediaAnalyzedUrl !== currentPageUrl() ||
        !mediaFormats.some(item => item.id === format.id)) {
      status.textContent = "Video changed. Reopen Download video to refresh the qualities.";
      return;
    }
    confirmReturnFocus = button;
    confirmPageUrl = currentPageUrl();
    confirmJobSubmitted = false;
    confirmInfo.textContent = "Choose Start Download or Download Later. No transfer has begun.";
    setConfirmationBusy(false);
    confirmTitle.textContent = typeof document.title === "string"
      ? document.title.slice(0, 160) : "Video";
    confirmSource.textContent = new URL(confirmPageUrl).hostname + " · Selected video format";
    const poster = typeof active?.poster === "string" ? active.poster : "";
    if (/^https?:\/\//i.test(poster)) { thumb.src = poster; thumb.hidden = false; }
    else { thumb.hidden = true; }
    qualityField.replaceChildren();
    for (const candidate of mediaFormats) {
      const choice = document.createElement("option");
      choice.value = candidate.id; choice.textContent = candidate.label;
      qualityField.appendChild(choice);
    }
    qualityField.value = format.id;
    nameField.value = sanitizeFileStem(confirmTitle.textContent);
    confirmPermission.checked = false;
    let hostName = "";
    try { hostName = new URL(confirmPageUrl).hostname; } catch {}
    const key = "rangedesk_media_folder:" + hostName;
    folderField.value = "default";
    rememberFolder.checked = false;
    try {
      const saved = await chrome.storage?.local?.get(key);
      if (confirmPageUrl !== currentPageUrl()) return;
      const choice = saved?.[key];
      if (choice === "default" || choice === "videos") {
        folderField.value = choice; rememberFolder.checked = true;
      }
    } catch {}
    if (confirmPageUrl !== currentPageUrl()) return;
    confirmMask.hidden = false;
    startButton.focus();
  }

  async function submitConfirmation(queueOnly) {
    if (confirmPending || confirmJobSubmitted) return;
    if (!confirmPermission.checked) {
      confirmInfo.textContent = "Confirm you have permission to save this video.";
      return;
    }
    const chosen = mediaFormats.find(f => f.id === qualityField.value);
    if (!chosen || confirmPageUrl !== currentPageUrl() ||
        confirmPageUrl !== mediaAnalyzedUrl) {
      confirmInfo.textContent = "The video or available formats changed. Close and refresh the menu.";
      return;
    }
    const fileStem = nameField.value.trim();
    if (!fileStem || [...fileStem].length > 80 ||
        !/^[\p{L}\p{N} _-]+$/u.test(fileStem)) {
      confirmInfo.textContent = "File name must contain only letters, numbers, spaces, _ or - (80 characters maximum).";
      return;
    }
    if (!["default", "videos"].includes(folderField.value)) return;
    setConfirmationBusy(true);
    confirmInfo.textContent = queueOnly ? "Saving for later in RangeDesk…" :
      "Sending confirmed download to RangeDesk…";
    try {
      const result = await chrome.runtime.sendMessage({
        action: "rangedesk_media_download", url: confirmPageUrl,
        formatId: chosen.id, rightsConfirmed: true,
        queueOnly, fileStem, saveLocation: folderField.value
      });
      if (!result?.accepted || typeof result.jobId !== "string" ||
          !/^[a-f0-9]{32}$/i.test(result.jobId)) {
        confirmInfo.textContent = typeof result?.message === "string"
          ? result.message : "RangeDesk could not accept the request.";
        return;
      }
      confirmJobSubmitted = true;
      mediaJobId = result.jobId;
      mediaJobUrl = confirmPageUrl;
      mediaJobDone = queueOnly;
      confirmInfo.textContent = queueOnly
        ? "Saved for later in RangeDesk Downloads. Use Resume there to start."
        : "Accepted by RangeDesk. Checking real download progress…";
      status.textContent = confirmInfo.textContent;
      if (rememberFolder.checked) {
        const hostname = new URL(confirmPageUrl).hostname;
        const key = "rangedesk_media_folder:" + hostname;
        try { await chrome.storage?.local?.set({ [key]: folderField.value }); } catch {}
      }
      if (currentPageUrl() === mediaJobUrl) {
        showMinimizedJob(queueOnly);
        if (!queueOnly) pollJob();
      }
    } catch {
      confirmInfo.textContent = "RangeDesk is unavailable. Keep the app open and retry.";
    } finally {
      setConfirmationBusy(false);
    }
  }
  startButton.addEventListener("click", () => { void submitConfirmation(false); });
  laterButton.addEventListener("click", () => { void submitConfirmation(true); });
  cancelButton.addEventListener("click", closeConfirmation);
  confirmX.addEventListener("click", closeConfirmation);
  confirmMask.addEventListener("keydown", event => {
    if (event.key === "Escape") { closeConfirmation(); event.stopPropagation(); }
  });
  qualityField.addEventListener("change", () => {
    confirmInfo.textContent = "Selected reported quality: " + qualityField.value;
  });

  function available(video) {
    const direct = sources.list(video, document.baseURI);
    const seen = new Set(direct.map(item => item.url));
    for (const item of observed) {
      if (direct.length >= 8) break;
      if (typeof item?.url !== "string" || item.format !== "HLS" ||
          seen.has(item.url)) continue;
      const valid = sources.directSource(item.url, document.baseURI,
        "application/vnd.apple.mpegurl");
      if (!valid || valid.format !== "HLS") continue;
      direct.push({ url: valid.url, format: "HLS",
        label: typeof item.label === "string" ? item.label : "Observed HLS (.ts)" });
      seen.add(valid.url);
    }
    return direct;
  }

  async function refreshObserved() {
    try {
      const reply = await chrome.runtime.sendMessage({ action: "rangedesk_media_list" });
      detecting = reply?.enabled === true;
      observed = detecting && Array.isArray(reply.sources) ? reply.sources.slice(0, 8) : [];
    } catch {
      detecting = false;
      observed = [];
    }
    scheduleRefresh();
  }

  function eligible(video) {
    if (!video || !video.isConnected) return false;
    const rect = video.getBoundingClientRect();
    if (rect.width < 230 || rect.height < 130 ||
        rect.bottom < 0 || rect.top > window.innerHeight ||
        rect.right < 0 || rect.left > window.innerWidth) return false;
    return detecting || available(video).length > 0 ||
      (typeof video.currentSrc === "string" &&
       video.currentSrc.startsWith("blob:")) ||
      (typeof video.readyState === "number" && video.readyState >= 1);
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

  async function sendPageForAnalysis(button) {
    if (pending) return;
    const pageUrl = typeof document.URL === "string" ? document.URL : document.baseURI;
    pending = true;
    button.disabled = true;
    status.textContent = "Opening this page in RangeDesk Media…";
    try {
      const response = await chrome.runtime.sendMessage({
        action: "rangedesk_media_page", url: pageUrl
      });
      status.textContent = response?.accepted
        ? "Sent to RangeDesk. Open the Media tab in Add Download."
        : "RangeDesk could not accept this page. Check its native connection.";
    } catch {
      status.textContent = "Chrome could not connect to RangeDesk.";
    } finally {
      pending = false; button.disabled = false;
    }
  }

  function renderChoices() {
    options.replaceChildren();
    qualityButtons = [];
    if (!active) return;
    const found = available(active);
    if (found.length === 0 && mediaFormats.length === 0 &&
        !mediaLoading && !mediaError && status.textContent === "") {
      status.textContent = detecting
        ? "No direct stream found. Checking the video page for qualities…"
        : "No direct source found. Checking the video page for qualities…";
    }
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
    if (mediaFormats.length > 0) {
      const title = document.createElement("div");
      title.className = "header";
      title.textContent = "Available qualities";
      options.appendChild(title);
      const selectionHint = document.createElement("div");
      selectionHint.className = "hint";
      selectionHint.textContent = "Click a quality to open RangeDesk download options";
      options.appendChild(selectionHint);
      mediaFormats.forEach(format => {
        const option = document.createElement("button");
        option.type = "button";
        option.className = "choice quality" + (selectedQualityId === format.id ? " selected" : "");
        option.setAttribute("aria-pressed", String(selectedQualityId === format.id));
        option.textContent = "↓ " + format.label;
        option.title = "Open RangeDesk download confirmation for this real quality";
        const select = () => {
          selectedQualityId = format.id;
          for (const item of qualityButtons) {
            const activeChoice = item.id === format.id;
            item.button.className = "choice quality" + (activeChoice ? " selected" : "");
            item.button.setAttribute("aria-pressed", String(activeChoice));
          }
        };
        qualityButtons.push({ id: format.id, button: option });
        option.addEventListener("click", () => {
          select();
          void openConfirmation(format, option);
        });
        options.appendChild(option);
      });
    } else if (mediaLoading) {
      const loading = document.createElement("div");
      loading.className = "hint";
      loading.textContent = "Finding available qualities…";
      options.appendChild(loading);
    } else if (mediaError) {
      const error = document.createElement("div");
      error.className = "hint";
      error.textContent = mediaError;
      options.appendChild(error);
    }
    // Page-based extractors handle sites whose video uses MSE/blob rather
    // than exposing a direct file or HLS manifest. Explicit user gesture.
    const page = document.createElement("button");
    page.type = "button";
    page.className = "page-choice";
    page.style.cssText = "cursor:pointer;display:block;width:100%;color:#eaf4ff;background:#245682;border:0;border-radius:7px;padding:9px 7px;margin-top:8px";
    page.textContent = "Open advanced Media options";
    page.title = "Analyze permitted video formats in RangeDesk; no automatic download";
    page.addEventListener("click", () => { void sendPageForAnalysis(page); });
    options.appendChild(page);
  }

  toggle.addEventListener("click", () => {
    if (!active) return;
    expanded = !expanded;
    menu.hidden = !expanded;
    toggle.setAttribute("aria-expanded", String(expanded));
    if (expanded) {
      status.textContent = "";
      if (mediaAnalyzedUrl !== currentPageUrl()) {
        mediaFormats = [];
        selectedQualityId = "";
        rightsConfirmed = false;
      }
      renderChoices();
      if (mediaJobId && !mediaJobDone) {
        status.textContent = "Checking current RangeDesk download…";
        pollJob();
      } else if (mediaJobId && mediaJobDone) {
        status.textContent = "Check the RangeDesk Media tab for the final job result.";
      }
      void analyzePageQualities();
    } else if (mediaJobTimer !== null) {
      clearTimeout(mediaJobTimer);
      mediaJobTimer = null;
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

  if (chrome.runtime.onMessage?.addListener)
    chrome.runtime.onMessage.addListener(message => {
      if (message?.action === "rangedesk_media_updated") {
        void refreshObserved();
      }
      return false;
    });
  scheduleRefresh();
  void refreshObserved();
})();
