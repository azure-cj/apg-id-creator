; The installer for Alpha Premier ID Maker: a proper Windows setup program.
;
; It installs the way any Windows app does. A wizard, a Start-menu
; shortcut, an optional desktop shortcut, and an entry under Settings ->
; Apps -> Installed apps with an Uninstall button. Run again over an older
; copy, it upgrades it in place.
;
; It installs for the person signed in and asks for no administrator.
; The one exception is the pen tablet driver, which Windows installs for
; the whole computer: that single step shows the usual permission prompt,
; and is skipped (and not offered) when a tablet driver is already there.
;
; Built by package.ps1 with Inno Setup 6, which passes in the four values
; below. For deploying without anyone at the keyboard:
;     AlphaPremierIdMaker-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
; and add /TASKS="" to leave the tablet driver out.

#ifndef AppVersion
  #error AppVersion is not set. Build with package.ps1.
#endif
#ifndef SourceDir
  #error SourceDir is not set. Build with package.ps1.
#endif
#ifndef DriverExe
  #error DriverExe is not set. Build with package.ps1.
#endif
#ifndef OutputDir
  #error OutputDir is not set. Build with package.ps1.
#endif

#define AppName "Alpha Premier ID Maker"
#define AppExe "AlphaPremierIdMaker.exe"
#define DriverExeName ExtractFileName(DriverExe)

[Setup]
; Never change AppId: it is how Windows knows a newer setup is the same app.
AppId={{6F0E2B6C-4D53-4C0B-9A8E-2C7B1D5A9E41}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Alpha Premier Group of Companies
AppPublisherURL=https://alphapremiergroup.com/
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=AlphaPremierIdMaker-Setup
SetupIconFile=..\IdMaker.App\AppIcon.ico
WizardStyle=modern
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
; if the app is open, offer to close it rather than failing on a file in use
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Put a shortcut on the desktop"; Flags: unchecked
Name: "driver"; Description: "Install the XP-Pen Deco Fun pen tablet driver (Windows will ask for permission)"; Check: not TabletDriverInstalled

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
; carried inside the setup program and only unpacked if the driver task runs
Source: "{#DriverExe}"; Flags: dontcopy

[InstallDelete]
; the earlier zip-and-script installer left its uninstall script here
Type: files; Name: "{app}\Uninstall.ps1"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Make employee ID cards"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Make employee ID cards"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Open {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
// Is this the name of a pen tablet driver? XP-Pen's own, or another
// maker's: two tablet drivers on one computer fight over the pen.
function IsTabletDriverName(const Name: String): Boolean;
var
  L: String;
begin
  L := Lowercase(Name);
  Result := (Pos('xp-pen', L) > 0) or (Pos('xppen', L) > 0) or (Pos('pentablet', L) > 0)
         or (Pos('wacom tablet', L) > 0) or (Pos('huion', L) > 0);
end;

function AnyTabletDriverUnder(const RootKey: Integer): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  DisplayName: String;
begin
  Result := False;
  if not RegGetSubkeyNames(RootKey, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', Names) then
    Exit;
  for I := 0 to GetArrayLength(Names) - 1 do
  begin
    if RegQueryStringValue(RootKey, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + Names[I], 'DisplayName', DisplayName)
       and IsTabletDriverName(DisplayName) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

// A driver registers itself for uninstalling like any other program.
function TabletDriverInstalled: Boolean;
begin
  Result := AnyTabletDriverUnder(HKLM64) or AnyTabletDriverUnder(HKLM32);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
  Installer: String;
begin
  if (CurStep <> ssPostInstall) or (not WizardIsTaskSelected('driver')) then
    Exit;
  ExtractTemporaryFile('{#DriverExeName}');
  Installer := ExpandConstant('{tmp}\{#DriverExeName}');
  // XP-Pen's installer is itself built with Inno Setup; these are its
  // switches for installing without questions. "runas" is what makes
  // Windows ask for permission, since this setup runs without it.
  if not ShellExec('runas', Installer, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ExitCode) then
    SuppressibleMsgBox('The pen tablet driver was not installed, because permission was not given.' + #13#10#13#10 +
      'The app is installed and works without it: sign with the mouse, or use a scan. Run this setup again to add the driver later.',
      mbInformation, MB_OK, IDOK)
  else if ExitCode <> 0 then
    SuppressibleMsgBox('The pen tablet driver''s installer stopped before finishing (code ' + IntToStr(ExitCode) + ').' + #13#10#13#10 +
      'The app is installed. To add the driver, download it from xp-pen.com/download/deco-fun.html.',
      mbInformation, MB_OK, IDOK)
  else
    SuppressibleMsgBox('The pen tablet driver is installed. Unplug the tablet and plug it back in. If the pen does not respond, restart the computer once.',
      mbInformation, MB_OK, IDOK);
end;
