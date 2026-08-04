param(
    [string]$ServerUrl = 'https://ares-3bic.onrender.com',
    [string]$ApiKey = 'CAMBIAR-ESTA-CLAVE',
    [string]$ManagedUser = '',
    [string]$InstallerAdminUser = $env:USERNAME,
    [switch]$ProvisionStandardUser,
    [switch]$NonInteractiveProvisioning,
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
$tokenSolicitud = [Guid]::NewGuid().ToString('N')
$configuracionAnterior = Join-Path $destino 'appsettings.json'
if (Test-Path $configuracionAnterior) {
    try {
        $anterior = Get-Content -LiteralPath $configuracionAnterior -Raw | ConvertFrom-Json
        if (-not [string]::IsNullOrWhiteSpace($anterior.RequestToken)) {
            $tokenSolicitud = [string]$anterior.RequestToken
        }
    } catch { }
}

if (-not (Test-Path (Join-Path $origen 'ARES.Agent.exe'))) {
    throw 'No se encontró app\ARES.Agent.exe. Usá el paquete de distribución completo.'
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $argumentosElevados = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'),
        '-ServerUrl', ('"' + $ServerUrl + '"'), '-ApiKey', ('"' + $ApiKey + '"'),
        '-ManagedUser', ('"' + $ManagedUser + '"'),
        '-InstallerAdminUser', ('"' + $InstallerAdminUser + '"'),
        '-LogPath', ('"' + $LogPath + '"')
    )
    if ($ProvisionStandardUser) { $argumentosElevados += '-ProvisionStandardUser' }
    if ($NonInteractiveProvisioning) { $argumentosElevados += '-NonInteractiveProvisioning' }
    $procesoElevado = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList $argumentosElevados
    exit $procesoElevado.ExitCode
}

if ($NonInteractiveProvisioning) {
    $apiKeySegura = [Environment]::GetEnvironmentVariable('ARES_SETUP_API_KEY')
    if ([string]::IsNullOrWhiteSpace($apiKeySegura)) { throw 'Falta la clave segura de ARES.' }
    $ApiKey = $apiKeySegura
    $apiKeySegura = $null
    [Environment]::SetEnvironmentVariable('ARES_SETUP_API_KEY', $null, 'Process')
}

function Read-ConfirmedPassword([string]$Prompt, [string]$EnvironmentVariable = '') {
    if ($NonInteractiveProvisioning) {
        $texto = [Environment]::GetEnvironmentVariable($EnvironmentVariable)
        if ([string]::IsNullOrWhiteSpace($texto)) { throw "Falta la credencial segura requerida: $EnvironmentVariable." }
        try { return ConvertTo-SecureString $texto -AsPlainText -Force }
        finally { $texto = $null; [Environment]::SetEnvironmentVariable($EnvironmentVariable, $null, 'Process') }
    }
    $primera = Read-Host $Prompt -AsSecureString
    $segunda = Read-Host 'Repeti la contrasena para confirmar' -AsSecureString
    $texto1 = [PSCredential]::new('ARES', $primera).GetNetworkCredential().Password
    $texto2 = [PSCredential]::new('ARES', $segunda).GetNetworkCredential().Password
    if ([string]::IsNullOrWhiteSpace($texto1)) { throw 'La contrasena no puede estar vacia.' }
    if ($texto1 -cne $texto2) { throw 'Las contrasenas ingresadas no coinciden.' }
    $texto1 = $null
    $texto2 = $null
    return $primera
}

