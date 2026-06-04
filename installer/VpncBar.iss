; VpncBar Windows installer (Inno Setup 6).
; Build:  iscc installer\VpncBar.iss   (after tools\publish.ps1)
; Produces dist\VpncBar-<version>-setup.exe.
;
; Layout installed to {app} (Program Files\VpncBar):
;   VpncBar.exe              self-contained single-file (tray | --service | --script)
;   vpncbar-script.js        openconnect --script relay
;   openconnect\             openconnect.exe + GnuTLS DLL closure + wintun.dll
;   vpnc\                    vpnc.exe + shared DLL closure + wintun.dll
;
; The privileged service is registered post-install via "VpncBar.exe
; --install-service" (demand-start, tray-controlled) and removed on uninstall.

#define AppName "VpncBar"
#define AppVersion "0.1.0"
#define AppPublisher "VpncBar"
#define AppExe "VpncBar.exe"

[Setup]
AppId={{8F1B6E2A-VPNC-BAR0-WIN0-PORTABLE0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\dist
OutputBaseFilename=VpncBar-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; The bundled Wintun driver + service need admin; install per-machine.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "..\dist\app\VpncBar.exe";         DestDir: "{app}";              Flags: ignoreversion
Source: "..\dist\app\vpncbar-script.js";   DestDir: "{app}";              Flags: ignoreversion
Source: "..\dist\app\openconnect\*";       DestDir: "{app}\openconnect";  Flags: ignoreversion recursesubdirs
Source: "..\dist\app\vpnc\*";              DestDir: "{app}\vpnc";         Flags: ignoreversion recursesubdirs
Source: "..\vendor\NOTICE";                DestDir: "{app}";              DestName: "NOTICE.txt"; Flags: ignoreversion
Source: "..\LICENSE";                      DestDir: "{app}";              DestName: "LICENSE.txt"; Flags: ignoreversion isreadme skipifsourcedoesntexist

[Icons]
Name: "{group}\VpncBar";          Filename: "{app}\{#AppExe}"
Name: "{userstartup}\VpncBar";    Filename: "{app}\{#AppExe}";  Tasks: autostart

[Tasks]
Name: autostart; Description: "Start VpncBar automatically at login"; GroupDescription: "Startup:"
Name: launch;    Description: "Launch VpncBar now";                    GroupDescription: "Startup:"

[Run]
; Register the privileged service (demand-start, tray-controlled lifetime).
Filename: "{app}\{#AppExe}"; Parameters: "--install-service"; Flags: runhidden waituntilterminated; StatusMsg: "Registering the VpncBar service..."
; Launch the tray (it starts the service); not elevated, so the tray runs as the user.
Filename: "{app}\{#AppExe}"; Description: "Launch VpncBar"; Flags: nowait postinstall skipifsilent runasoriginaluser; Tasks: launch

[UninstallRun]
; Stop the tray and remove the service before files are deleted.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall-service"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveService"

[Code]
// Make sure the tray isn't running (it would lock the exe and keep the
// service alive) before we remove anything.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
    Exec('taskkill.exe', '/IM VpncBar.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
