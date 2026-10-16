// Chrome MV3 HLS discovery contracts; all requests, tabs and permission
// decisions are synthetic. Never contacts a live website or native host.
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
const scripts = name => fs.readFileSync(path.join(__dirname, "chromium", name), "utf8");

function pure() {
  const scope = vm.createContext({ URL, Number, Array, Set, Date });
  vm.runInContext(scripts("media-discovery.js"), scope);
  return scope.RangeDeskMediaDiscovery;
}

const PAGE = "https://videos.example.test/watch";
const MANIFEST = "https://media.example.test/movie/master.m3u8?token=private";
const DETAILS = {
  tabId: 11, frameId: 0, statusCode: 200, type: "xmlhttprequest",
  initiator: "https://videos.example.test", url: MANIFEST
};

test("HLS discovery is strictly direct, HTTP(S) and limited to known playlists", () => {
  const p = pure();
  assert.equal(p.eligibleHls(MANIFEST), true);
  for (const url of [
    "blob:https://videos.example.test/some-id", "data:application/vnd.apple.mpegurl,hi",
    "file:///etc/passwd", "https://u:p@media.test/s.m3u8",
    "https://media.test/movie.mpd", "https://media.test/master.m3u8#hidden"
  ]) assert.equal(p.eligibleHls(url), false, url);
});

test("unwatched tab, wrong site, other frame, errors or expired window cannot leak media", () => {
  const p = pure(); const now = 1000; const state = p.beginWatch(PAGE, now);
  assert.equal(p.observe(DETAILS, null, now), null);
  assert.equal(p.observe({ ...DETAILS, initiator: "https://other.test" }, state, now), null);
  assert.equal(p.observe({ ...DETAILS, statusCode: 403 }, state, now), null);
  assert.equal(p.observe({ ...DETAILS, type: "main_frame" }, state, now), null);
  assert.equal(p.observe(DETAILS, state, state.expiresAt + 1), null);
  const recorded = p.observe(DETAILS, state, now);
  assert.ok(recorded);
  assert.equal(p.listing(recorded, PAGE, 1, now).sources.length, 0);
  assert.equal(p.listing(recorded, "https://other.test/watch", 0, now).enabled, false);
  assert.equal(p.listing(recorded, PAGE, 0, now).sources.length, 1);
});

test("session entries are bounded, deduplicated and expire", () => {
  const p = pure(); let state = p.beginWatch(PAGE, 100);
  for (let i = 0; i < 20; i++) {
    state = p.observe({ ...DETAILS, url: "https://media.test/" + i + ".m3u8" }, state, 200);
  }
  assert.equal(state.sources.length, 8);
  const again = p.observe({ ...DETAILS, url: state.sources[0].url }, state, 300);
  assert.equal(again.sources.length, 8);
  assert.equal(p.listing(again, PAGE, 0, 300).sources.length, 8);
  assert.equal(p.listing(again, PAGE, 0, state.expiresAt + 1).enabled, false);
});

