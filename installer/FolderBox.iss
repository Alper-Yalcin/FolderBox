; FolderBox installer (Inno Setup 6). Per-user, no administrator rights.
; Build: ISCC.exe installer\FolderBox.iss [/DAppVersion=1.0.0]
; Expects the self-contained publish output in ..\publish\win-x64 (see build.ps1 -Publish).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "FolderBox"
#define AppExe "FolderBox.exe"
#define AppPublisher "Alper Yalçın"
#define AppUrl "https://github.com/Alper-Yalcin/FolderBox"
#define SourceDir "..\publish\win-x64"

[Setup]
AppId={{9B0C1D4E-6A7F-4C3B-9E2D-1F0A2B3C4D5E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\publish
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile=..\src\FolderBox.App\Assets\FolderBox.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
ShowLanguageDialog=auto
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; The launcher shortcut FolderBox creates on first run (same name as the installer's optional one).
Type: files; Name: "{userdesktop}\{#AppName}.lnk"
Type: filesandordirs; Name: "{app}"

[Code]
// FolderBox has no main window, so close it explicitly before files are replaced or removed.
procedure KillFolderBox();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExe} /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillFolderBox();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  KillFolderBox();
  Result := True;
end;

// Remove the per-user integrations FolderBox registers itself (they point into {app}).
// User data in %LocalAppData%\FolderBox and the folders in %UserProfile%\FolderBox are kept.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#AppName}');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\.folderbox');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\FolderBox.Widget');
  end;
end;
