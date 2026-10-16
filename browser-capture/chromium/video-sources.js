// Pure, local HTML5-video source discovery. No network interception, cookies,
// page scripts, DRM handling, playlist parsing, or guessed quality variants.
// Loaded before video-overlay.js inside Chrome's isolated content-script world.
(() => {
  "use strict";

  const FORMATS = Object.freeze({
    ".mp4": "MP4", ".m4v": "M4V", ".webm": "WebM",
    ".ogv": "Ogg video", ".ogg": "Ogg video", ".mov": "MOV",
    ".m3u8": "HLS"
  });
  const MIME_FORMATS = Object.freeze({
    "video/mp4": "MP4", "video/webm": "WebM",
    "video/ogg": "Ogg video", "video/quicktime": "MOV"
  });
  const BLOCKED = /\.(?:m3u8|mpd|m3u|ism|mp4\.m3u8)$/i;

  function directSource(value, base, type = "") {
    if (typeof value !== "string" || !value.trim()) return null;
    let parsed;
    try { parsed = new URL(value.trim(), base); }
    catch { return null; }
    if (!["https:", "http:"].includes(parsed.protocol) ||
        parsed.username || parsed.password || parsed.href.length > 4096)
      return null;

    const pathname = parsed.pathname.toLowerCase();
    if (BLOCKED.test(pathname)) return null;
    const ext = /\.[a-z0-9]+$/.exec(pathname)?.[0] || "";
    // Do not treat an arbitrary known non-video suffix as video because of
    // a misleading MIME declaration. Extensionless URLs need declared MIME.
    if (ext && !FORMATS[ext]) return null;
    const mime = typeof type === "string" ? type.split(";")[0].trim().toLowerCase() : "";
    const format = FORMATS[ext] || MIME_FORMATS[mime];
    if (!format) return null;
    return { url: parsed.href, format };
  }

  function list(video, base) {
    if (!video || typeof video.querySelectorAll !== "function") return [];
    const result = [];
    const known = new Set();
    const sources = Array.from(video.querySelectorAll("source[src]"));
    const matchingType = raw => {
      for (const source of sources) {
        try {
          if (new URL(source.getAttribute("src"), base).href === new URL(raw, base).href)
            return source.getAttribute("type") || "";
        } catch { /* Invalid markup */ }
      }
      return "";
    };
    const current = typeof video.currentSrc === "string" ? video.currentSrc : "";
    const height = Number.isFinite(video.videoHeight) && video.videoHeight >= 144
      ? Math.round(video.videoHeight) : null;

    function append(raw, type = "") {
      const source = directSource(raw, base, type);
      if (!source || known.has(source.url) || result.length >= 8) return;
      known.add(source.url);
      // Resolution belongs only to the actual playing source, NEVER
      // indiscriminately to every listed source on the page.
      const playing = Boolean(current) && source.url === directSource(current, base, type)?.url;
      result.push({
        url: source.url,
        format: source.format,
        label: source.format + (playing && height ? " · " + height + "p" : "")
      });
    }

    append(current, matchingType(current));
    const elementSrc = typeof video.getAttribute === "function" ? video.getAttribute("src") : "";
    append(elementSrc, matchingType(elementSrc));
    for (const source of sources)
      append(source.getAttribute("src"), source.getAttribute("type") || "");
    return result;
  }

  globalThis.RangeDeskVideoSources = Object.freeze({ directSource, list });
})();
