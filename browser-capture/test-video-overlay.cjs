// Deterministic Chrome Phase 2 tests, using only Node's built-in test runner.
// No browser data, external network, npm dependencies or native host needed.
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");

const folder = path.join(__dirname, "chromium");
const scripts = name => fs.readFileSync(path.join(folder, name), "utf8");

function extractor() {
  const context = vm.createContext({ URL, Set, Array, Number });
  vm.runInContext(scripts("video-sources.js"), context);
  return context.RangeDeskVideoSources;
}

function fakeVideo({ src = "", current = "", height = 0, sources = [] } = {}) {
  return {
    currentSrc: current, videoHeight: height,
    getAttribute: attr => attr === "src" ? src : null,
    querySelectorAll: selector => {
      assert.equal(selector, "source[src]");
      return sources.map(({ url, type }) => ({
        getAttribute: attr => attr === "src" ? url : attr === "type" ? type : null
      }));
    }
  };
}

test("only genuine HTTP(S) direct video sources are eligible", () => {
  const { directSource } = extractor();
  const base = "https://example.test/watch";
  assert.equal(directSource("/assets/movie.mp4?sig=private", base).format, "MP4");
  assert.equal(directSource("https://cdn.test/file.webm", base).format, "WebM");
  assert.equal(directSource("/stream?id=abc", base, "video/mp4; codecs=avc1").format, "MP4");
  for (const url of [
    "blob:https://example.test/secret", "data:video/mp4;base64,abc",
    "javascript:alert(1)", "file:///C:/movie.mp4", "https://u:p@example.test/a.mp4",
    "https://example.test/live.mpd",
    "https://example.test/watch.html", "https://example.test/song.mp3"
  ]) {
    assert.equal(directSource(url, base, "video/mp4"), null, url);
  }
  assert.equal(directSource("/api/download?id=1", base), null,
    "Extensionless endpoints require a declared MIME type.");
});

test("HLS source is exposed as VOD auto-quality, not falsely labeled MP4", () => {
  const probe = extractor();
  const source = probe.directSource("https://cdn.test/catalog.m3u8", "https://site.test/",
    "application/vnd.apple.mpegurl");
  assert.equal(source.format, "HLS");
  const found = probe.list(fakeVideo({
    current: "https://cdn.test/catalog.m3u8", height: 1080
  }), "https://site.test/");
  assert.equal(found.length, 1);
  assert.equal(found[0].label, "HLS · VOD auto quality (.ts)");
});

test("Chrome sends clicked HLS playlist URL to the ordinary review inbox", async () => {
  const bg = chromeBackground();
  const reply = await bg.message({
    action: "rangedesk_video_source",
    url: "https://cdn.test/playlist.m3u8?sig=abc", format: "HLS"
  });
  assert.equal(reply.reply.accepted, true);
  assert.equal(bg.sent.length, 1);
  assert.equal(bg.sent[0].message.action, "add_link");
});

test("deduplicates sources and labels resolution only for playing variant", () => {
  const found = extractor().list(fakeVideo({
    current: "https://cdn.test/1080.mp4?token=signed",
    height: 1080,
    sources: [
      { url: "https://cdn.test/1080.mp4?token=signed", type: "video/mp4" },
      { url: "https://cdn.test/720.webm", type: "video/webm" },
      { url: "blob:https://site.test/local", type: "video/mp4" }
    ]
  }), "https://site.test/watch");
  assert.equal(found.length, 2);
  assert.equal(found[0].label, "MP4 · 1080p");
  assert.equal(found[1].label, "WebM");
  assert.equal(found[0].url, "https://cdn.test/1080.mp4?token=signed");
});

test("relative MIME-labeled source resolves correctly; no invented quality", () => {
  const found = extractor().list(fakeVideo({
    sources: [{ url: "../direct-stream?id=123", type: "video/mp4" }]
  }), "https://site.test/videos/watch");
  assert.equal(found.length, 1);
  assert.equal(found[0].url, "https://site.test/direct-stream?id=123");
  assert.equal(found[0].label, "MP4");
});

test("bounded options and unsupported video-only sites remain empty", () => {
  const list = extractor().list;
  assert.equal(list(fakeVideo({ current: "blob:https://site.test/blob" }), "https://site.test/").length, 0);
  const many = Array.from({ length: 14 }, (_, i) => ({
    url: "https://site.test/media/" + i + ".mp4", type: "video/mp4"
  }));
  assert.equal(list(fakeVideo({ sources: many }), "https://site.test/").length, 8);
});

test("Chrome manifest installs ordered isolated scripts on regular web pages", () => {
  const manifest = JSON.parse(scripts("manifest.json"));
  assert.equal(manifest.manifest_version, 3);
  assert.deepEqual(manifest.content_scripts[0].js, ["video-sources.js", "video-overlay.js"]);
  assert.deepEqual(manifest.content_scripts[0].matches, ["http://*/*", "https://*/*"]);
  assert.equal(manifest.content_scripts[0].all_frames, true);
  assert.ok(manifest.permissions.includes("webRequest"));
  assert.ok(!manifest.permissions.includes("webRequestBlocking"));
  assert.deepEqual(manifest.optional_host_permissions, ["<all_urls>"]);
  assert.ok(!manifest.permissions.includes("cookies"));
});

