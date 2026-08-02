$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$salida = Join-Path $raiz 'distribucion\ARES-Agent-Windows-x64'
$app = Join-Path $salida 'app'
$zip = Join-Path $raiz 'distribucion\ARES-Agent-Windows-x64.zip'

if (Test-Path $salida) { Remove-Item -LiteralPath $salida -Recurse -Force }
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
New-Item -ItemType Directory -Path $app -Force | Out-Null

dotnet publish (Join-Path $raiz 'ARES.Agent\ARES.Agent.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $app

if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar ARES Agent. Código de salida: $LASTEXITCODE"
}

Copy-Item (Join-Path $raiz 'ARES.Agent\Installer\*') $salida -Force
Get-ChildItem -Path $salida -Filter '*.bat' | ForEach-Object {
    $contenido = [IO.File]::ReadAllText($_.FullName) -replace "`r?`n", "`r`n"
    [IO.File]::WriteAllText($_.FullName, $contenido, [Text.UTF8Encoding]::new($false))
}
Compress-Archive -Path (Join-Path $salida '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Paquete creado: $zip"
