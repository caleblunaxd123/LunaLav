[CmdletBinding()]
param(
    [string]$SqlServer = "localhost\SQLEXPRESS",
    [string]$Database = "LunaLav"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$maintenanceSql = Join-Path $root "backend\db\maintenance\restablecer_demo_lunalav.sql"
$seedFiles = @(
    (Join-Path $root "backend\db\scripts\064_datos_demo_lunalav.sql"),
    (Join-Path $root "backend\db\scripts\065_comprobantes_demo_simulados.sql"),
    (Join-Path $root "backend\db\scripts\066_configuracion_facturacion_demo.sql")
)
$stateRoot = Join-Path $env:LOCALAPPDATA "LunaLav\demo"
$fotosRoot = Join-Path $stateRoot "fotos"
$logRoot = Join-Path $stateRoot "logs"

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw "No se encontró sqlcmd en PATH." }
if ($Database -notmatch '^[A-Za-z0-9_]+$') { throw "Nombre de base de datos inválido." }
foreach ($file in @($maintenanceSql) + $seedFiles) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "No existe $file" }
}

New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$logFile = Join-Path $logRoot "restablecer-demo-$stamp.log"
Start-Transcript -Path $logFile -Append | Out-Null

try {
    # La carpeta se identifica por el Id del tenant demo, nunca por una ruta amplia.
    $demoId = @(& sqlcmd -S $SqlServer -E -b -C -d $Database -h -1 -W -Q "SET NOCOUNT ON; SELECT Id FROM dbo.Negocio WHERE Slug = 'demo';") |
        ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^\d+$' } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($demoId)) { throw "No se encontró el tenant demo; no se eliminó ningún archivo." }

    & sqlcmd -S $SqlServer -E -b -C -d $Database -i $maintenanceSql
    if ($LASTEXITCODE -ne 0) { throw "Falló la limpieza aislada de la demo." }

    foreach ($seedFile in $seedFiles) {
        & sqlcmd -S $SqlServer -E -b -C -d $Database -i $seedFile
        if ($LASTEXITCODE -ne 0) { throw "Falló la carga de datos ficticios: $(Split-Path $seedFile -Leaf)" }
    }

    $demoPhotos = Join-Path $fotosRoot $demoId
    if (Test-Path -LiteralPath $demoPhotos) {
        Remove-Item -LiteralPath $demoPhotos -Recurse -Force
        Write-Host "Fotos temporales eliminadas: $demoPhotos" -ForegroundColor DarkGray
    }

    Write-Host "Demo restablecida con datos ficticios. Registro: $logFile" -ForegroundColor Green
}
finally {
    Stop-Transcript | Out-Null
}
