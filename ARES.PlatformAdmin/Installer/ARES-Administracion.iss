#ifndef AppSource
  #define AppSource "..\..\distribucion\ARES-Administracion-Windows-x64\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\distribucion"
#endif
#ifndef AppVersion
  #define AppVersion "1.2.1"
#endif
[Setup]
AppId={{BCA8CB94-A263-4B87-93E8-8326021C50B4}
AppName=ARES Administración
AppVersion={#AppVersion}
AppPublisher=ARES
DefaultDirName={localappdata}\Programs\ARES Administración
DefaultGroupName=ARES
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=ARES-Administracion-Setup
SetupIconFile=..\..\Branding\ares.ico
UninstallDisplayIcon={app}\ARES.Administracion.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: checkedonce
[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\ARES Administración"; Filename: "{app}\ARES.Administracion.exe"
Name: "{autodesktop}\ARES Administración"; Filename: "{app}\ARES.Administracion.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\ARES.Administracion.exe"; Description: "Abrir ARES Administración"; Flags: nowait postinstall skipifsilent
