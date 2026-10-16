# RangeDesk

Independent Windows download manager built with C# / .NET 10 / WPF. The approved v5 navy desktop preview, HTTP(S) engine, resume/checkpoints, scheduling, download organization and browser capture are maintained in this repository and checked by Windows CI. Source builds are **development previews**, not signed stable releases.

## Chrome first: free development extension

RangeDesk bundles an **opt-in Chrome extension** under `browser-capture/chromium`, included in the Windows preview ZIP as `BrowserCapture/chromium`. **No Chrome Web Store listing, developer payment or PowerShell command is necessary for local unpacked testing.**

1. Extract the entire Windows preview ZIP; launch RangeDesk.
2. Open **Settings → Chrome browser → Open Chrome extensions**. Enable Developer mode in Chrome, select **Load unpacked**, then choose the packaged Chrome folder (use **Open extension folder** in RangeDesk to find it).
3. Back in RangeDesk Settings, press **Connect Chrome** to register the approved native messaging host for your current Windows user.
4. Restart Chrome. Pin the extension, right-click its toolbar icon and select **Check RangeDesk connection**. `OK` confirms Chrome can contact the local host. Click the icon to enable auto-capture (`ON`); it is disabled by default.

Chrome's own **Load unpacked** action is unavoidable on unmanaged Windows without store distribution. RangeDesk does **not** force-install the extension, collect cookies, steal protected media or auto-enable browser monitoring. Only direct supported HTTP(S) file URLs are eligible; unaccepted downloads stay in Chrome. See [full setup, limitations and disconnect instructions](browser-capture/README.md).

The project is independent and does not include IDM proprietary source, licensed binaries or private authentication data. Real Windows 11 browser and extension-installation acceptance is still required before public release.
