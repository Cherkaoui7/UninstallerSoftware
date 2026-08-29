#define MyAppName "Uninstaller"
#define MyAppVersion GetEnv("APP_VERSION")
#define MyAppPublisher "Uninstaller Team"
#define MyAppExeName "Uninstaller.App.exe"
#define OutputPath "..\Artifacts"

[Setup]
AppId={{E3E47A70-8774-4C3D-A480-281C3084C9ED}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir={#OutputPath}
OutputBaseFilename=Uninstaller-{#MyAppVersion}-win-x64-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\Publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Ensures smooth upgrades without prompt if app is closed
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