function chromeBackground(nativeReply = { accepted: true }) {
  const handlers = {};
  const sent = [];
  const event = name => ({ addListener: fn => { handlers[name] = fn; } });
  const chrome = {
    runtime: {
      id: "extension-under-test",
      onInstalled: event("installed"),
      onStartup: event("startup"),
      onMessage: event("message"),
      sendNativeMessage: async (host, message) => {
        sent.push({ host, message });
        if (nativeReply instanceof Error) throw nativeReply;
        return nativeReply;
      }
    },
    contextMenus: { create: () => {}, onClicked: event("menu") },
    action: {
      onClicked: event("toggle"),
      setBadgeText: async () => {},
      setBadgeBackgroundColor: async () => {},
      setTitle: async () => {}
    },
    storage: { local: { get: async () => ({}), set: async () => {} } },
    downloads: { onCreated: event("created") }
  };
  vm.runInNewContext(scripts("media-discovery.js") + "\n" + scripts("background.js"), {
    chrome, importScripts: () => {}, URL, Number, Set, console
  });
  const sender = { id: chrome.runtime.id, tab: { id: 19 }, url: "https://site.test/watch" };
  async function message(payload, source = sender) {
    let reply;
    const keepsChannel = handlers.message(payload, source, value => { reply = value; });
    if (keepsChannel === true)
      for (let i = 0; i < 50 && reply === undefined; i++)
        await new Promise(resolve => setTimeout(resolve, 1));
    return { reply, keepsChannel };
  }
  return { sent, message, sender };
}

test("Chrome background accepts explicit video click without other browser data", async () => {
  const bg = chromeBackground();
  const result = await bg.message({
    action: "rangedesk_video_source", url: "https://media.test/movie.mp4?sig=secret", format: "MP4"
  });
  assert.equal(result.reply.accepted, true);
  assert.equal(bg.sent.length, 1);
  assert.deepEqual(Object.keys(bg.sent[0].message), ["action", "url"]);
  assert.equal(bg.sent[0].message.action, "add_link");
});

test("extensionless MIME-labeled direct video endpoints work through native host", async () => {
  const bg = chromeBackground();
  const result = await bg.message({
    action: "rangedesk_video_source", url: "https://cdn.test/stream?id=123", format: "MP4"
  });
  assert.equal(result.reply.accepted, true);
  assert.equal(bg.sent.length, 1);
});

test("reject spoofed origin, playlists, credentials, unknown suffixes and non-video", async () => {
  const bg = chromeBackground();
  const valid = "https://media.test/movie.mp4";
  const cases = [
    [{ action: "rangedesk_video_source", url: valid }, { id: "other-extension", tab: { id: 1 }, url: "https://site.test/" }],
    [{ action: "rangedesk_video_source", url: valid }, { id: bg.sender.id, url: "https://site.test/" }],
    [{ action: "rangedesk_video_source", url: valid }, { id: bg.sender.id, tab: { id: 1 }, url: "chrome://extensions" }],
    [{ action: "rangedesk_video_source", url: "https://cdn.test/live.m3u8", format: "MP4" }, bg.sender],
    [{ action: "rangedesk_video_source", url: "https://cdn.test/watch.html", format: "MP4" }, bg.sender],
    [{ action: "rangedesk_video_source", url: "https://user:secret@cdn.test/v.mp4", format: "MP4" }, bg.sender],
    [{ action: "rangedesk_video_source", url: "blob:https://cdn.test/123", format: "MP4" }, bg.sender],
    [{ action: "rangedesk_video_source", url: "https://cdn.test/unknown", format: "Fake" }, bg.sender]
  ];
  for (const [message, sender] of cases)
    assert.equal((await bg.message(message, sender)).reply.accepted, false);
  assert.equal(bg.sent.length, 0);
});

test("Chrome page handoff opens media analysis without inventing a direct video URL", async () => {
  const bg = chromeBackground();
  const url = "https://site.test/watch";
  const result = await bg.message({ action: "rangedesk_media_page", url });
  assert.equal(result.reply.accepted, true);
  assert.equal(bg.sent.length, 1);
  assert.equal(bg.sent[0].message.action, "add_link");
  assert.equal(bg.sent[0].message.url, url);
});

test("Chrome media page handoff rejects spoofed or credentialed URLs", async () => {
  const bg = chromeBackground();
  const cases = [
    [{ action: "rangedesk_media_page", url: "https://evil.test/watch" }, bg.sender],
    [{ action: "rangedesk_media_page", url: "file:///etc/hosts" }, bg.sender],
    [{ action: "rangedesk_media_page", url: "https://u:pass@site.test/watch" }, bg.sender],
    [{ action: "rangedesk_media_page", url: "https://site.test/watch" },
      { ...bg.sender, id: "other-extension" }]
  ];
  for (const [msg, sender] of cases)
    assert.equal((await bg.message(msg, sender)).reply.accepted, false);
  assert.equal(bg.sent.length, 0);
});

test("Chrome shows actual extracted qualities from its native yt-dlp bridge", async () => {
  const bg = chromeBackground({ accepted: true, formats: [
    { id: "1080webm", label: "1080p · WEBM · 1080webm (merge separate audio)", height: 1080, needsAudioMerge: true },
    { id: "720mp4", label: "720p · MP4 · 720mp4 (video + audio)", height: 720, needsAudioMerge: false }
  ] });
  const actual = await bg.message({
    action: "rangedesk_media_probe", url: bg.sender.url
  });
  assert.equal(actual.reply.accepted, true);
  assert.equal(actual.reply.formats.length, 2);
  assert.equal(actual.reply.formats[0].id, "1080webm");
  assert.equal(bg.sent[0].message.action, "media_probe");
  assert.equal(bg.sent[0].message.url, bg.sender.url);
});

