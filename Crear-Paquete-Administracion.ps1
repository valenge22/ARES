param([string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$version = ([xml](Get-Content (Join-Path $root "ARES.PlatformAdmin\ARES.PlatformAdmin.csproj"))).Project.PropertyGroup.Version
$publish = Join-Path $root "distribucion\ARES-Administracion-Windows-x64\app"
$output = Join-Path $root "distribucion"
dotnet publish (Join-Path $root "ARES.PlatformAdmin\ARES.PlatformAdmin.csproj") -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o $publish
$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) { $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $iscc)) { $iscc = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe" }
if (-not (Test-Path $iscc)) { throw "No se encontró Inno Setup." }
& $iscc "/DAppSource=$publish" "/DOutputDir=$output" "/DAppVersion=$version" (Join-Path $root "ARES.PlatformAdmin\Installer\ARES-Administracion.iss")
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath (Join-Path $output "ARES-Administracion-Windows-x64.zip") -Force
