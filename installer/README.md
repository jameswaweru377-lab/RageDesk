# RangeDesk Windows installer preview

This per-user x64 installer is independent and currently **unsigned**. Windows SmartScreen may warn until an appropriate publisher code-signing certificate is in place. Do not recommend bypassing managed-device security policy.

## Scope
- Installs the portable RangeDesk WPF application under the current user's `%LOCALAPPDATA%\Programs\RangeDesk`.
- Creates a Start menu shortcut and optionally a desktop shortcut.
- Includes the opt-in browser native host and browser-extension setup files with the application.
- Supports normal Windows uninstall. **Uninstall does not erase user downloads, the catalog, queued schedules, browser settings or any partial-download checkpoints** under `%LOCALAPPDATA%\RangeDesk` or the chosen download folder.
- Upgrade tests must verify an existing `catalog-v5.json`, `preferences-v5.json`, queued schedule file and download partials still exist after installing a newer version. Normal existing installations should be closed during update.
- Does not register a browser extension, run code-signing, or silently intercept browser traffic.

## Build
The `installer/RangeDesk.iss` file is an Inno Setup 6 script. After publishing the self-contained `win-x64` application and native messaging host to `artifact/RangeDesk`, compile with:

```powershell
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' 'installer\RangeDesk.iss' '/DSourceDir=<full-path-to-artifact\RangeDesk>'
```

The preview installer appears beside the portable folder as `RangeDesk-Setup-Preview.exe`.

Do **not** publish a stable public release until the app, browser permissions, installer, upgrades and data-preservation checks pass on a real Windows 11 machine. Sign the final distribution appropriately.
