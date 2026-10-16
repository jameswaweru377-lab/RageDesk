// No browser, user data, network, or native host required. Both browser
// bundles are exercised with mocked WebExtension APIs on the CI runner.
const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");

for (const browserFamily of ["chromium", "firefox"]) {
  function build(reply = { accepted: true }) {
    const handlers = {};
    const actions = [];
    let enabled = false;
    const event = name => ({ addListener: fn => { handlers[name] = fn; } });
    const api = {
      runtime: {
        id: "rangedesk-extension",
        onInstalled: event("installed"),
        onStartup: event("startup"),
        sendNativeMessage: async (host, message) => {
          actions.push(["native", host, message.action, message.url]);
          if (reply instanceof Error) throw reply;
          return reply;
        }
      },
      contextMenus: {
        create: () => {},
        onClicked: event("menu")
      },
      action: {
        onClicked: event("toggle"),
        setBadgeText: async () => {},
        setBadgeBackgroundColor: async () => {},
        setTitle: async () => {}
      },
      storage: {
        local: {
          get: async () => ({ autoCapture: enabled }),
          set: async settings => { enabled = settings.autoCapture; }
        }
      },
      downloads: {
        onCreated: event("created"),
        pause: async id => { actions.push(["pause", id]); },
        resume: async id => { actions.push(["resume", id]); },
        cancel: async id => { actions.push(["cancel", id]); }
      }
    };
    const source = fs.readFileSync(path.join(__dirname, browserFamily, "background.js"), "utf8");
    vm.runInNewContext(source, {
      ...(browserFamily === "chromium" ? { chrome: api } : { browser: api }),
      URL, Number, Set, console
    }, { filename: browserFamily + "/background.js" });
    const deliver = async (item = {
      id: 42, state: "in_progress", url: "https://downloads.example.test/setup.zip"
    }) => {
      handlers.created(item);
      // Listeners dispatch async work without blocking the extension runtime.
      for (let i = 0; i < 30; i++) await new Promise(resolve => setTimeout(resolve, 2));
    };
    return { handlers, actions, deliver };
  }

  test(browserFamily + ": auto capture is OFF until user clicks toolbar", async () => {
    const ext = build();
    await ext.deliver();
    assert.equal(ext.actions.length, 0);
  });

  test(browserFamily + ": ACK pauses then cancels only after native GUI acceptance", async () => {
    const ext = build();
    await ext.handlers.toggle();
    await ext.deliver();
    assert.deepEqual(ext.actions.map(a => a[0]), ["pause", "native", "cancel"]);
    assert.equal(ext.actions[1][2], "capture_download");
  });

  test(browserFamily + ": rejected handoff resumes original browser download", async () => {
    const ext = build({ accepted: false });
    await ext.handlers.toggle();
    await ext.deliver();
    assert.deepEqual(ext.actions.map(a => a[0]), ["pause", "native", "resume"]);
  });

  test(browserFamily + ": absent native host leaves browser download intact", async () => {
    const ext = build(new Error("Native host not configured"));
    await ext.handlers.toggle();
    await ext.deliver();
    assert.deepEqual(ext.actions.map(a => a[0]), ["pause", "native", "resume"]);
  });

  test(browserFamily + ": ineligible or authenticated URLs are not intercepted", async () => {
    const ext = build();
    await ext.handlers.toggle();
    await ext.deliver({ id: 1, url: "https://website.example.test/account", state: "in_progress" });
    await ext.deliver({ id: 2, url: "https://u:p@example.test/file.zip", state: "in_progress" });
    await ext.deliver({ id: 3, url: "blob:https://example.test/xyz", state: "in_progress" });
    assert.equal(ext.actions.length, 0);
  });

  test(browserFamily + ": toolbar can disable previously enabled capture", async () => {
    const ext = build();
    await ext.handlers.toggle();
    await ext.handlers.toggle();
    await ext.deliver();
    assert.equal(ext.actions.length, 0);
  });

  test(browserFamily + ": manual right-click remains available", async () => {
    const ext = build();
    await ext.handlers.menu({ menuItemId: "rangedesk-link", linkUrl: "https://example.test/file.zip" });
    assert.equal(ext.actions[0][0], "native");
    assert.equal(ext.actions[0][2], "add_link");
  });
}
