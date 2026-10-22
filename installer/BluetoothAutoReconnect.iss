; Inno Setup script for Bluetooth Auto Reconnect.
; Build with build-release.ps1 (publishes the app first), or:
;   ISCC.exe /DAppVersion=1.0.0 installer\BluetoothAutoReconnect.iss
;
; Installs per user (no admin prompt) and starts the agent at sign-in through the HKCU Run key.
; Deliberately not a Windows Service: services run in session 0, which has no tray and where
; WinRT Bluetooth pairing fails.

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif

#define AppName "Bluetooth Auto Reconnect"
#define AppExe "BluetoothAutoReconnect.exe"
#define PublishDir "..\artifacts\publish"

[Setup]
AppId={{6F2A9C3E-8B1D-4E57-A0C4-3D9E21B7F5A8}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts\installer
OutputBaseFilename=BluetoothAutoReconnect-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Updating over a running agent: close it, replace the files, start it again from [Run].
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[CustomMessages]
english.AutoStart=Start automatically when I sign in to Windows
brazilianportuguese.AutoStart=Iniciar automaticamente quando eu entrar no Windows
english.LaunchNow=Start {#AppName} now
brazilianportuguese.LaunchNow=Iniciar o {#AppName} agora

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "BluetoothAutoReconnect"; ValueData: """{app}\{#AppExe}"""; \
  Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent
; In-app updates run the installer with /SILENT: start the new version when it finishes.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "StopAgent"
