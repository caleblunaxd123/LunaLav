# Configura el arranque automático de LunaLav tras cualquier apagado/reinicio.
# EJECUTAR UNA SOLA VEZ COMO ADMINISTRADOR:
#   Clic derecho en PowerShell -> "Ejecutar como administrador", luego:
#   powershell -ExecutionPolicy Bypass -File "C:\Users\Caleb\Documents\GitHub\LunaLav\scripts\configurar-autoarranque.ps1"

#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$script = 'C:\Users\Caleb\Documents\GitHub\LunaLav\scripts\iniciar-runtime-lunalav.ps1'
$user   = "$env:USERDOMAIN\$env:USERNAME"
Write-Host ("Cuenta: " + $user) -ForegroundColor Cyan

# 1) Desactivar Fast Startup (conserva la hibernación). Sin esto, tras un APAGADO
#    el encendido es un "resume" que NO dispara las tareas de arranque/logon.
New-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' `
  -Name 'HiberbootEnabled' -Value 0 -PropertyType DWord -Force | Out-Null
Write-Host "[1/3] Fast Startup DESACTIVADO (arranque limpio en cada encendido)." -ForegroundColor Green

# 2) La tarea de servicios: arranca AL ENCENDER (boot) y AL INICIAR SESION,
#    y corre AUNQUE NADIE HAYA INICIADO SESION (S4U, sin guardar contraseña),
#    con 3 reintentos por si SQL aún no está listo.
$action    = New-ScheduledTaskAction -Execute 'powershell.exe' `
  -Argument ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}"' -f $script)
$tBoot     = New-ScheduledTaskTrigger -AtStartup; $tBoot.Delay = 'PT45S'
$tLogon    = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType S4U -RunLevel Limited
$settings  = New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 3 `
  -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit (New-TimeSpan -Hours 72) `
  -MultipleInstances IgnoreNew -AllowStartIfOnBatteries
Register-ScheduledTask -TaskName 'LunaLav - Servicios' -Action $action `
  -Trigger @($tBoot, $tLogon) -Principal $principal -Settings $settings -Force | Out-Null
Write-Host "[2/3] Tarea 'LunaLav - Servicios' -> arranque + logon, corre sin sesión." -ForegroundColor Green

# 3) Asegurar que los servicios base arranquen solos.
foreach ($s in 'cloudflared', 'MSSQL$SQLEXPRESS') {
  $svc = Get-Service -Name $s -ErrorAction SilentlyContinue
  if ($svc) { Set-Service -Name $s -StartupType Automatic; Write-Host ("      Servicio " + $s + " => Automatico") }
}
Write-Host "[3/3] cloudflared y SQL Server en arranque automático." -ForegroundColor Green

Write-Host "`nListo. Probando la tarea ahora..." -ForegroundColor Cyan
Start-ScheduledTask -TaskName 'LunaLav - Servicios'
Start-Sleep -Seconds 20
foreach ($p in 5004, 5005) {
  try { $c = (Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:$p/health/ready" -TimeoutSec 6).StatusCode }
  catch { $c = 'sin respuesta' }
  Write-Host ("  puerto $p -> $c")
}
Write-Host "`nSi ves 200/200, quedó configurado. Reinicia para confirmar el arranque solo." -ForegroundColor Green
