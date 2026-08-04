#ifndef PackageSource
  #define PackageSource "..\..\distribucion\ARES-Agent-Windows-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\distribucion"
#endif
#ifndef SetupUiSource
  #define SetupUiSource "..\..\distribucion\ARES-Agent-Setup-UI"
#endif
#ifndef AppVersion
  #define AppVersion "1.6.5"
#endif

[Setup]
AppId={{39BC7511-C265-476A-A302-5EDB2A4995B9}
AppName=ARES Agent
AppVersion={#AppVersion}
AppPublisher=ARES
DefaultDirName={tmp}\ARES-Agent-Setup
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename=ARES-Agent-Setup
SetupIconFile=..\..\Branding\ares.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupLogging=yes
Uninstallable=no
CreateAppDir=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Files]
Source: "{#PackageSource}\*"; DestDir: "{tmp}\ARES-Agent-Package"; Flags: ignoreversion recursesubdirs createallsubdirs deleteafterinstall
Source: "{#SetupUiSource}\*"; DestDir: "{tmp}\ARES-Agent-Setup-UI"; Flags: ignoreversion recursesubdirs createallsubdirs deleteafterinstall

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  PackageDir: String;
begin
  if CurStep = ssPostInstall then
  begin
    PackageDir := ExpandConstant('{tmp}\ARES-Agent-Package');
    WizardForm.StatusLabel.Caption := 'Configurando las cuentas de Windows y ARES Agent...';
    if not Exec(ExpandConstant('{tmp}\ARES-Agent-Setup-UI\ARES.Agent.Setup.exe'),
      '--package "' + PackageDir + '"', PackageDir,
      SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
      RaiseException('No se pudo iniciar la configuracion de ARES Agent.');
    if ResultCode = 2 then
      Abort;
    if ResultCode <> 0 then
      RaiseException('ARES Agent no pudo instalarse. Revisa el error mostrado por el configurador.');
  end;
end;
