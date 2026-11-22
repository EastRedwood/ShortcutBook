; ShortcutBook installer (Inno Setup 6). Build with installer\build-installer.ps1.
; Keep this file ASCII only (Inno needs a BOM for non-ASCII text).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "ShortcutBook"
#define AppExe "ShortcutBook.exe"

[Setup]
; AppId must never change: it lets a new installer upgrade an old install.
AppId={{C4ADB9D6-3C5E-4F21-AEE9-F749270A4A64}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=EastRedwood
VersionInfoVersion={#AppVersion}
; Per-user install (no administrator rights). The app keeps its "Lists" folder
; next to the exe, so the install folder must stay writable for the user.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\publish\installer
OutputBaseFilename=ShortcutBook-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\app\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Run]
; Register the .sbook / .sblink Explorer integration for this exe (current user only).
Filename: "{app}\{#AppExe}"; Parameters: "--register"; Flags: runhidden
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Close a running instance so its files can be removed.
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: runhidden; RunOnceId: "ExitApp"

[Code]
// Ask a running instance to exit before files are replaced (upgrade).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Exe: String;
  Code: Integer;
begin
  Result := '';
  Exe := ExpandConstant('{app}\{#AppExe}');
  if FileExists(Exe) then
  begin
    Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Sleep(800);
  end;
end;

// Remove the Explorer file-type registration, but only if it points at this install.
// The user's list files (Lists folder and any .sbook files) are never deleted.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Cmd: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if RegQueryStringValue(HKCU, 'Software\Classes\ShortcutBook.List\shell\open\command', '', Cmd) and
       (Pos(Lowercase(ExpandConstant('{app}')), Lowercase(Cmd)) > 0) then
    begin
      RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\.sbook');
      RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\ShortcutBook.List');
      RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\.sblink');
      RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\ShortcutBook.Link');
    end;
  end;
end;