test("Chrome queues user-confirmed quality, never downloads silently", async () => {
  const bg = chromeBackground();
  const msg = { action: "rangedesk_media_download",
    url: bg.sender.url, formatId: "1080webm", rightsConfirmed: true };
  const result = await bg.message(msg);
  assert.equal(result.reply.accepted, true);
  assert.equal(bg.sent[0].message.action, "media_download");
  assert.equal(bg.sent[0].message.formatId, "1080webm");
  assert.equal(bg.sent[0].message.rightsConfirmed, true);
});

test("Chrome rejects unconfirmed, spoofed, malicious and credentialed video page requests", async () => {
  const bg = chromeBackground();
  const cases = [
    { action: "rangedesk_media_probe", url: "https://other.test/watch" },
    { action: "rangedesk_media_probe", url: "file:///tmp/video" },
    { action: "rangedesk_media_download", url: bg.sender.url,
      formatId: "1080webm", rightsConfirmed: false },
    { action: "rangedesk_media_download", url: bg.sender.url,
      formatId: "../../bad", rightsConfirmed: true },
    { action: "rangedesk_media_download", url: "https://user:pass@site.test/watch",
      formatId: "720mp4", rightsConfirmed: true }
  ];
  for (const invalid of cases) {
    assert.equal((await bg.message(invalid)).reply.accepted, false);
  }
  assert.equal(bg.sent.length, 0);
  assert.equal((await bg.message({action:"rangedesk_media_probe",url:bg.sender.url},
    {...bg.sender,id:"another-extension"})).reply.accepted, false);
});

test("native host rejection or absence does not claim video was queued", async () => {
  for (const rejected of [{ accepted: false }, new Error("no host")]) {
    const bg = chromeBackground(rejected);
    const result = await bg.message({
      action: "rangedesk_video_source", url: "https://cdn.test/movie.webm", format: "WebM"
    });
    assert.equal(result.reply.accepted, false);
  }
});

// Small DOM mock for an in-page interaction regression (no third-party deps).
test("player overlay appears and clicking exposed format sends link for review", async () => {
  const handlers = {};
  const elements = [];
  class Element {
    constructor(tag) {
      this.tagName = tag.toUpperCase();
      this.children = []; this.listeners = {}; this.attributes = {};
      this.style = {}; this.isConnected = true; this.hidden = false;
      this.textContent = ""; this.className = "";
      elements.push(this);
    }
    append(...children) { this.children.push(...children); }
    appendChild(child) { this.children.push(child); }
    replaceChildren(...children) { this.children = children; }
    attachShadow() { return new Element("shadow-root"); }
    setAttribute(key, value) { this.attributes[key] = value; }
    getAttribute(key) { return this.attributes[key] ?? null; }
    addEventListener(type, handler) { this.listeners[type] = handler; }
    getBoundingClientRect() { return { left: 10, top: 20, right: 650, bottom: 400, width: 640, height: 380 }; }
    querySelectorAll() { return []; }
    contains(x) { return x === this || this.children.includes(x); }
    focus() {}
  }
  const video = new Element("video");
  video.currentSrc = "https://cdn.test/movies/clip.mp4";
  video.videoHeight = 720;
  const document = {
    baseURI: "https://site.test/page", documentElement: new Element("html"),
    createElement: tag => new Element(tag),
    querySelectorAll: selector => selector === "video" ? [video] : [],
    addEventListener: (type, cb) => { handlers[type] = cb; }
  };
  const window = { innerHeight: 900, innerWidth: 1300, addEventListener: () => {} };
  const sent = [];
  const chrome = { runtime: { id: "extension-under-test",
    sendMessage: async message => { sent.push(message); return { accepted: true }; } } };
  const context = vm.createContext({
    URL, Array, Number, Set, document, window, chrome,
    MutationObserver: class { observe() {} },
    requestAnimationFrame: callback => callback()
  });
  vm.runInContext(scripts("video-sources.js"), context);
  vm.runInContext(scripts("video-overlay.js"), context);

  const overlay = elements.find(el => el.getAttribute("data-rangedesk-video-controls") === "");
  assert.ok(overlay, "In-page overlay missing");
  assert.equal(overlay.style.display, "block");
  const toggle = elements.find(el => el.className === "toggle");
  assert.ok(toggle);
  toggle.listeners.click();
  const visibleOptions = elements.find(el => el.className === "menu").children[2];
  const choices = visibleOptions.children.filter(el => el.className === "choice");
  assert.equal(choices.length, 1);
  assert.equal(choices[0].textContent, "MP4 · 720p");
  choices[0].listeners.click();
  await new Promise(resolve => setTimeout(resolve, 0));
  const selected = sent.filter(message => message.action === "rangedesk_video_source");
  assert.equal(selected.length, 1);
  assert.equal(selected[0].url, "https://cdn.test/movies/clip.mp4");
  assert.equal(selected[0].format, "MP4");
});

