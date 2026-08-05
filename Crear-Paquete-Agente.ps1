param([string]$UninstallPassword = $env:ARES_UNINSTALL_PASSWORD)

$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$salida = Join-Path $raiz 'distribucion\ARES-Agent-Windows-x64'
$app = Join-Path $salida 'app'
$zip = Join-Path $raiz 'distribucion\ARES-Agent-Windows-x64.zip'
$setup = Join-Path $raiz 'distribucion\ARES-Agent-Setup.exe'
$setupUi = Join-Path $raiz 'distribucion\ARES-Agent-Setup-UI'
$proteccionAnterior = Join-Path $salida 'uninstall-protection.json'
$contenidoProteccionAnterior = if (Test-Path $proteccionAnterior) {
    [IO.File]::ReadAllBytes($proteccionAnterior)
} else { $null }

if (Test-Path $salida) { Remove-Item -LiteralPath $salida -Recurse -Force }
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
if (Test-Path $setup) { Remove-Item -LiteralPath $setup -Force }
if (Test-Path $setupUi) { Remove-Item -LiteralPath $setupUi -Recurse -Force }
New-Item -ItemType Directory -Path $app -Force | Out-Null
New-Item -ItemType Directory -Path $setupUi -Force | Out-Null

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

dotnet publish (Join-Path $raiz 'ARES.Agent.Setup\ARES.Agent.Setup.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $setupUi
if ($LASTEXITCODE -ne 0) { throw "No se pudo compilar la interfaz grafica del instalador. Codigo: $LASTEXITCODE" }

Copy-Item (Join-Path $raiz 'ARES.Agent\Installer\*') $salida -Force

if ([string]::IsNullOrWhiteSpace($UninstallPassword)) {
    if ($env:GITHUB_ACTIONS -eq 'true') {
        throw 'Falta el secreto ARES_UNINSTALL_PASSWORD en GitHub Actions.'
    }
    if ($null -ne $contenidoProteccionAnterior) {
        [IO.File]::WriteAllBytes((Join-Path $salida 'uninstall-protection.json'), $contenidoProteccionAnterior)
        Write-Host 'Se conservó la contraseña fija de desinstalación de la versión anterior.'
    } else {
        $segura = Read-Host 'Contraseña administrativa fija de desinstalación' -AsSecureString
        $puntero = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($segura)
        try { $UninstallPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($puntero) }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($puntero) }
    }
}

if (-not [string]::IsNullOrWhiteSpace($UninstallPassword) -and $UninstallPassword.Length -lt 8) {
    throw 'La contraseña de desinstalación debe tener al menos 8 caracteres.'
}

if (-not [string]::IsNullOrWhiteSpace($UninstallPassword)) {
    $iteraciones = 200000
    $sal = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($sal)
    $derivador = New-Object Security.Cryptography.Rfc2898DeriveBytes(
        $UninstallPassword, $sal, $iteraciones, [Security.Cryptography.HashAlgorithmName]::SHA256)
    $hash = $derivador.GetBytes(32)
    $derivador.Dispose()
    @{
        Salt = [Convert]::ToBase64String($sal)
        Hash = [Convert]::ToBase64String($hash)
        Iterations = $iteraciones
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $salida 'uninstall-protection.json') -Encoding UTF8
}
$UninstallPassword = $null

Get-ChildItem -Path $salida -Filter '*.bat' | ForEach-Object {
    $contenido = [IO.File]::ReadAllText($_.FullName) -replace "`r?`n", "`r`n"
    [IO.File]::WriteAllText($_.FullName, $contenido, [Text.UTF8Encoding]::new($false))
}
Compress-Archive -Path (Join-Path $salida '*') -DestinationPath $zip -CompressionLevel Optimal
$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path $_) }
$iscc = $isccCandidates | Select-Object -First 1
if (-not $iscc) { throw 'No se encontro Inno Setup 6 (ISCC.exe).' }

$iss = Join-Path $raiz 'ARES.Agent\Installer\ARES-Agent.iss'
& $iscc "/DPackageSource=$salida" "/DSetupUiSource=$setupUi" "/DOutputDir=$(Join-Path $raiz 'distribucion')" '/DAppVersion=1.7.2' $iss
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $setup)) { throw 'No se pudo generar el instalador EXE de ARES Agent.' }

Write-Host "Paquete creado: $zip"
Write-Host "Instalador creado: $setup"
