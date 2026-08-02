$ErrorActionPreference = 'Stop'
$nombreTarea = 'ARES Agent'
$destino = Join-Path $env:ProgramFiles 'ARES Agent'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"')
    )
    exit
}

Get-Process -Name 'ARES.Agent' -ErrorAction SilentlyContinue | Stop-Process -Force
Unregister-ScheduledTask -TaskName $nombreTarea -Confirm:$false -ErrorAction SilentlyContinue
if (Test-Path $destino) { Remove-Item -LiteralPath $destino -Recurse -Force }

Add-Type -AssemblyName PresentationFramework
[System.Windows.MessageBox]::Show('ARES Agent fue desinstalado.', 'ARES Agent') | Out-Null
