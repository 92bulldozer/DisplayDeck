; DisplayDeck 인스톨러 스크립트 (Inno Setup 6.3+)
; 컴파일: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
; 사전: dotnet publish -c Release -r win-x64 --self-contained true 로 publish 폴더 생성

#define MyAppName "DisplayDeck"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "92bulldozer"
#define MyAppExeName "DisplayDeck.exe"
#define MyPublishDir "DisplayDeck\DisplayDeck\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{A1F4C2E8-7B3D-4E96-9C1A-DD5208BE2026}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=DisplayDeck\DisplayDeck\appicon.ico
OutputDir=installer-output
OutputBaseFilename=DisplayDeck-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