function mockBrowser(allow = true) {
  const events = {}, sent = [], writes = [];
  const session = {};
  const event = name => ({ addListener: listener => { events[name] = listener; } });
  const tab = { id: 11, url: PAGE };
  const api = {
    runtime: {
      id: "my-extension", onInstalled: event("installed"), onStartup: event("startup"),
      onMessage: event("message"), sendNativeMessage: async () => ({ accepted: true })
    },
    action: {
      onClicked: event("toggle"), setBadgeText: async () => {},
      setBadgeBackgroundColor: async () => {}, setTitle: async () => {}
    },
    contextMenus: {
      create: entry => { sent.push(["menu", entry.id]); },
      onClicked: event("context")
    },
    downloads: { onCreated: event("created") },
    storage: {
      local: { get: async () => ({}), set: async () => {} },
      session: {
        get: async key => ({ [key]: session[key] }),
        set: async object => Object.assign(session, object)
      }
    },
    permissions: {
      request: async grant => { writes.push(["permission", grant.origins]); return allow; },
      remove: async grant => { writes.push(["remove", grant.origins]); return true; }
    },
    tabs: {
      query: async () => [tab],
      sendMessage: async (id, msg, options) => {
        sent.push(["tab-message", id, msg.action, options?.frameId]);
      },
      onRemoved: event("tab-removed"),
      onUpdated: event("tab-updated")
    },
    webRequest: { onCompleted: event("completed") }
  };
  const context = vm.createContext({ chrome: api, URL, Date, Number, Set, Array,
    console, importScripts: () => {} });
  vm.runInContext(scripts("media-discovery.js") + "\n" + scripts("background.js"), context);
  const pause = () => new Promise(resolve => setTimeout(resolve, 15));
  const select = () => events.context({ menuItemId: "rangedesk-watch-hls" }, tab);
  const stop = () => events.context({ menuItemId: "rangedesk-stop-hls" }, tab);
  async function list(page = PAGE, frameId = 0) {
    let reply;
    const open = events.message({ action: "rangedesk_media_list" },
      { id: api.runtime.id, tab, url: page, frameId }, r => { reply = r; });
    assert.equal(open, true);
    await pause();
    assert.ok(reply);
    return reply;
  }
  return { events, api, session, sent, writes, tab, pause, select, stop, list };
}

test("Chrome requires a direct user opt-in before recording cross-origin HLS URLs", async () => {
  const b = mockBrowser();
  b.events.installed();
  assert.ok(b.sent.some(x => x[1] === "rangedesk-watch-hls"));
  assert.equal((await b.list()).enabled, false);
  b.events.completed(DETAILS);
  await b.pause();
  assert.equal((await b.list()).sources.length, 0);
  await b.select();
  assert.deepEqual(b.writes[0][0], "permission");
  assert.deepEqual(Array.from(b.writes[0][1]), ["<all_urls>"]);
  b.events.completed(DETAILS);
  await b.pause();
  const list = await b.list();
  assert.equal(list.enabled, true);
  assert.equal(list.sources.length, 1);
  assert.equal(list.sources[0].url, MANIFEST);
  assert.equal(list.sources[0].format, "HLS");
  assert.ok(b.sent.some(x => x[0] === "tab-message" && x[3] === 0));
  const saved = JSON.stringify(b.session);
  assert.ok(!saved.includes("Cookie") && !saved.includes("Authorization"));
  assert.ok(!saved.includes("https://unrelated.test"));
});

test("Chrome permission rejection leaves media detection off", async () => {
  const b = mockBrowser(false);
  await b.select();
  b.events.completed(DETAILS);
  await b.pause();
  assert.equal((await b.list()).enabled, false);
  assert.equal(b.writes.length, 1);
});

test("Chrome ignores media from unrelated tabs and unrelated page initiators", async () => {
  const b = mockBrowser();
  await b.select();
  b.events.completed({ ...DETAILS, tabId: 12 });
  b.events.completed({ ...DETAILS, initiator: "https://different.test" });
  b.events.completed({ ...DETAILS, url: "https://media.test/movie.mpd" });
  await b.pause();
  assert.equal((await b.list()).sources.length, 0);
});

test("closing tab removes recorded sources; navigation to another origin disables watch", async () => {
  const b = mockBrowser();
  await b.select();
  b.events.completed(DETAILS); await b.pause();
  b.events["tab-updated"](11, { url: "https://other.test/" });
  await b.pause();
  assert.equal((await b.list()).enabled, false);
  await b.select();
  b.events.completed(DETAILS); await b.pause();
  b.events["tab-removed"](11);
  await b.pause();
  assert.equal((await b.list()).enabled, false);
});

test("Stop explicitly disables media observation while retaining normal file capture", async () => {
  const b = mockBrowser();
  await b.select();
  b.events.completed(DETAILS); await b.pause();
  assert.equal((await b.list()).sources.length, 1);
  await b.stop();
  assert.equal((await b.list()).enabled, false);
  b.events.completed(DETAILS); await b.pause();
  assert.equal((await b.list()).sources.length, 0);
});