test("blob player shows observed HLS in the overlay only after tab opt-in", async () => {
  const els = [];
  class Element {
    constructor(tag) {
      this.tagName = tag.toUpperCase(); this.children = [];
      this.listeners = {}; this.attributes = {}; this.style = {};
      this.isConnected = true; this.hidden = false;
      this.className = ""; this.textContent = "";
      els.push(this);
    }
    append(...nodes) { this.children.push(...nodes); }
    appendChild(node) { this.children.push(node); }
    replaceChildren(...nodes) { this.children = nodes; }
    attachShadow() { return new Element("shadow-root"); }
    setAttribute(k, v) { this.attributes[k] = v; }
    getAttribute(k) { return this.attributes[k] ?? null; }
    addEventListener(k, listener) { this.listeners[k] = listener; }
    querySelectorAll() { return []; }
    getBoundingClientRect() {
      return { left: 5, top: 20, right: 700, bottom: 380, width: 695, height: 360 };
    }
    contains(node) { return node === this || this.children.includes(node); }
    focus() {}
  }
  const video = new Element("video");
  video.currentSrc = "blob:https://site.test/some-media-source";
  video.videoHeight = 1080;
  const document = {
    baseURI: "https://site.test/watch",
    documentElement: new Element("html"),
    createElement: type => new Element(type),
    querySelectorAll: selector => selector === "video" ? [video] : [],
    addEventListener: () => {}
  };
  const sent = []; const listeners = {};
  const chrome = { runtime: {
    id: "under-test",
    onMessage: { addListener: fn => { listeners.message = fn; } },
    sendMessage: async message => {
      sent.push(message);
      if (message.action === "rangedesk_media_list") return {
        enabled: true,
        sources: [{
          url: "https://cdn.test/stream/master.m3u8?sig=secret",
          format: "HLS", label: "Observed HLS · stream 1 (.ts)"
        }]
      };
      if (message.action === "rangedesk_media_probe") return {
        accepted: true,
        formats: [
          { id: "hd1080", label: "1080p · WEBM · hd1080 (merge separate audio)",
            height: 1080, needsAudioMerge: true },
          { id: "sd720", label: "720p · MP4 · sd720 (video + audio)",
            height: 720, needsAudioMerge: false }
        ]
      };
      return { accepted: true };
    }
  }};
  const scope = vm.createContext({
    document, window: { innerHeight: 800, innerWidth: 1300, addEventListener: () => {} },
    chrome, URL, Number, Array, Set,
    requestAnimationFrame: callback => callback(),
    MutationObserver: class { observe() {} }
  });
  vm.runInContext(scripts("video-sources.js"), scope);
  vm.runInContext(scripts("video-overlay.js"), scope);
  await new Promise(resolve => setTimeout(resolve, 1));
  const overlay = els.find(x => x.getAttribute("data-rangedesk-video-controls") === "");
  assert.ok(overlay);
  assert.equal(overlay.style.display, "block");
  const toggle = els.find(x => x.className === "toggle");
  toggle.listeners.click();
  const visibleOptions = els.find(el => el.className === "menu").children[2];
  const choices = visibleOptions.children.filter(x => x.className === "choice");
  assert.equal(choices.length, 1);
  assert.match(choices[0].textContent, /Observed HLS/);
  choices[0].listeners.click();
  await new Promise(resolve => setTimeout(resolve, 1));
  const handoffs = sent.filter(x => x.action === "rangedesk_video_source");
  assert.equal(handoffs.length, 1);
  assert.equal(handoffs[0].format, "HLS");
  assert.equal(handoffs[0].url, "https://cdn.test/stream/master.m3u8?sig=secret");
  // The observed URL is never automatically sent just because a player exists.
  assert.ok(sent.filter(x => x.action === "rangedesk_media_list").length >= 1);
  const qualities = visibleOptions.children.filter(x => x.className === "choice quality");
  assert.equal(qualities.length, 2);
  assert.match(qualities[0].textContent, /1080p/);
  // One click opens the browser modal. Nothing has been downloaded yet.
  qualities[0].listeners.click();
  await new Promise(resolve => setTimeout(resolve, 2));
  const modal = els.find(x => x.className === "confirm-mask");
  assert.ok(modal && !modal.hidden);
  assert.equal(sent.filter(x => x.action === "rangedesk_media_download").length, 0);
});

test("media probe exposes a structured safe analyzer error", async () => {
  const bg = chromeBackground({ accepted: false, errorCode: "JS_RUNTIME_MISSING",
    message: "Deno 2.3 or newer is required for YouTube." });
  const result = await bg.message({ action: "rangedesk_media_probe", url: bg.sender.url });
  assert.equal(result.reply.accepted, false);
  assert.equal(result.reply.errorCode, "JS_RUNTIME_MISSING");
  assert.match(result.reply.message, /Deno/);
  assert.equal(result.reply.formats.length, 0);
});

test("queued media acceptance returns a job ID, never completion", async () => {
  const jobId = "0123456789abcdef0123456789abcdef";
  const bg = chromeBackground({ accepted: true, jobId, state: "queued",
    message: "Selection received by RangeDesk." });
  const result = await bg.message({ action: "rangedesk_media_download",
    url: bg.sender.url, formatId: "hd1080", rightsConfirmed: true });
  assert.equal(result.reply.accepted, true);
  assert.equal(result.reply.jobId, jobId);
  assert.equal(result.reply.state, "queued");
  assert.ok(!/complete|started/i.test(result.reply.message));
});

