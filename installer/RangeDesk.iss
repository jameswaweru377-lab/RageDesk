; RangeDesk 0.8.0 Preview 3 — English / Kiswahili installer.
; Compatible with the existing RangeDesk installer AppId. All downloader
; settings, browser opt-ins, jobs and partial checkpoints stay outside {app}.
#ifndef SourceDir
  #define SourceDir "..\artifact\RangeDesk"
#endif
#define MyAppName "RangeDesk"
#define MyAppVersion "0.8.0-preview.3"
#define MyVersionNumbers "0.8.0.3"
[Setup]
AppId={{F7B609F5-0C3F-4E1E-8D9C-70214C371CF4}
AppName={#MyAppName}
AppVersion={#MyVersionNumbers}
AppVerName={#MyAppName} 0.8.0 Preview 3
AppPublisher=RangeDesk Independent Project
AppPublisherURL=https://github.com/jameswaweru377-lab/RageDesk
AppSupportURL=https://github.com/jameswaweru377-lab/RageDesk/issues
AppUpdatesURL=https://github.com/jameswaweru377-lab/RageDesk/releases
DefaultDirName={autopf}\RangeDesk
DefaultGroupName=RangeDesk
UninstallDisplayName=RangeDesk 0.8.0 Preview 3
UninstallDisplayIcon={app}\RangeDesk.exe
OutputDir={#SourceDir}\..
OutputBaseFilename=RangeDesk-Setup-{#MyAppVersion}-win-x64
VersionInfoVersion={#MyVersionNumbers}
VersionInfoDescription=RangeDesk Windows installer (unsigned preview)
VersionInfoProductName=RangeDesk
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=yes
UsePreviousAppDir=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0
WizardStyle=modern
ShowLanguageDialog=yes
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no
; Do NOT delete %LOCALAPPDATA%\RangeDesk or user-selected download folders.

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"; LicenseFile: "licenses\License-en.txt"
Name: "sw"; MessagesFile: "compiler:Default.isl"; LicenseFile: "licenses\License-sw.txt"

; The default Inno English messages serve as a safe fallback for less-common
; errors. User-facing wizard pages are localized below for Kiswahili.
[LangOptions]
sw.LanguageName=Kiswahili
sw.LanguageID=$0441

[Messages]
sw.SetupAppTitle=Usakinishaji
sw.SelectLanguageTitle=Chagua Lugha
sw.SelectLanguageLabel=Chagua lugha utakayotumia wakati wa kusakinisha programu.
sw.PrivilegesRequiredOverrideTitle=Chagua namna ya kusakinisha
sw.PrivilegesRequiredOverrideInstruction=Chagua aina ya usakinishaji
sw.PrivilegesRequiredOverrideText2=%1 inaweza kusakinishwa kwa akaunti yako pekee au kwa watumiaji wote (ruhusa ya msimamizi inahitajika).
sw.PrivilegesRequiredOverrideCurrentUser=Kwa akaunti &yangu pekee
sw.PrivilegesRequiredOverrideCurrentUserRecommended=Kwa akaunti &yangu pekee (inapendekezwa)
sw.PrivilegesRequiredOverrideAllUsers=Kwa &watumiaji wote
sw.PrivilegesRequiredOverrideAllUsersRecommended=Kwa &watumiaji wote (inapendekezwa)
sw.ButtonBack=< &Nyuma
sw.ButtonNext=&Endelea >
sw.ButtonInstall=&Sakinisha
sw.ButtonFinish=&Maliza
sw.ButtonCancel=Ghairi
sw.ButtonBrowse=&Vinjari...
sw.ButtonWizardBrowse=&Vinjari...
sw.ClickNext=Bofya Endelea ili kuendelea, au Ghairi ili kutoka.
sw.WelcomeLabel1=Karibu kwenye kisakinishi cha [name]
sw.WelcomeLabel2=Kisakinishi hiki kitaweka [name/ver] kwenye kompyuta yako.%n%nTunapendekeza ufunge programu nyingine kabla ya kuendelea.
sw.WizardLicense=Masharti ya Matumizi
sw.LicenseLabel=Tafadhali soma taarifa hizi muhimu kabla ya kuendelea.
sw.LicenseLabel3=Soma masharti yafuatayo. Unapaswa kuyakubali kabla ya kuendelea na usakinishaji.
sw.LicenseAccepted=&Ninakubali masharti
sw.LicenseNotAccepted=&Sikubali masharti
sw.WizardSelectDir=Chagua Mahali pa Kusakinisha
sw.SelectDirDesc=Unataka [name] isakinishwe wapi?
sw.SelectDirLabel3=Programu ya [name] itasakinishwa kwenye folda ifuatayo.
sw.SelectDirBrowseLabel=Bofya Endelea, au Vinjari ili kuchagua folda nyingine.
sw.WizardSelectTasks=Chagua Mipangilio ya Ziada
sw.SelectTasksDesc=Ni hatua gani za ziada unazotaka?
sw.SelectTasksLabel2=Chagua hatua za ziada za kusakinisha [name], kisha bofya Endelea.
sw.WizardReady=Tayari Kusakinisha
sw.ReadyLabel1=Kisakinishi kiko tayari kuweka [name] kwenye kompyuta yako.
sw.ReadyLabel2a=Bofya Sakinisha ili kuendelea, au Nyuma kubadilisha mipangilio.
sw.ReadyLabel2b=Bofya Sakinisha ili kuendelea.
sw.ReadyMemoDir=Mahali pa kusakinisha:
sw.ReadyMemoGroup=Folda ya menyu ya Anza:
sw.ReadyMemoTasks=Hatua za ziada:
sw.WizardPreparing=Kuandaa Usakinishaji
sw.WizardInstalling=Inasakinisha
sw.InstallingLabel=Tafadhali subiri wakati [name] inasakinishwa.
sw.FinishedHeadingLabel=Kukamilisha usakinishaji wa [name]
sw.FinishedLabelNoIcons=Usakinishaji wa [name] umekamilika.
sw.FinishedLabel=Usakinishaji wa [name] umekamilika. Unaweza kufungua programu kupitia njia zake za mkato.
sw.ClickFinish=Bofya Maliza ili kufunga kisakinishi.
sw.StatusCreateDirs=Inatengeneza folda...
sw.StatusExtractFiles=Inatoa faili...
sw.StatusCreateIcons=Inatengeneza njia za mkato...
sw.StatusSavingUninstall=Inahifadhi taarifa za kuondoa programu...
sw.StatusRunProgram=Inakamilisha usakinishaji...

[CustomMessages]
en.DesktopShortcut=Create a desktop shortcut
sw.DesktopShortcut=Tengeneza njia ya mkato kwenye eneo-kazi
en.ShortcutsGroup=Additional shortcuts
sw.ShortcutsGroup=Njia za mkato za ziada
en.LaunchProgram=Launch %1
sw.LaunchProgram=Fungua %1

[Tasks]
Name: desktopicon; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:ShortcutsGroup}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\RangeDesk"; Filename: "{app}\RangeDesk.exe"; WorkingDir: "{app}"
; Optional React view: WPF still owns engine, settings and native popup.
Name: "{group}\RageDesk - New UI Preview"; Filename: "{app}\RangeDesk.exe"; Parameters: "--react-preview"; WorkingDir: "{app}"
Name: "{group}\Uninstall RangeDesk"; Filename: "{uninstallexe}"
Name: "{autodesktop}\RangeDesk"; Filename: "{app}\RangeDesk.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\RangeDesk.exe"; Parameters: "--react-preview"; Description: "{cm:LaunchProgram,RageDesk New UI Preview}"; Flags: nowait postinstall skipifsilent
