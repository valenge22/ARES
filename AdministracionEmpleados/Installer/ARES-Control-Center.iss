#ifndef AppSource
  #define AppSource "..\..\distribucion\ARES-Centro-Control-Windows-x64\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\distribucion"
#endif
#ifndef AppVersion
  #define AppVersion "1.3.1"
#endif

[Setup]
AppId={{6D605970-4D61-4C1F-94A7-60BD4939BA57}
AppName=ARES Centro de Control
AppVersion={#AppVersion}
AppPublisher=ARES
DefaultDirName={localappdata}\Programs\ARES Centro de Control
DefaultGroupName=ARES
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=ARES-Centro-Control-Setup
SetupIconFile=..\..\Branding\ares.ico
UninstallDisplayIcon={app}\ARES.ControlCenter.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: checkedonce

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ARES Centro de Control"; Filename: "{app}\ARES.ControlCenter.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\ARES Centro de Control"; Filename: "{app}\ARES.ControlCenter.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\ARES.ControlCenter.exe"; Description: "Abrir ARES Centro de Control"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