test("media status polling validates sender page and job ID", async () => {
  const jobId = "0123456789abcdef0123456789abcdef";
  const bg = chromeBackground({ accepted: true, jobId, state: "downloading",
    downloadedBytes: 1048576, totalBytes: null, estimated: false,
    message: "Downloading selected stream" });
  const valid = await bg.message({
    action: "rangedesk_media_status", url: bg.sender.url, jobId
  });
  assert.equal(valid.reply.accepted, true);
  assert.equal(valid.reply.state, "downloading");
  assert.equal(valid.reply.downloadedBytes, 1048576);
  assert.equal(valid.reply.totalBytes, null);
  assert.deepEqual(Object.keys(bg.sent[0].message), ["action", "jobId"]);
  const invalid = await bg.message({
    action: "rangedesk_media_status", url: "https://other.test/watch", jobId
  });
  assert.equal(invalid.reply.accepted, false);
  const fake = await bg.message({
    action: "rangedesk_media_status", url: bg.sender.url, jobId: "../../hello"
  });
  assert.equal(fake.reply.accepted, false);
  assert.equal(bg.sent.length, 1);
});

test("YouTube SPA navigation accepts a browser-verified current tab URL, not a forged page", async () => {
  const oldUrl = "https://www.youtube.com/watch?v=aaaaaaaaaaa";
  const nextUrl = "https://www.youtube.com/watch?v=bbbbbbbbbbb";
  const bg = chromeBackground({accepted: true, formats: [
    {id:"hd1080",label:"1080p · WEBM · hd1080",height:1080,needsAudioMerge:true}
  ]});
  const sender = {...bg.sender, frameId: 0, origin: "https://www.youtube.com",
    url: oldUrl, tab: {id: 19, url: nextUrl}};
  const probe = await bg.message({action:"rangedesk_media_probe", url:nextUrl}, sender);
  assert.equal(probe.reply.accepted,true);
  assert.equal(probe.reply.formats[0].id,"hd1080");
  assert.equal(bg.sent[0].message.url,nextUrl);
  const selected = await bg.message({action:"rangedesk_media_download",
    url:nextUrl, formatId:"hd1080", rightsConfirmed:true}, sender);
  assert.equal(selected.reply.accepted,true);
  const advanced = await bg.message({action:"rangedesk_media_page", url:nextUrl}, sender);
  assert.equal(advanced.reply.accepted,true);
});

test("YouTube restricted same-origin navigation fallback works without tab URL", async () => {
  const oldUrl="https://www.youtube.com/watch?v=aaaaaaaaaaa";
  const nextUrl="https://www.youtube.com/shorts/bbbbbbbbbbb";
  const bg=chromeBackground({accepted:true,formats:[{id:"720",label:"720p",height:720}]});
  const sender={...bg.sender,frameId:0,origin:"https://www.youtube.com",url:oldUrl,tab:{id:19}};
  const result=await bg.message({action:"rangedesk_media_probe",url:nextUrl},sender);
  assert.equal(result.reply.accepted,true);
  assert.equal(bg.sent[0].message.url,nextUrl);
});

test("YouTube embedded players permit verified same-origin iframe navigation only", async () => {
  const bg=chromeBackground({accepted:true,formats:[{id:"720",label:"720p",height:720}]});
  const sender={...bg.sender,frameId:3,origin:"https://www.youtube.com",
    url:"https://www.youtube.com/embed/aaaaaaaaaaa",
    tab:{id:19,url:"https://news.test/article"}};
  const next="https://www.youtube.com/embed/bbbbbbbbbbb";
  assert.equal((await bg.message({action:"rangedesk_media_probe",url:next},sender)).reply.accepted,true);
  assert.equal((await bg.message({action:"rangedesk_media_probe",url:"https://news.test/article"},sender)).reply.accepted,false);
  assert.equal(bg.sent.length,1);
});

test("Video request rejects forged origins, stale tab URLs and arbitrary same-site routes", async () => {
  const bg=chromeBackground({accepted:true,formats:[{id:"720",label:"720p",height:720}]});
  const oldUrl="https://www.youtube.com/watch?v=aaaaaaaaaaa";
  const nextUrl="https://www.youtube.com/watch?v=bbbbbbbbbbb";
  const origin="https://www.youtube.com";
  const mismatch={...bg.sender,frameId:0,origin,tab:{id:19,url:oldUrl},url:oldUrl};
  const cases=[
    [{action:"rangedesk_media_probe",url:nextUrl},mismatch],
    [{action:"rangedesk_media_probe",url:nextUrl},{...mismatch,origin:"https://evil.test",tab:{id:19,url:nextUrl}}],
    [{action:"rangedesk_media_probe",url:"https://www.youtube.com/account"},{...mismatch,tab:{id:19}}],
    [{action:"rangedesk_media_probe",url:"https://www.youtube.com/watch?v=bbbbbbbbbbb"},
      {...mismatch,frameId:3,tab:{id:19,url:nextUrl},origin:"https://evil.test"}],
    [{action:"rangedesk_media_probe",url:"https://youtube.com/watch?v=bbbbbbbbbbb"},
      {...mismatch,tab:{id:19,url:nextUrl}}],
    [{action:"rangedesk_media_probe",url:"https://www.youtube.com/watch?v=ccccccccccc"},
      {...mismatch,url:"https://www.youtube.com/account",tab:{id:19}}],
    [{action:"rangedesk_media_probe",url:"https://www.youtube.com/watch?v=bbbbbbbbbbb"},
      {...mismatch,frameId:3,url:"https://www.youtube.com/account",tab:{id:19,url:nextUrl}}]
  ];
  for(const [msg,sender] of cases) {
    const res=await bg.message(msg,sender);
    assert.equal(res.reply.accepted,false);
    assert.equal(res.reply.errorCode,"PAGE_IDENTITY_MISMATCH");
  }
  assert.equal(bg.sent.length,0);
});

