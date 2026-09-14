#define MyAppName "Toaster"
#define MyAppVersion "0.2.0"
#define MyAppPublisher "Chris Babcock"
#define MyAppExeName "Toaster.exe"

[Setup]
AppId={{85FBCC7E-D39D-49F0-926F-554833E66404}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Toaster
DefaultGroupName=Toaster
UninstallDisplayIcon={app}\Tray\Toaster.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=Toaster-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "..\artifacts\publish\Toaster.Service\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\publish\Toaster.Cli\*"; DestDir: "{app}\Cli"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\publish\Toaster.Tray\*"; DestDir: "{app}\Tray"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\redist\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "..\scripts\install-service.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\uninstall-service.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion

[Icons]
Name: "{group}\Toaster"; Filename: "{app}\Tray\Toaster.exe"
Name: "{group}\Toaster CLI"; Filename: "{cmd}"; Parameters: "/k \"{app}\Cli\toaster.exe status\""

[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Toaster"; ValueData: "\"{app}\Tray\Toaster.exe\""; Flags: uninsdeletevalue

[Run]
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; Flags: runhidden waituntilterminated
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File \"{app}\scripts\install-service.ps1\" -InstallDir \"{app}\""; Flags: runhidden waituntilterminated
Filename: "{app}\Tray\Toaster.exe"; Description: "Open Toaster"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File \"{app}\scripts\uninstall-service.ps1\""; Flags: runhidden waituntilterminated
