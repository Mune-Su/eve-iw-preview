; EVE-IW-Preview installer (Inno Setup 6.3 or newer).
; Built by installer\build-installer.ps1, which passes AppVersion, NumericVersion, SourceExe and OutputDir.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef NumericVersion
  #define NumericVersion "0.0.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\publish-installer\EVE-IW-Preview.exe"
#endif
#ifndef OutputDir
  #define OutputDir "..\installer-output"
#endif

[Setup]
; Never change this ID: it is how Windows recognises a newer Setup as an upgrade of the installed app.
AppId={{B6D8F1E4-2A7C-4C0E-9E55-6C4F7A1D2B93}
AppName=EVE-IW-Preview
AppVersion={#AppVersion}
AppVerName=EVE-IW-Preview {#AppVersion}
AppPublisher=AUTZ IronWorks
AppPublisherURL=https://github.com/Mune-Su/eve-iw-preview
AppSupportURL=https://github.com/Mune-Su/eve-iw-preview/issues
AppUpdatesURL=https://github.com/Mune-Su/eve-iw-preview/releases
VersionInfoVersion={#NumericVersion}
VersionInfoProductName=EVE-IW-Preview
VersionInfoCompany=AUTZ IronWorks
DefaultDirName={autopf}\EVE-IW-Preview
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Setup waits (and asks you to close the app) while EVE-IW-Preview is running. It is never closed for you.
AppMutex=AUTZ.IronWorks.Preview.SingleInstance
CloseApplications=no
SetupIconFile=..\src\AUTZ.IronWorks.Preview\Assets\app.ico
UninstallDisplayIcon={app}\EVE-IW-Preview.exe
UninstallDisplayName=EVE-IW-Preview
OutputDir={#OutputDir}
OutputBaseFilename=EVE-IW-Preview-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Tasks]
; Unticked on a first install. An update keeps what was chosen the first time (Setup remembers it), so a silent update never adds or removes the icon.
Name: "desktopicon"; Description: "Create a desktop icon"; Flags: unchecked

[Icons]
Name: "{autoprograms}\EVE-IW-Preview"; Filename: "{app}\EVE-IW-Preview.exe"
Name: "{autodesktop}\EVE-IW-Preview"; Filename: "{app}\EVE-IW-Preview.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\EVE-IW-Preview.exe"; Description: "Start EVE-IW-Preview"; Flags: nowait postinstall skipifsilent

[Code]
// EVE-IW-Preview needs the .NET 8 Desktop Runtime (x64). Warn if it is not there, but let the user carry on.
function DesktopRuntimeInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if (not WizardSilent) and (not DesktopRuntimeInstalled) then
    Result := MsgBox('The .NET 8 Desktop Runtime (x64) was not found on this PC, and EVE-IW-Preview needs it to run.'
      + #13#10 + #13#10 + 'You can install it from https://dotnet.microsoft.com/download/dotnet/8.0 before or after this setup.'
      + #13#10 + #13#10 + 'Continue installing EVE-IW-Preview now?', mbConfirmation, MB_YESNO) = IDYES;
end;

// Settings and profiles live in %LOCALAPPDATA%\EVE-IronWorks-Preview and are left alone. Only the start-up entry is removed.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'EVE-IronWorks-Preview');
end;