test("Denied video probe yields a useful rejection instead of a misleading success", async () => {
  const bg=chromeBackground();
  const result=await bg.message({action:"rangedesk_media_probe",url:"https://evil.test/watch"});
  assert.equal(result.reply.accepted,false);
  assert.equal(result.reply.errorCode,"PAGE_IDENTITY_MISMATCH");
  assert.match(result.reply.message,/Refresh this video/);
  assert.equal(bg.sent.length,0);
});

function mockPlayerOverlay(sendMessage, { fastPoll = false } = {}) {
  const elements=[];
  class Element {
    constructor(tag) {
      this.tagName=tag.toUpperCase(); this.children=[]; this.listeners={};
      this.attributes={}; this.style={}; this.isConnected=true;
      this.hidden=false; this.className=""; this.textContent="";
      elements.push(this);
    }
    append(...items){this.children.push(...items);}
    appendChild(item){this.children.push(item);}
    replaceChildren(...items){this.children=items;}
    attachShadow(){return new Element("shadow-root");}
    setAttribute(key,value){this.attributes[key]=value;}
    getAttribute(key){return this.attributes[key]??null;}
    addEventListener(key,cb){this.listeners[key]=cb;}
    getBoundingClientRect(){return {left:0,top:0,right:750,bottom:400,width:750,height:400};}
    querySelectorAll(){return [];}
    contains(x){return x===this||this.children.includes(x);}
    focus(){}
  }
  const video=new Element("video");
  video.currentSrc="blob:https://www.youtube.com/fake"; video.readyState=1; video.videoHeight=1080;
  const document={
    title:"Permitted sample - YouTube",
    URL:"https://www.youtube.com/watch?v=aaaaaaaaaaa",
    baseURI:"https://www.youtube.com/watch?v=aaaaaaaaaaa",
    documentElement:new Element("html"),
    createElement:type=>new Element(type),
    querySelectorAll:sel=>sel==="video"?[video]:[],
    addEventListener:()=>{}
  };
  const chrome={runtime:{id:"extension-under-test",sendMessage:async message=>{
    if(message.action==="rangedesk_media_list")return {enabled:false,sources:[]};
    return sendMessage(message);
  }}};
  const context=vm.createContext({URL,document,window:{innerHeight:800,innerWidth:1400,addEventListener:()=>{}},
    chrome,Number,Array,Set,setTimeout:(fn,ms)=>setTimeout(fn,fastPoll&&ms===1500?12:ms),clearTimeout,
    MutationObserver:class{observe(){}},
    requestAnimationFrame:cb=>cb()});
  vm.runInContext(scripts("video-sources.js"),context);
  vm.runInContext(scripts("video-overlay.js"),context);
  return {elements,document,toggle:elements.find(e=>e.className==="toggle"),
    status:elements.find(e=>e.className==="status"),
    options:elements.find(e=>e.className==="menu").children[2]};
}

test("Rejected page probe displays error and clears Finding available qualities status", async () => {
  const ui=mockPlayerOverlay(async()=>({accepted:false,errorCode:"PAGE_IDENTITY_MISMATCH",
    message:"The video page changed or its frame could not be verified. Refresh this video and retry."}));
  assert.ok(ui.toggle);
  ui.toggle.listeners.click();
  await new Promise(resolve=>setTimeout(resolve,5));
  const error=ui.options.children.find(e=>e.className==="hint"&&/Refresh this video/.test(e.textContent));
  assert.ok(error,"Rejection should display in the qualities section");
  assert.doesNotMatch(ui.status.textContent,/Finding available/);
});

test("Video analysis errors show actionable safe code, not private extractor output", async () => {
  const ui=mockPlayerOverlay(async()=>({
    accepted:false,errorCode:"RATE_LIMITED",
    message:"The video site is limiting requests. Wait before trying again."
  }));
  ui.toggle.listeners.click();
  await new Promise(resolve=>setTimeout(resolve,5));
  const error=ui.options.children.find(e=>e.className==="hint"&&/RATE_LIMITED/.test(e.textContent));
  assert.ok(error);
  assert.match(error.textContent,/Wait before trying again/);
  assert.doesNotMatch(error.textContent,/token=|http/i);
});

test("New YouTube SPA probe can finish before an older in-flight probe", async () => {
  let resolveOld;
  const oldProbe=new Promise(resolve=>{resolveOld=resolve;});
  const ui=mockPlayerOverlay(message=>{
    if(message.action!=="rangedesk_media_probe")return {accepted:false};
    if(message.url.includes("aaaaaaaaaaa"))return oldProbe;
    return {accepted:true,formats:[{id:"hd1080",label:"1080p · WEBM",height:1080}]};
  });
  ui.toggle.listeners.click();
  ui.toggle.listeners.click();
  ui.document.URL="https://www.youtube.com/watch?v=bbbbbbbbbbb";
  ui.toggle.listeners.click();
  await new Promise(resolve=>setTimeout(resolve,5));
  assert.ok(ui.options.children.some(e=>e.className==="choice quality"&&/1080p/.test(e.textContent)));
  resolveOld({accepted:false,message:"Old video should not overwrite the new one."});
  await new Promise(resolve=>setTimeout(resolve,5));
  assert.equal(ui.options.children.filter(e=>e.className==="choice quality").length,1);
  assert.ok(!ui.options.children.some(e=>/Old video should not/.test(e.textContent)));
});


