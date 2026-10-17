# RangeDesk — Windows EXE installer (0.8.0 Preview 1)

The standard Windows setup wizard is built with **Inno Setup 6**. It
packages the self-contained Windows x64 application, native browser host,
optional scheduler helper and unpacked Chrome extension.

## User flow

1. Select **English** or **Kiswahili**.
2. Read and accept the English or Swahili preview EULA.
3. Choose **current user** (default, no admin; LocalAppData/Programs/RangeDesk)
   or **all users** (UAC confirmation; Program Files/RangeDesk).
4. Choose a different folder if desired, and optionally a desktop shortcut.
5. Install; the Start Menu link and Windows Installed Apps uninstaller
   are registered automatically.
6. Optionally launch the app after successful installation.

Kiswahili translations cover the main wizard screens and the agreement.
Uncommon operating-system/installer error messages fall back to English.

## Safety and upgrades

The AppId deliberately matches prior RangeDesk previews, allowing upgrades
without creating unrelated installed-app entries. Local download history,
preferences, queue, saved browser state, and partial checkpoints live under
%LOCALAPPDATA%/RangeDesk and are **never** uninstalled. Downloads in user
folders are likewise left alone. No browser extension is auto-enabled.

Setup is currently **unsigned**. SmartScreen may warn. Do not override
device management or Windows security restrictions.

## Build

Publish all three .NET projects as self-contained win-x64 and package
BrowserCapture and Scheduler assets under artifact/RangeDesk. Then run:

    & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "installer\RangeDesk.iss" "/DSourceDir=<absolute-path-to-artifact\RangeDesk>"

Output:

    artifact/RangeDesk-Setup-0.8.0-preview.1-win-x64.exe

The existing Windows CI workflow runs installation, registration, upgrade,
uninstall and data-preservation smoke tests on the release feature branch.
After an explicit [publish-preview] merge commit, the separate release
workflow downloads the ZIP from successful **main** CI, builds and smoke-tests
the EXE, then publishes the installer, portable ZIP and SHA-256 hashes into a
GitHub **prerelease**. No release is created from unverified feature code.
