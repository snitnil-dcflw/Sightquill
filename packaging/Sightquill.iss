#ifndef AppVersion
#define AppVersion "0.6.1"
#endif
#ifndef SourceDir
#define SourceDir "..\artifacts\Sightquill-win-x64"
#endif
; Compile after publish.ps1 with Inno Setup 6: ISCC.exe packaging\Sightquill.iss
[Setup]
AppId={{9EC11843-24B4-48A7-9F03-F60A29EEA811}
AppName=Sightquill
AppVersion={#AppVersion}
AppPublisher=Sightquill
DefaultDirName={localappdata}\Programs\Sightquill
DefaultGroupName=Sightquill
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=Sightquill-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\Sightquill\Assets\Sightquill.ico
UninstallDisplayIcon={app}\Sightquill.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
AppMutex=Local\Sightquill.Desktop
RestartApplications=no
LicenseFile=..\LICENSE
VersionInfoDescription=Sightquill Installer
AppPublisherURL=https://github.com/snitnil-dcflw/Sightquill
AppSupportURL=https://github.com/snitnil-dcflw/Sightquill/issues

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\Sightquill.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\Integrations\*"; DestDir: "{app}\Integrations"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#SourceDir}\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Sightquill"; Filename: "{app}\Sightquill.exe"
Name: "{autodesktop}\Sightquill"; Filename: "{app}\Sightquill.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Sightquill.exe"; Description: "Open Sightquill"; Flags: nowait postinstall skipifsilent
