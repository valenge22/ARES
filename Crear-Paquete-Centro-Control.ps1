$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$salida = Join-Path $raiz 'distribucion\ARES-Centro-Control-Windows-x64'
$app = Join-Path $salida 'app'
$zip = Join-Path $raiz 'distribucion\ARES-Centro-Control-Windows-x64.zip'
$setup = Join-Path $raiz 'distribucion\ARES-Centro-Control-Setup.exe'

if (Test-Path $salida) { Remove-Item -LiteralPath $salida -Recurse -Force }
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
if (Test-Path $setup) { Remove-Item -LiteralPath $setup -Force }
New-Item -ItemType Directory -Path $app -Force | Out-Null

dotnet publish (Join-Path $raiz 'AdministracionEmpleados\AdministracionEmpleados.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $app

if ($LASTEXITCODE -ne 0) { throw "No se pudo compilar el Centro de Control. Código: $LASTEXITCODE" }

# El ZIP conserva la carpeta app/ porque es el formato usado por la actualización remota.
Compress-Archive -Path $app -DestinationPath $zip -CompressionLevel Optimal

$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) }
$iscc = $isccCandidates | Select-Object -First 1
if (-not $iscc) { throw 'No se encontró Inno Setup 6 (ISCC.exe).' }

$iss = Join-Path $raiz 'AdministracionEmpleados\Installer\ARES-Control-Center.iss'
& $iscc "/DAppSource=$app" "/DOutputDir=$(Join-Path $raiz 'distribucion')" '/DAppVersion=1.3.1' $iss
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $setup)) { throw 'No se pudo generar el instalador EXE.' }

Write-Host "Instalador creado: $setup"
Write-Host "Paquete remoto creado: $zip"