test("one quality click opens a browser-native confirmation without a native download", async()=>{
  const messages=[], jobId="0123456789abcdef0123456789abcdef";
  const ui=mockPlayerOverlay(async message=>{
    messages.push(message);
    if(message.action==="rangedesk_media_probe")
      return {accepted:true,formats:[
        {id:"f1080",label:"1080p · MP4",height:1080,needsAudioMerge:true},
        {id:"f720",label:"720p · MP4",height:720,needsAudioMerge:false}]};
    if(message.action==="rangedesk_media_download")return {accepted:true,jobId};
    if(message.action==="rangedesk_media_status")return {
      accepted:true,jobId,state:"completed",message:"Output verified"};
    return {accepted:false};
  });
  ui.toggle.listeners.click();await new Promise(r=>setTimeout(r,5));
  const quality=ui.options.children.find(x=>x.className.startsWith("choice quality"));
  quality.listeners.click();
  await new Promise(r=>setTimeout(r,2));
  const modal=ui.elements.find(x=>x.className==="confirm-mask");
  assert.equal(modal.hidden,false);
  assert.equal(messages.filter(m=>m.action==="rangedesk_media_download").length,0);
  const start=ui.elements.find(x=>x.className==="confirm-btn primary");
  const note=ui.elements.find(x=>x.className==="confirm-note" && x.attributes.role==="status");
  start.listeners.click();await new Promise(r=>setTimeout(r,1));
  assert.match(note.textContent,/permission/);
  assert.equal(messages.filter(m=>m.action==="rangedesk_media_download").length,0);
  const permission=ui.elements.find(x=>x.className==="confirm-rights" &&
    x.children.some(y=>y.textContent==="I have permission to save this video")).children[0];
  permission.checked=true;
  start.listeners.click();await new Promise(r=>setTimeout(r,3));
  const sent=messages.filter(m=>m.action==="rangedesk_media_download");
  assert.equal(sent.length,1);
  assert.equal(sent[0].formatId,"f1080");
  assert.equal(sent[0].queueOnly,false);
  assert.equal(sent[0].rightsConfirmed,true);
  assert.equal(sent[0].saveLocation,"default");
  assert.equal(modal.hidden,true,"Accepted Start should minimize the full confirmation");
  const pill=ui.elements.find(x=>x.className==="job-pill");
  assert.equal(pill.hidden,false,"A compact progress panel remains visible");
  assert.match(ui.elements.find(x=>x.className==="job-pill-toggle").textContent,/Download queued/);
  start.listeners.click();await new Promise(r=>setTimeout(r,2));
  assert.equal(messages.filter(m=>m.action==="rangedesk_media_download").length,1);
});

test("Cancel closes modal without starting or queueing any download", async()=>{
  const messages=[];
  const ui=mockPlayerOverlay(async message=>{
    messages.push(message);
    if(message.action==="rangedesk_media_probe")
      return {accepted:true,formats:[{id:"720",label:"720p",height:720}]};
    return {accepted:true};
  });
  ui.toggle.listeners.click();await new Promise(r=>setTimeout(r,5));
  ui.options.children.find(x=>x.className.startsWith("choice quality")).listeners.click();
  await new Promise(r=>setTimeout(r,2));
  const modal=ui.elements.find(x=>x.className==="confirm-mask");
  assert.equal(modal.hidden,false);
  ui.elements.find(x=>x.className==="confirm-btn"&&x.textContent==="Cancel").listeners.click();
  assert.equal(modal.hidden,true);
  assert.equal(messages.filter(m=>m.action==="rangedesk_media_download").length,0);
});

test("Download Later creates a queue-only request and no started claim", async()=>{
  const messages=[],jobId="0123456789abcdef0123456789abcdef";
  const ui=mockPlayerOverlay(async message=>{
    messages.push(message);
    if(message.action==="rangedesk_media_probe")
      return {accepted:true,formats:[{id:"720",label:"720p",height:720}]};
    return {accepted:true,jobId};
  });
  ui.toggle.listeners.click();await new Promise(r=>setTimeout(r,5));
  ui.options.children.find(x=>x.className.startsWith("choice quality")).listeners.click();
  await new Promise(r=>setTimeout(r,2));
  const permission=ui.elements.find(x=>x.className==="confirm-rights" &&
    x.children.some(y=>y.textContent==="I have permission to save this video")).children[0];
  permission.checked=true;
  ui.elements.find(x=>x.textContent==="Download Later").listeners.click();
  await new Promise(r=>setTimeout(r,3));
  const sent=messages.filter(m=>m.action==="rangedesk_media_download");
  assert.equal(sent.length,1);
  assert.equal(sent[0].queueOnly,true);
  const note=ui.elements.find(x=>x.className==="confirm-note"&&x.attributes.role==="status");
  assert.match(note.textContent,/Saved for later/);
  assert.equal(ui.elements.find(x=>x.className==="confirm-mask").hidden,true);
  assert.equal(ui.elements.find(x=>x.className==="job-pill").hidden,false);
  assert.match(ui.elements.find(x=>x.className==="job-pill-toggle").textContent,/Saved for later/);
});


