param(
    [string]$ServerUrl = 'http://localhost:5050',
    [string]$ApiKey = 'CAMBIAR-ESTA-CLAVE'
)

$ErrorActionPreference = 'Stop'

$nombreTarea = 'ARES Agent'
$destino = Join-Path $env:ProgramFiles 'ARES Agent'
$origen = Join-Path $PSScriptRoot 'app'

if (-not (Test-Path (Join-Path $origen 'ARES.Agent.exe'))) {
    throw 'No se encontró app\ARES.Agent.exe. Usá el paquete de distribución completo.'
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'),
        '-ServerUrl', ('"' + $ServerUrl + '"'), '-ApiKey', ('"' + $ApiKey + '"')
    )
    exit
}

New-Item -ItemType Directory -Path $destino -Force | Out-Null
Copy-Item -Path (Join-Path $origen '*') -Destination $destino -Recurse -Force

@{
    ServerUrl = $ServerUrl.TrimEnd('/')
    ApiKey = $ApiKey
    HeartbeatSeconds = 10
} | ConvertTo-Json | Set-Content -Path (Join-Path $destino 'appsettings.json') -Encoding UTF8

$ejecutable = Join-Path $destino 'ARES.Agent.exe'
$accion = New-ScheduledTaskAction -Execute $ejecutable
$disparador = New-ScheduledTaskTrigger -AtLogOn
$configuracion = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 999 `
    -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName $nombreTarea -Action $accion -Trigger $disparador -Settings $configuracion -Description 'Inicia el agente visible de ARES al ingresar a Windows.' -Force | Out-Null

Start-Process $ejecutable

Add-Type -AssemblyName PresentationFramework
[System.Windows.MessageBox]::Show('ARES Agent se instaló correctamente. El escudo aparecerá junto al reloj de Windows.', 'ARES Agent') | Out-Null
