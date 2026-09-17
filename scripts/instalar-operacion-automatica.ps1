[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtimeScript = Join-Path $root "scripts\iniciar-runtime-lunalav.ps1"
$backupScript = Join-Path $root "scripts\respaldo-lunalav.ps1"
$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name

foreach ($script in @($runtimeScript, $backupScript)) {
    if (-not (Test-Path -LiteralPath $script -PathType Leaf)) { throw "No existe $script" }
}

$principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)

$runtimeArgs = "-NoProfile -ExecutionPolicy Bypass -File `"$runtimeScript`""
$runtimeAction = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $runtimeArgs -WorkingDirectory $root
$runtimeTrigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser
Register-ScheduledTask -TaskName "LunaLav - Servicios" -Action $runtimeAction -Trigger $runtimeTrigger `
    -Principal $principal -Settings $settings -Description "Inicia la web, demo y aplicación de LunaLav al ingresar a Windows." -Force | Out-Null

$backupArgs = "-NoProfile -ExecutionPolicy Bypass -File `"$backupScript`""
$backupAction = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $backupArgs -WorkingDirectory $root
$backupTrigger = New-ScheduledTaskTrigger -Daily -At "03:00"
Register-ScheduledTask -TaskName "LunaLav - Respaldo diario" -Action $backupAction -Trigger $backupTrigger `
    -Principal $principal -Settings $settings -Description "Crea y verifica un respaldo diario de la base LunaLav." -Force | Out-Null

Write-Host "Inicio automático y respaldo diario configurados para $currentUser." -ForegroundColor Green
