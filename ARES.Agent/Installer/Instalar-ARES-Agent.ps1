param(
    [string]$ServerUrl = 'https://ares-3bic.onrender.com',
    [string]$ApiKey = 'CAMBIAR-ESTA-CLAVE',
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
$destino = Join-Path $env:ProgramFiles 'ARES Agent'
$origen = Join-Path $PSScriptRoot 'app'
$rutaProteccion = Join-Path $env:ProgramData 'ARES\agent-uninstall.json'

if (-not (Test-Path (Join-Path $origen 'ARES.Agent.exe'))) {
    throw 'No se encontró app\ARES.Agent.exe. Usá el paquete de distribución completo.'
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $procesoElevado = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'),
        '-ServerUrl', ('"' + $ServerUrl + '"'), '-ApiKey', ('"' + $ApiKey + '"'),
        '-LogPath', ('"' + $LogPath + '"')
    )
    exit $procesoElevado.ExitCode
}

function Convertir-SecureString([Security.SecureString]$valor) {
    $puntero = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($valor)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($puntero) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($puntero) }
}

Write-Host ''
Write-Host 'Configurá una contraseña exclusiva para desinstalar ARES Agent.' -ForegroundColor Cyan
$passwordSeguro = Read-Host 'Contraseña de desinstalación' -AsSecureString
$confirmacionSegura = Read-Host 'Repetí la contraseña' -AsSecureString
$passwordPlano = Convertir-SecureString $passwordSeguro
$confirmacionPlana = Convertir-SecureString $confirmacionSegura
try {
    if ([string]::IsNullOrWhiteSpace($passwordPlano) -or $passwordPlano.Length -lt 8) {
        throw 'La contraseña de desinstalación debe tener al menos 8 caracteres.'
    }
    if ($passwordPlano -cne $confirmacionPlana) {
        throw 'Las contraseñas de desinstalación no coinciden.'
    }

    $iteraciones = 200000
    $sal = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($sal)
    $derivador = New-Object Security.Cryptography.Rfc2898DeriveBytes(
        $passwordPlano, $sal, $iteraciones, [Security.Cryptography.HashAlgorithmName]::SHA256)
    $hash = $derivador.GetBytes(32)
    $derivador.Dispose()

    New-Item -ItemType Directory -Path (Split-Path $rutaProteccion) -Force | Out-Null
    @{
        Salt = [Convert]::ToBase64String($sal)
        Hash = [Convert]::ToBase64String($hash)
        Iterations = $iteraciones
    } | ConvertTo-Json | Set-Content -LiteralPath $rutaProteccion -Encoding UTF8
}
finally {
    $passwordPlano = $null
    $confirmacionPlana = $null
}

New-Item -ItemType Directory -Path $destino -Force | Out-Null

# Permite actualizar una instalación existente sin que el ejecutable bloquee la copia.
Get-Process -Name 'ARES.Agent' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
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
Set-Content -LiteralPath $LogPath -Value "Instalación completada correctamente el $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')." -Encoding UTF8

Add-Type -AssemblyName PresentationFramework
[System.Windows.MessageBox]::Show('ARES Agent se instaló correctamente. El escudo aparecerá junto al reloj de Windows.', 'ARES Agent') | Out-Null
