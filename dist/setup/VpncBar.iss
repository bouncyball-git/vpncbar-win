; VpncBar Windows installer (Inno Setup 6).
; Build:  iscc dist\setup\VpncBar.iss   (after publish.ps1)
; Produces dist\VpncBar-<version>-setup.exe.
;
; Layout installed to {app} (Program Files\VpncBar):
;   VpncBar.exe              self-contained single-file (tray | --service | --script)
;   vpncbar-script.js        openconnect --script relay
;   backend\                 openconnect.exe + vpnc.exe + shared DLL closure + wintun.dll
;
; The privileged service is registered post-install via "VpncBar.exe
; --install-service" (demand-start, tray-controlled) and removed on uninstall.

#define AppName "VpncBar"
#define AppVersion "1.0.1"
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
OutputDir=.
OutputBaseFilename=VpncBar-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; The bundled Wintun driver + service need admin; install per-machine.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "..\app\VpncBar.exe";         DestDir: "{app}";          Flags: ignoreversion
Source: "..\app\vpncbar-script.js";   DestDir: "{app}";          Flags: ignoreversion
Source: "..\backend\*";               DestDir: "{app}\backend";  Flags: ignoreversion recursesubdirs
Source: "..\..\vendor\NOTICE";                DestDir: "{app}";              DestName: "NOTICE.txt"; Flags: ignoreversion
Source: "..\..\LICENSE";                      DestDir: "{app}";              DestName: "LICENSE.txt"; Flags: ignoreversion isreadme skipifsourcedoesntexist

[Icons]
Name: "{group}\VpncBar";          Filename: "{app}\{#AppExe}"

[Tasks]
Name: autostart; Description: "Start VpncBar automatically at login"; GroupDescription: "Startup:"
Name: launch;    Description: "Launch VpncBar now";                    GroupDescription: "Startup:"

[Run]
; Register the privileged service (demand-start, tray-controlled lifetime).
Filename: "{app}\{#AppExe}"; Parameters: "--install-service"; Flags: runhidden waituntilterminated; StatusMsg: "Registering the VpncBar service..."
; Enable start-at-login by running the app AS THE ORIGINAL USER, so it writes
; the HKCU Run key for the right account (not the elevated admin's). Avoids the
; per-user-area-in-admin-install pitfall of a {userstartup} shortcut.
Filename: "{app}\{#AppExe}"; Parameters: "--enable-autostart"; Flags: runhidden runasoriginaluser; Tasks: autostart
; Launch the tray (it starts the service); not elevated, so the tray runs as the user.
Filename: "{app}\{#AppExe}"; Description: "Launch VpncBar"; Flags: nowait postinstall skipifsilent runasoriginaluser; Tasks: launch

[UninstallRun]
; Stop the tray and remove the service before files are deleted.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall-service"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveService"

[Code]
// VpncBar is framework-dependent: it needs the .NET 10 Desktop Runtime, which
// is NOT in-box. Detect it by looking for a 10.x folder under the shared
// WindowsDesktop runtime directory.
function IsDotNet10DesktopInstalled(): Boolean;
var
  FindRec: TFindRec;
  Base: String;
begin
  Result := False;
  Base := ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if FindFirst(Base + '\10.*', FindRec) then
  try
    repeat
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        Result := True;
        Break;
      end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

// Warn (and offer the download) before installing if the runtime is missing.
function InitializeSetup(): Boolean;
var ErrorCode: Integer;
begin
  Result := True;
  if not IsDotNet10DesktopInstalled() then
  begin
    if MsgBox('VpncBar needs the .NET 10 Desktop Runtime, which is not installed.' + #13#10#13#10 +
              'Click Yes to open the download page now — install the ".NET Desktop Runtime 10.x (x64)",' + #13#10 +
              'then run this setup again.' + #13#10#13#10 +
              'Click No to continue installing anyway (VpncBar will not run until the runtime is installed).',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0',
                '', '', SW_SHOW, ewNoWait, ErrorCode);
      Result := False;   // abort so the user installs the runtime, then re-runs setup
    end;
  end;
end;

// On uninstall: stop the tray, then ask separately whether to also remove the
// user's saved profiles and stored credentials. Those live in the original
// (non-elevated) user's profile, so the purge runs AS THAT USER via the app's
// --purge-* modes (the uninstaller itself is elevated). Both are kept by
// default; session logs and the program's own removal are unaffected.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec('taskkill.exe', '/IM VpncBar.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if MsgBox('Also remove your saved VPN profiles?' + #13#10#13#10 +
              'Choose No to keep them for a future reinstall.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      ExecAsOriginalUser(ExpandConstant('{app}\{#AppExe}'), '--purge-profiles', '',
                         SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if MsgBox('Also remove your saved passwords / group secrets from Windows' + #13#10 +
              'Credential Manager?' + #13#10#13#10 +
              'Choose No to keep them for a future reinstall.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      ExecAsOriginalUser(ExpandConstant('{app}\{#AppExe}'), '--purge-credentials', '',
                         SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
