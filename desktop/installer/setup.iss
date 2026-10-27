; Alpha Premier ID Maker — Windows installer
; -------------------------------------------------------------------
; Builds AP-ID-Maker-Setup-<version>.exe: one file to hand someone, which
; installs the app, puts it in the Start menu and in Settings > Apps, and
; uninstalls cleanly.
;
; Compile with Inno Setup 6 (free, https://jrsoftware.org/isdl.php):
;     desktop\build-installer.cmd
;
; It installs per user, so Windows never asks for administrator rights.
; The exe is unsigned, so the first person to run it meets SmartScreen —
; see desktop\README.md.

#define AppName    "Alpha Premier ID Maker"
#define Publisher  "Alpha Premier Group of Companies"
#define SrcRoot    "..\.."
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
; never change AppId: it is how Windows knows an install is an upgrade
AppId={{74A48AC7-B3E1-5D41-8F31-C21BBBEB97D0}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL=https://alphapremiergroup.com/
DefaultDirName={localappdata}\AlphaPremierIDMaker
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
; per user: no administrator rights, so no UAC prompt either
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=AP-ID-Maker-Setup-{#AppVersion}
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\app.ico
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
AppCopyright=© {#Publisher}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#Publisher}
VersionInfoDescription={#AppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Put a shortcut on my desktop"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#SrcRoot}\Alpha Premier ID Maker.html"; DestDir: "{app}"; DestName: "index.html"; Flags: ignoreversion
Source: "..\app.ico";                            DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Edge in app mode: its own window, its own icon, no address bar and no tabs
Name: "{group}\{#AppName}";           Filename: "{code:EdgePath}"; Parameters: "{code:AppArgs}"; IconFilename: "{app}\app.ico"; WorkingDir: "{app}"; Comment: "Makes the Alpha Premier employee ID card"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";     Filename: "{code:EdgePath}"; Parameters: "{code:AppArgs}"; IconFilename: "{app}\app.ico"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{code:EdgePath}"; Parameters: "{code:AppArgs}"; Description: "Open {#AppName} now"; Flags: nowait postinstall skipifsilent

[Code]
var
  CachedEdge: String;

{ Edge draws the card, so the install is pointless without it. Look where
  it normally lives, then ask Windows where it registered itself. }
function FindEdge(): String;
var
  p: String;
begin
  if CachedEdge <> '' then begin Result := CachedEdge; exit; end;

  p := ExpandConstant('{commonpf32}\Microsoft\Edge\Application\msedge.exe');
  if not FileExists(p) then
    p := ExpandConstant('{commonpf}\Microsoft\Edge\Application\msedge.exe');
  if not FileExists(p) then
    p := ExpandConstant('{localappdata}\Microsoft\Edge\Application\msedge.exe');

  if not FileExists(p) then
    if not RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe', '', p) then
      p := '';
  if (p <> '') and (not FileExists(p)) then
    if not RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe', '', p) then
      p := '';

  if (p <> '') and FileExists(p) then CachedEdge := p else CachedEdge := '';
  Result := CachedEdge;
end;

function EdgePath(Param: String): String;
begin
  Result := FindEdge();
end;

{ The page is loaded from a file, so the path has to become a URL: back
  slashes forward, and spaces encoded, because a user name may contain one.
  Its own user-data-dir keeps the app's settings and its issued-ID list
  away from the person's ordinary browsing. }
function AppArgs(Param: String): String;
var
  url: String;
begin
  url := ExpandConstant('{app}\index.html');
  StringChangeEx(url, '\', '/', True);
  StringChangeEx(url, ' ', '%20', True);
  Result := '--app="file:///' + url + '"' +
            ' --user-data-dir="' + ExpandConstant('{app}\profile') + '"' +
            ' --no-first-run --no-default-browser-check --window-size=1280,900';
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if FindEdge() = '' then
  begin
    MsgBox('Microsoft Edge was not found on this computer.' + #13#10#13#10 +
           'The ID Maker uses it to draw the card, so there is nothing to ' +
           'install without it. Install Edge from microsoft.com/edge and ' +
           'run this again.', mbCriticalError, MB_OK);
    Result := False;
  end;
end;

{ The issued-ID list lives in the app's browser profile. Removing the app
  removes it, so say so rather than doing it quietly. The shared Google
  Sheet is untouched either way. }
procedure CurUninstallStepChanged(CurStep: TUninstallStep);
var
  prof: String;
begin
  if CurStep = usUninstall then
  begin
    prof := ExpandConstant('{app}\profile');
    if DirExists(prof) then
    begin
      if MsgBox('Also delete the list of IDs issued on this computer?' + #13#10#13#10 +
                'The shared Google Sheet is not touched, so if the register ' +
                'is set up nothing is really lost.',
                mbConfirmation, MB_YESNO) = IDYES then
        DelTree(prof, True, True, True);
    end;
  end;
end;
