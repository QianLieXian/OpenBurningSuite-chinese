#ifndef AppVersion
  #define AppVersion "1.2.0"
#endif

[Setup]
AppId=Open Burning Suite
AppName=Open Burning Suite
AppVersion={#AppVersion}
SetupArchitecture=x64
MinVersion=10.0
AppPublisher=Open Burning Suite contributors
AppPublisherURL=https://github.com/QianLieXian/OpenBurningSuite-chinese
DefaultDirName={autopf}\OpenBurningSuite
DefaultGroupName=Open Burning Suite
UninstallDisplayIcon={app}\OpenBurningSuite.exe
OutputDir=..\installer
OutputBaseFilename=OpenBurningSuite-v{#AppVersion}-windows-amd64-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\publish\LICENSE
PrivilegesRequired=admin
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
UsePreviousLanguage=yes
CloseApplications=yes
UninstallDisplayName=Open Burning Suite

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "zh"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Registry]
; Used until a user explicitly saves their own language preference.
Root: HKLM; Subkey: "Software\OpenBurningSuite"; ValueType: string; ValueName: "UILanguage"; ValueData: "{language}"; Flags: uninsdeletevalue uninsdeletekeyifempty

[Icons]
Name: "{group}\Open Burning Suite"; Filename: "{app}\OpenBurningSuite.exe"
Name: "{group}\{cm:UninstallProgram,Open Burning Suite}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Open Burning Suite"; Filename: "{app}\OpenBurningSuite.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\OpenBurningSuite.exe"; Description: "{cm:LaunchProgram,Open Burning Suite}"; Flags: nowait postinstall skipifsilent
