# RangeDesk

Independent Windows download manager built with C# / .NET 10 / WPF. The Arctic Glass Windows UI, HTTP(S) engine, validator-safe resume/checkpoints, scheduling, download organization, and optional browser capture are maintained here and checked by Windows CI. Current releases are **unsigned development previews**, not signed stable builds.

## Windows installer (English / Kiswahili)

Download **RangeDesk-Setup-0.8.0-preview.1-win-x64.exe** from [Releases](https://github.com/jameswaweru377-lab/RageDesk/releases). The wizard lets you select English or Kiswahili, accept preview terms, choose an installation directory and install for yourself (no admin) or all users (admin required). It adds a Start Menu shortcut and Windows uninstall entry. The setup does **not** delete existing download history, files or resume checkpoints on upgrade/removal.

The unsigned preview may display a Windows unknown-publisher warning. For locked-down PCs, do not bypass organizational security restrictions. A portable ZIP is also available.

## Chrome first: free development extension

RangeDesk bundles an **opt-in Chrome extension** under `browser-capture/chromium`, included in the Windows preview ZIP as `BrowserCapture/chromium`. **No Chrome Web Store listing, developer payment or PowerShell command is necessary for local unpacked testing.**

1. Extract the entire Windows preview ZIP; launch RangeDesk.
2. Open **Settings → Chrome browser → Open Chrome extensions**. Enable Developer mode in Chrome, select **Load unpacked**, then choose the packaged Chrome folder (use **Open extension folder** in RangeDesk to find it).
3. Back in RangeDesk Settings, press **Connect Chrome** to register the approved native messaging host for your current Windows user.
4. Restart Chrome. Pin the extension, right-click its toolbar icon and select **Check RangeDesk connection**. `OK` confirms Chrome can contact the local host. Click the icon to enable auto-capture (`ON`); it is disabled by default.

Chrome's own **Load unpacked** action is unavoidable on unmanaged Windows without store distribution. RangeDesk does **not** force-install the extension, collect cookies, steal protected media or auto-enable browser monitoring. Only direct supported HTTP(S) file URLs are eligible; unaccepted downloads stay in Chrome. See [full setup, limitations and disconnect instructions](browser-capture/README.md).

The project is independent and does not include IDM proprietary source, licensed binaries or private authentication data. Real Windows 11 browser and extension-installation acceptance is still required before public release.
