param([string]$LogPath = (Join-Path $env:TEMP 'ARES-ControlCenter-Install.log'))

$ErrorActionPreference = 'Stop'
trap {
    $detalle = "Fecha: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')`r`nError: $($_.Exception.Message)`r`n$($_.InvocationInfo.PositionMessage)"
    Set-Content -LiteralPath $LogPath -Value $detalle -Encoding UTF8
    exit 1
}

$origen = Join-Path $PSScriptRoot 'app'
$destino = Join-Path $env:LOCALAPPDATA 'Programs\ARES Centro de Control'
$ejecutable = Join-Path $destino 'ARES.ControlCenter.exe'

Get-Process -Name 'ARES.ControlCenter' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400
New-Item -ItemType Directory -Path $destino -Force | Out-Null
Copy-Item -Path (Join-Path $origen '*') -Destination $destino -Recurse -Force

$shell = New-Object -ComObject WScript.Shell
$escritorio = [Environment]::GetFolderPath('Desktop')
$menuInicio = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs'

foreach ($ruta in @(
    (Join-Path $escritorio 'ARES Centro de Control.lnk'),
    (Join-Path $menuInicio 'ARES Centro de Control.lnk')
)) {
    $acceso = $shell.CreateShortcut($ruta)
    $acceso.TargetPath = $ejecutable
    $acceso.WorkingDirectory = $destino
    $acceso.Description = 'ARES Centro de Control'
    $acceso.IconLocation = "$ejecutable,0"
    $acceso.Save()
}

Set-Content -LiteralPath $LogPath -Value "Instalación completada el $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')." -Encoding UTF8
Start-Process $ejecutable
