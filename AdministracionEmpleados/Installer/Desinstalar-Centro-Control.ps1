$ErrorActionPreference = 'Stop'
$destino = Join-Path $env:LOCALAPPDATA 'Programs\ARES Centro de Control'
$escritorio = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ARES Centro de Control.lnk'
$menuInicio = Join-Path (Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs') 'ARES Centro de Control.lnk'

Get-Process -Name 'ARES.ControlCenter' -ErrorAction SilentlyContinue | Stop-Process -Force
foreach ($acceso in @($escritorio, $menuInicio)) {
    if (Test-Path $acceso) { Remove-Item -LiteralPath $acceso -Force }
}
if (Test-Path $destino) { Remove-Item -LiteralPath $destino -Recurse -Force }

Write-Host 'ARES Centro de Control fue desinstalado.'
Write-Host 'La configuración y el historial local se conservaron en LocalAppData\ARES.'
