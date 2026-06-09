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
#define AppVersion "1.1.0"
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
SetupIconFile=..\..\src\assets\VpncBar.ico
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
Source: "..\..\scripts\dns-info.ps1"; DestDir: "{app}";          Flags: ignoreversion
Source: "..\backend\*";               DestDir: "{app}\backend";  Flags: ignoreversion recursesubdirs
Source: "..\..\vendor\NOTICE";                DestDir: "{app}";              DestName: "NOTICE.txt"; Flags: ignoreversion
Source: "..\..\LICENSE";                      DestDir: "{app}";              DestName: "LICENSE.txt"; Flags: ignoreversion isreadme skipifsourcedoesntexist

[Icons]
Name: "{group}\VpncBar";          Filename: "{app}\{#AppExe}"
; Read-only DNS / split-DNS diagnostic; opens a PowerShell window with the report.
Name: "{group}\DNS Info";         Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -NoExit -File ""{app}\dns-info.ps1"""; Comment: "Show the current Windows DNS / split-DNS configuration"

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
var
  DotNetProgress: TOutputProgressWizardPage;   // two-row "Downloading / Installing" page

procedure InitializeWizard;
begin
  DotNetProgress := CreateOutputProgressPage(
    'Prerequisite: .NET 10 Desktop Runtime',
    'If the .NET 10 Desktop Runtime is missing, Setup downloads and installs it here.');
end;

// Drives the first row of DotNetProgress from the live download. Return False to
// cancel; we always continue.
function OnDotNetDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if ProgressMax > 0 then
  begin
    DotNetProgress.SetProgress(Progress, ProgressMax);
    DotNetProgress.SetText(
      'Downloading the .NET 10 Desktop Runtime...  ' + IntToStr(Progress div 1048576) + ' of ' + IntToStr(ProgressMax div 1048576) + ' MB',
      'Installing the .NET 10 Desktop Runtime...   (pending)');
  end;
  Result := True;
end;

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

// Stop a running VpncBar before an over-the-top (re)install so its open handles
// don't lock the files in {app}. Kills the tray and the service process (both are
// VpncBar.exe) plus any orphaned backend children, then clears the service's SCM
// state. Setup is elevated (PrivilegesRequired=admin), so it has the rights.
procedure StopRunningVpncBar;
var ResultCode: Integer;
begin
  // Ask the service to stop first so it tears down tunnels/routes/NRPT cleanly
  // (the tray doesn't auto-restart it), give it a moment, then force-kill any
  // survivors so nothing keeps a handle open on {app}.
  Exec('sc.exe',       'stop VpncBar',           '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);
  Exec('taskkill.exe', '/IM VpncBar.exe /F',     '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/IM openconnect.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/IM vpnc.exe /F',        '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);   // let the OS release the file handles before the copy
end;

// If the .NET 10 Desktop Runtime is missing, offer to download + install it
// inline (setup is elevated, so a machine-wide runtime install works). Runs
// after the user clicks Install; returning a non-empty string aborts with that
// message. Falls back to opening the download page if the auto-install fails.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Installer: String;
begin
  Result := '';
  StopRunningVpncBar();   // free any files locked by a running instance first

  if IsDotNet10DesktopInstalled() then
    exit;

  if MsgBox('VpncBar needs the .NET 10 Desktop Runtime (x64), which is not installed.' + #13#10#13#10 +
            'Download (~55 MB) and install it now?' + #13#10#13#10 +
            'Choose No to cancel (VpncBar can''t run without it).',
            mbConfirmation, MB_YESNO) <> IDYES then
  begin
    Result := 'The .NET 10 Desktop Runtime is required. Setup was cancelled.';
    exit;
  end;

  // Download (real progress on row 1) then install (the runtime's own /passive
  // window shows install progress; row 2 tracks the step) — both on one page.
  Installer := ExpandConstant('{tmp}\windowsdesktop-runtime-10-x64.exe');
  DotNetProgress.Show;
  try
    DotNetProgress.SetText('Downloading the .NET 10 Desktop Runtime...',
                           'Installing the .NET 10 Desktop Runtime...   (pending)');
    try
      DownloadTemporaryFile('https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe',
                            'windowsdesktop-runtime-10-x64.exe', '', @OnDotNetDownloadProgress);
    except
      Installer := '';   // download failed
    end;

    if (Installer <> '') and FileExists(Installer) then
    begin
      DotNetProgress.SetProgress(1, 1);   // download row done
      DotNetProgress.SetText('Downloaded the .NET 10 Desktop Runtime.',
                             'Installing the .NET 10 Desktop Runtime...');
      // /passive (not /quiet) so the runtime installer shows its own progress UI.
      if Exec(Installer, '/install /passive /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
        if ResultCode = 3010 then NeedsRestart := True;   // 3010 = installed, restart pending
    end;
  finally
    DotNetProgress.Hide;
  end;

  if not IsDotNet10DesktopInstalled() then
  begin
    // Auto-install didn't take — point the user at the manual download.
    ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/10.0', '', '', SW_SHOW, ewNoWait, ResultCode);
    Result := 'The .NET 10 Desktop Runtime could not be installed automatically.' + #13#10 +
              'The download page has been opened — install the "Desktop Runtime 10 (x64)",' + #13#10 +
              'then run this setup again.';
  end;
end;

// On uninstall: stop the tray + service + backends, then ask separately whether
// to also remove the user's saved profiles and stored credentials (both live in
// the user's own %APPDATA% / Credential Manager). Inno forbids ExecAsOriginalUser
// during uninstall, so we use Exec: the purge runs in the uninstaller's (elevated)
// context, which targets the right per-user store when the person uninstalling is
// the one who installed it (the normal case on a personal machine). Both are kept
// by default; session logs and the program's own removal are unaffected.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    StopRunningVpncBar();   // release {app} file handles before deletion
    if MsgBox('Also remove your saved VPN profiles?' + #13#10#13#10 +
              'Choose No to keep them for a future reinstall.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      Exec(ExpandConstant('{app}\{#AppExe}'), '--purge-profiles', '',
           SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if MsgBox('Also remove your saved passwords / group secrets from Windows' + #13#10 +
              'Credential Manager?' + #13#10#13#10 +
              'Choose No to keep them for a future reinstall.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      Exec(ExpandConstant('{app}\{#AppExe}'), '--purge-credentials', '',
           SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
