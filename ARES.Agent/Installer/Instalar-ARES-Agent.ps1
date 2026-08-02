param(
    [string]$ServerUrl = 'https://ares-3bic.onrender.com',
    [string]$ApiKey = 'CAMBIAR-ESTA-CLAVE',
    [string]$ManagedUser = $env:USERNAME,
    [string]$LogPath = (Join-Path $env:TEMP 'ARES-Agent-Install.log')
)

$ErrorActionPreference = 'Stop'

trap {
    $detalle = @(
        "Fecha: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        "Error: $($_.Exception.Message)"
        "Tipo: $($_.Exception.GetType().FullName)"
        "Ubicacion: $($_.InvocationInfo.PositionMessage)"
    ) -join [Environment]::NewLine
    Set-Content -LiteralPath $LogPath -Value $detalle -Encoding UTF8
    exit 1
}

$nombreTarea = 'ARES Agent'
$nombreTareaServicio = 'ARES Agent Service'
$destino = Join-Path $env:ProgramFiles 'ARES Agent'
$origen = Join-Path $PSScriptRoot 'app'
$rutaProteccion = Join-Path $env:ProgramData 'ARES\agent-uninstall.json'
$proteccionIncluida = Join-Path $PSScriptRoot 'uninstall-protection.json'

if (-not (Test-Path (Join-Path $origen 'ARES.Agent.exe'))) {
    throw 'No se encontró app\ARES.Agent.exe. Usá el paquete de distribución completo.'
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $procesoElevado = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'),
        '-ServerUrl', ('"' + $ServerUrl + '"'), '-ApiKey', ('"' + $ApiKey + '"'),
        '-ManagedUser', ('"' + $ManagedUser + '"'),
        '-LogPath', ('"' + $LogPath + '"')
    )
    exit $procesoElevado.ExitCode
}

$usuarioAdministrado = $ManagedUser
if ([string]::IsNullOrWhiteSpace($usuarioAdministrado)) {
    throw 'No se pudo identificar la cuenta del empleado.'
}
$miembroAdministradores = Get-LocalGroupMember -SID 'S-1-5-32-544' -ErrorAction Stop |
    Where-Object { $_.Name -ieq "$env:COMPUTERNAME\$usuarioAdministrado" }
if ($miembroAdministradores) {
    throw "La cuenta '$usuarioAdministrado' es administradora. Por seguridad ARES no puede bloquearla. Creá una cuenta estándar para el empleado y ejecutá el instalador desde esa sesión."
}

if (-not (Test-Path $proteccionIncluida)) {
    throw 'El paquete no contiene la protección administrativa de desinstalación.'
}
New-Item -ItemType Directory -Path (Split-Path $rutaProteccion) -Force | Out-Null
Copy-Item -LiteralPath $proteccionIncluida -Destination $rutaProteccion -Force

New-Item -ItemType Directory -Path $destino -Force | Out-Null

# Permite actualizar una instalación existente sin que el ejecutable bloquee la copia.
Get-Process -Name 'ARES.Agent' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Copy-Item -Path (Join-Path $origen '*') -Destination $destino -Recurse -Force

@{
    ServerUrl = $ServerUrl.TrimEnd('/')
    ApiKey = $ApiKey
    HeartbeatSeconds = 10
    ManagedUser = $usuarioAdministrado
} | ConvertTo-Json | Set-Content -Path (Join-Path $destino 'appsettings.json') -Encoding UTF8

$ejecutable = Join-Path $destino 'ARES.Agent.exe'
$accion = New-ScheduledTaskAction -Execute $ejecutable
$disparador = New-ScheduledTaskTrigger -AtLogOn -User "$env:COMPUTERNAME\$usuarioAdministrado"
$principalInteractivo = New-ScheduledTaskPrincipal -UserId "$env:COMPUTERNAME\$usuarioAdministrado" -LogonType Interactive -RunLevel Limited
$configuracion = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 999 `
    -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName $nombreTarea -Action $accion -Trigger $disparador -Principal $principalInteractivo -Settings $configuracion -Description 'Inicia el agente visible de ARES al ingresar a Windows.' -Force | Out-Null

$accionServicio = New-ScheduledTaskAction -Execute $ejecutable -Argument '--service'
$disparadorServicio = New-ScheduledTaskTrigger -AtStartup
$principalServicio = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName $nombreTareaServicio -Action $accionServicio -Trigger $disparadorServicio `
    -Principal $principalServicio -Settings $configuracion `
    -Description 'Mantiene la conexion remota de ARES y aplica el bloqueo nativo.' -Force | Out-Null
# El instalador está elevado y Start-Process abriría el agente en la sesión del
# administrador. La tarea lo inicia con la identidad y la sesión del empleado.
Start-ScheduledTask -TaskName $nombreTarea
Start-Sleep -Seconds 2
if ((Get-ScheduledTask -TaskName $nombreTarea).State -ne 'Running') {
    throw "No se pudo iniciar ARES Agent en la sesión de '$usuarioAdministrado'. Verificá que esa cuenta esté desbloqueada y tenga una sesión iniciada."
}
Start-ScheduledTask -TaskName $nombreTareaServicio
Set-Content -LiteralPath $LogPath -Value "Instalación completada correctamente el $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')." -Encoding UTF8

Add-Type -AssemblyName PresentationFramework
[System.Windows.MessageBox]::Show('ARES Agent se instaló correctamente. El escudo aparecerá junto al reloj de Windows.', 'ARES Agent') | Out-Null
