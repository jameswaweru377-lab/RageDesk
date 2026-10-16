; RangeDesk independent Windows x64 per-user installer (UNSIGNED PREVIEW).
; Saved download catalogs, segment journals and browser handoffs live under
; %LOCALAPPDATA%\RangeDesk, outside the install directory. Uninstall preserves them.
#ifndef SourceDir
  #define SourceDir "..\artifact\RangeDesk"
#endif
#define MyAppName "RangeDesk"
#define MyAppVersion "0.5.0-preview"
[Setup]
AppId={{F7B609F5-0C3F-4E1E-8D9C-70214C371CF4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=RangeDesk Independent Project
DefaultDirName={localappdata}\Programs\RangeDesk
DefaultGroupName=RangeDesk
UninstallDisplayIcon={app}\RangeDesk.exe
OutputDir={#SourceDir}\..
OutputBaseFilename=RangeDesk-Setup-Preview
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes
ChangesAssociations=no
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts"; Flags: unchecked
[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{userprograms}\RangeDesk"; Filename: "{app}\RangeDesk.exe"
Name: "{userdesktop}\RangeDesk"; Filename: "{app}\RangeDesk.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\RangeDesk.exe"; Description: "Launch RangeDesk"; Flags: nowait postinstall skipifsilent
