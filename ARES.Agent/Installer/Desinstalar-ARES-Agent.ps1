param([string]$LogPath = (Join-Path $env:TEMP 'ARES-Agent-Uninstall.log'))

$ErrorActionPreference = 'Stop'
trap {
    $detalle = "Fecha: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`r`nError: $($_.Exception.Message)"
    Set-Content -LiteralPath $LogPath -Value $detalle -Encoding UTF8
    exit 1
}
$nombreTarea = 'ARES Agent'
$nombreTareaServicio = 'ARES Agent Service'
$destino = Join-Path $env:ProgramFiles 'ARES Agent'
$rutaProteccion = Join-Path $env:ProgramData 'ARES\agent-uninstall.json'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $procesoElevado = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'),
        '-LogPath', ('"' + $LogPath + '"')
    )
    exit $procesoElevado.ExitCode
}

if (-not (Test-Path $rutaProteccion)) {
    throw 'No se encontró la protección de desinstalación. Contactá al administrador de ARES.'
}

function Convertir-SecureString([Security.SecureString]$valor) {
    $puntero = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($valor)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($puntero) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($puntero) }
}

$proteccion = Get-Content -LiteralPath $rutaProteccion -Raw | ConvertFrom-Json
$passwordSeguro = Read-Host 'Contraseña de desinstalación de ARES Agent' -AsSecureString
$passwordPlano = Convertir-SecureString $passwordSeguro
try {
    $sal = [Convert]::FromBase64String($proteccion.Salt)
    $esperado = [Convert]::FromBase64String($proteccion.Hash)
    $derivador = New-Object Security.Cryptography.Rfc2898DeriveBytes(
        $passwordPlano, $sal, [int]$proteccion.Iterations, [Security.Cryptography.HashAlgorithmName]::SHA256)
    $obtenido = $derivador.GetBytes(32)
    $derivador.Dispose()

    $diferencia = 0
    for ($i = 0; $i -lt $esperado.Length; $i++) {
        $diferencia = $diferencia -bor ($esperado[$i] -bxor $obtenido[$i])
    }
    if ($diferencia -ne 0) { throw 'Contraseña de desinstalación incorrecta.' }
}
finally { $passwordPlano = $null }

Get-Process -Name 'ARES.Agent' -ErrorAction SilentlyContinue | Stop-Process -Force
Unregister-ScheduledTask -TaskName $nombreTarea -Confirm:$false -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName $nombreTareaServicio -Confirm:$false -ErrorAction SilentlyContinue
$configuracionAgente = Join-Path $destino 'appsettings.json'
if (Test-Path $configuracionAgente) {
    $configuracion = Get-Content -LiteralPath $configuracionAgente -Raw | ConvertFrom-Json
    if (-not [string]::IsNullOrWhiteSpace($configuracion.ManagedUser)) {
        & (Join-Path $env:SystemRoot 'System32\net.exe') user ([string]$configuracion.ManagedUser) /active:yes | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "No se pudo volver a habilitar la cuenta '$($configuracion.ManagedUser)'. No se desinstaló ARES."
        }
    }
}
if (Test-Path $destino) { Remove-Item -LiteralPath $destino -Recurse -Force }
if (Test-Path $rutaProteccion) { Remove-Item -LiteralPath $rutaProteccion -Force }
Set-Content -LiteralPath $LogPath -Value "Desinstalación completada el $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')." -Encoding UTF8

Add-Type -AssemblyName PresentationFramework
[System.Windows.MessageBox]::Show('ARES Agent fue desinstalado.', 'ARES Agent') | Out-Null