test("Compact progress panel polls while full confirmation and menu are closed", async()=>{
  const jobId="0123456789abcdef0123456789abcdef", actions=[];
  const ui=mockPlayerOverlay(async message=>{
    actions.push(message.action);
    if(message.action==="rangedesk_media_probe")
      return {accepted:true,formats:[{id:"720",label:"720p · MP4",height:720}]};
    if(message.action==="rangedesk_media_download")return {accepted:true,jobId};
    if(message.action==="rangedesk_media_status")return {
      accepted:true,jobId,state:"downloading",message:"Receiving media",downloadedBytes:512000,totalBytes:1024000
    };
    return {accepted:false};
  },{fastPoll:true});
  ui.toggle.listeners.click();await new Promise(r=>setTimeout(r,5));
  ui.options.children.find(x=>x.className.startsWith("choice quality")).listeners.click();
  await new Promise(r=>setTimeout(r,2));
  const permission=ui.elements.find(x=>x.className==="confirm-rights" &&
    x.children.some(y=>y.textContent==="I have permission to save this video")).children[0];
  permission.checked=true;
  ui.elements.find(x=>x.className==="confirm-btn primary").listeners.click();
  await new Promise(r=>setTimeout(r,35));
  const modal=ui.elements.find(x=>x.className==="confirm-mask");
  const menu=ui.elements.find(x=>x.className==="menu");
  const pill=ui.elements.find(x=>x.className==="job-pill");
  const title=ui.elements.find(x=>x.className==="job-pill-toggle");
  const detail=ui.elements.find(x=>x.className==="job-pill-detail");
  assert.equal(modal.hidden,true);
  assert.equal(menu.hidden,true);
  assert.equal(pill.hidden,false);
  assert.ok(actions.includes("rangedesk_media_status"),"Closed menu must not stop polling while pill is visible");
  assert.match(title.textContent,/Downloading/);
  assert.equal(detail.hidden,true,"Progress starts minimized");
  title.listeners.click();
  assert.equal(detail.hidden,false,"Status can be expanded without reopening the full form");
  assert.match(detail.textContent,/Receiving media/);
  ui.elements.find(x=>x.className==="job-pill-close").listeners.click();
  assert.equal(pill.hidden,true,"User may hide the pill without cancelling the download");
  assert.equal(actions.filter(x=>x==="rangedesk_media_download").length,1);
});

test("Rejected Start leaves full confirmation visible and never shows a queued pill",async()=>{
  const actions=[];
  const ui=mockPlayerOverlay(async message=>{
    actions.push(message.action);
    if(message.action==="rangedesk_media_probe")
      return {accepted:true,formats:[{id:"720",label:"720p",height:720}]};
    return {accepted:false,errorCode:"NATIVE_REQUEST_FAILED",message:"Could not queue media."};
  });
  ui.toggle.listeners.click();await new Promise(r=>setTimeout(r,5));
  ui.options.children.find(x=>x.className.startsWith("choice quality")).listeners.click();
  await new Promise(r=>setTimeout(r,2));
  const permission=ui.elements.find(x=>x.className==="confirm-rights" &&
    x.children.some(y=>y.textContent==="I have permission to save this video")).children[0];
  permission.checked=true;
  ui.elements.find(x=>x.className==="confirm-btn primary").listeners.click();
  await new Promise(r=>setTimeout(r,3));
  assert.equal(ui.elements.find(x=>x.className==="confirm-mask").hidden,false);
  assert.equal(ui.elements.find(x=>x.className==="job-pill").hidden,true);
  assert.match(ui.elements.find(x=>x.className==="confirm-note"&&x.attributes.role==="status").textContent,/Could not queue/);
  assert.equal(actions.filter(x=>x==="rangedesk_media_download").length,1);
});

test("Missing video thumbnail lets form occupy full confirmation width",()=>{
  const ui=mockPlayerOverlay(async()=>({accepted:false}));
  const body=ui.elements.find(x=>x.className==="confirm-body");
  const thumbnail=ui.elements.find(x=>x.className==="confirm-thumb");
  const detail=ui.elements.find(x=>x.className==="confirm-detail");
  const stylesheet=ui.elements.find(x=>x.tagName==="STYLE").textContent;
  assert.ok(body.children.includes(thumbnail));
  assert.equal(body.children[1],detail);
  assert.ok(stylesheet.includes(".confirm-thumb[hidden]+.confirm-detail{grid-column:1/-1;}"));
  assert.ok(stylesheet.includes("grid-template-columns:84px minmax(0,1fr)"));
});

test("Old video choices cannot be submitted after navigating away", async()=>{
  const messages=[];
  const ui=mockPlayerOverlay(async message=>{
    messages.push(message);
    if(message.action==="rangedesk_media_probe")
      return {accepted:true,formats:[{id:"720",label:"720p",height:720}]};
    return {accepted:true};
  });
  ui.toggle.listeners.click();await new Promise(r=>setTimeout(r,5));
  ui.options.children.find(x=>x.className.startsWith("choice quality")).listeners.click();
  await new Promise(r=>setTimeout(r,2));
  const permission=ui.elements.find(x=>x.className==="confirm-rights" &&
    x.children.some(y=>y.textContent==="I have permission to save this video")).children[0];
  permission.checked=true;
  ui.document.URL="https://www.youtube.com/watch?v=bbbbbbbbbbb";
  ui.elements.find(x=>x.className==="confirm-btn primary").listeners.click();
  await new Promise(r=>setTimeout(r,2));
  assert.equal(messages.filter(m=>m.action==="rangedesk_media_download").length,0);
});
