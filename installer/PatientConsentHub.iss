; ============================================================================
;  Patient Consent Hub - Windows installer
;  Build with Inno Setup 6:  iscc installer\PatientConsentHub.iss
;  Produces: installer\Output\PatientConsentHub_Setup.exe
;
;  Run build.ps1 first - this script packages the publish output, which
;  already contains the .NET runtime and ffmpeg.exe, so the target machine
;  needs nothing installed beforehand.
; ============================================================================

#define AppName        "Patient Consent Hub"
#define AppVersion     "1.0.0"
#define AppPublisher   "A&T"
#define AppExeName     "PatientConsentHub.exe"
#define PublishDir     "..\publish"

[Setup]
AppId={{9D2F4A61-5C77-4C0E-9F2B-6B0E1D3A77C4}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\A&T\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=PatientConsentHub_Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\src\PatientConsentHub\Assets\app.ico
AppMutex=PatientConsentHubSingleInstance

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a shortcut on the desktop"; GroupDescription: "Shortcuts:"

[Files]
; Everything produced by dotnet publish: app, .NET runtime, OpenCV natives,
; and Tools\ffmpeg\ffmpeg.exe.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; Default recording location, writable by the clinical staff who use it.
Name: "C:\Patient Consent Hub\Recordings"; Permissions: users-modify

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove application logs only. Patient recordings and settings are never
; deleted by the uninstaller - they are hospital records.
Type: filesandordirs; Name: "{localappdata}\A&T\Patient Consent Hub\Logs"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not FileExists(ExpandConstant('{app}\Tools\ffmpeg\ffmpeg.exe')) then
      MsgBox('Warning: the recording component (ffmpeg.exe) was not included in this build.' + #13#10 +
             'Recording will not work until it is added. Please contact A&T support.',
             mbError, MB_OK);
  end;
end;