$usuarioAdministrado = $ManagedUser.Trim()
if ($ProvisionStandardUser) {
    $cuentaAdmin = Get-LocalUser -Name $InstallerAdminUser -ErrorAction SilentlyContinue
    if (-not $cuentaAdmin) {
        throw "La cuenta '$InstallerAdminUser' no es local. Ejecuta el instalador desde la cuenta administradora local que queres conservar."
    }
    $esAdmin = Get-LocalGroupMember -SID 'S-1-5-32-544' -ErrorAction Stop |
        Where-Object { $_.SID -eq $cuentaAdmin.SID }
    if (-not $esAdmin) { throw "La cuenta '$InstallerAdminUser' no pertenece al grupo Administradores." }

    if ([string]::IsNullOrWhiteSpace($usuarioAdministrado)) {
        $usuarioAdministrado = (Read-Host 'Nombre para la nueva cuenta del empleado (ejemplo: Empleado)').Trim()
    }
    if ($usuarioAdministrado -notmatch '^[^\\/\[\]:;|=,+*?<>@"]{1,20}$' -or $usuarioAdministrado.EndsWith('.')) {
        throw 'El nombre de la cuenta del empleado no es valido o supera los 20 caracteres.'
    }
    if ($usuarioAdministrado -ieq $InstallerAdminUser) {
        throw 'La cuenta del empleado debe ser diferente de la cuenta administradora.'
    }

    $cuentaEmpleado = Get-LocalUser -Name $usuarioAdministrado -ErrorAction SilentlyContinue
    if (-not $cuentaEmpleado) {
        Write-Host "Creando la cuenta estandar '$usuarioAdministrado'..."
        $claveEmpleado = Read-ConfirmedPassword 'Contrasena inicial para el empleado' 'ARES_SETUP_EMPLOYEE_PASSWORD'
        $cuentaEmpleado = New-LocalUser -Name $usuarioAdministrado -Password $claveEmpleado `
            -FullName $usuarioAdministrado -Description 'Cuenta estandar administrada por ARES' `
            -AccountNeverExpires -UserMayNotChangePassword:$false
        $grupoUsuarios = Get-LocalGroup -SID 'S-1-5-32-545'
        Add-LocalGroupMember -Group $grupoUsuarios -Member $cuentaEmpleado -ErrorAction SilentlyContinue
        $claveEmpleado.Dispose()
    } else {
        Write-Host "La cuenta '$usuarioAdministrado' ya existe; se conservara su contrasena actual."
        Enable-LocalUser -Name $usuarioAdministrado
    }

    $empleadoEsAdmin = Get-LocalGroupMember -SID 'S-1-5-32-544' -ErrorAction Stop |
        Where-Object { $_.SID -eq $cuentaEmpleado.SID }
    if ($empleadoEsAdmin) {
        throw "La cuenta '$usuarioAdministrado' ya existe pero es administradora. No se modifico la contrasena de '$InstallerAdminUser'."
    }

    $claveAdmin = Read-ConfirmedPassword "Nueva contrasena privada para el administrador '$InstallerAdminUser'" 'ARES_SETUP_ADMIN_PASSWORD'
    Set-LocalUser -Name $InstallerAdminUser -Password $claveAdmin
    $claveAdmin.Dispose()
    Write-Host 'Cuenta estandar creada y cuenta administradora protegida.'
}
if ([string]::IsNullOrWhiteSpace($usuarioAdministrado)) {
    throw 'No se pudo identificar la cuenta del empleado.'
}
$miembroAdministradores = Get-LocalGroupMember -SID 'S-1-5-32-544' -ErrorAction Stop |
    Where-Object { $_.SID -eq (Get-LocalUser -Name $usuarioAdministrado -ErrorAction Stop).SID }
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
    RequestToken = $tokenSolicitud
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
try {
    Start-ScheduledTask -TaskName $nombreTarea -ErrorAction Stop
    Start-Sleep -Seconds 2
} catch {
    Write-Host "ARES Agent visible se iniciara cuando '$usuarioAdministrado' ingrese a Windows."
}
Start-ScheduledTask -TaskName $nombreTareaServicio
$fondoGenerado = Join-Path $env:ProgramData 'ARES\lockscreen.png'
for ($intento = 0; $intento -lt 10 -and -not (Test-Path $fondoGenerado); $intento++) {
    Start-Sleep -Seconds 1
}
if (-not (Test-Path $fondoGenerado)) {
    throw 'ARES Agent se instaló, pero no pudo generar el fondo de bloqueo. Verificá que app\Assets\lockscreen-template-v3.png exista en el paquete.'
}
Set-Content -LiteralPath $LogPath -Value "Instalación completada correctamente el $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')." -Encoding UTF8

Add-Type -AssemblyName PresentationFramework
if (-not $NonInteractiveProvisioning) {
    [System.Windows.MessageBox]::Show('ARES Agent se instaló correctamente. El escudo aparecerá junto al reloj de Windows.', 'ARES Agent') | Out-Null
}
