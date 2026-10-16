// Isolated, pure checks for Chrome's opt-in, per-tab HLS network metadata.
// No request/response bodies, cookies, headers, authorization or page text.
// Storage stays in Chrome's session-only extension state, never persisted to disk.
(() => {
  "use strict";
  const MAX_SOURCES = 8;
  const WATCH_MS = 15 * 60 * 1000;

  function originOf(url) {
    if (typeof url !== "string") return null;
    try {
      const uri = new URL(url);
      return (uri.protocol === "http:" || uri.protocol === "https:") &&
        !uri.username && !uri.password ? uri.origin : null;
    } catch { return null; }
  }

  function eligibleHls(url) {
    if (typeof url !== "string" || url.length > 4096) return false;
    try {
      const parsed = new URL(url);
      return Boolean(originOf(url)) &&
        parsed.pathname.toLowerCase().endsWith(".m3u8") &&
        !parsed.hash;  // Fragments can hide unsupported custom instructions.
    } catch { return false; }
  }

  function beginWatch(pageUrl, now) {
    const origin = originOf(pageUrl);
    return origin ? { origin, expiresAt: now + WATCH_MS, sources: [] } : null;
  }

  function observe(details, entry, now) {
    if (!entry || !entry.origin || entry.expiresAt <= now ||
        !Number.isInteger(details?.tabId) || details.tabId < 0 ||
        !Number.isInteger(details?.frameId) || details.frameId < 0 ||
        details.statusCode !== 200 ||
        !["xmlhttprequest", "media", "other"].includes(details.type) ||
        originOf(details.initiator) !== entry.origin ||
        !eligibleHls(details.url)) return null;

    const rest = Array.isArray(entry.sources) ? entry.sources : [];
    const current = rest.filter(item =>
      typeof item?.url === "string" && item.url !== details.url &&
      Number.isInteger(item.frameId) && item.expiresAt > now
    );
    current.unshift({
      url: details.url, frameId: details.frameId,
      expiresAt: Math.min(entry.expiresAt, now + WATCH_MS)
    });
    return { origin: entry.origin, expiresAt: entry.expiresAt,
      sources: current.slice(0, MAX_SOURCES) };
  }

  function listing(entry, pageUrl, frameId, now) {
    if (!entry || entry.expiresAt <= now ||
        entry.origin !== originOf(pageUrl) || !Number.isInteger(frameId))
      return { enabled: false, sources: [] };

    return {
      enabled: true,
      sources: (Array.isArray(entry.sources) ? entry.sources : [])
        .filter(item => item.frameId === frameId &&
          item.expiresAt > now && eligibleHls(item.url))
        .slice(0, MAX_SOURCES)
        .map((item, index) => ({
          url: item.url, format: "HLS",
          label: "Observed HLS · stream " + (index + 1) + " (.ts)"
        }))
    };
  }

  globalThis.RangeDeskMediaDiscovery = Object.freeze({
    beginWatch, observe, listing, originOf, eligibleHls, WATCH_MS
  });
})();
