[CmdletBinding()]
param(
    [string]$SqlServer = "localhost\SQLEXPRESS",
    [string]$Database = "LunaLav",
    [ValidateRange(1, 90)]
    [int]$RetenerDias = 14
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw "No se encontró sqlcmd en PATH."
}
if ($Database -notmatch '^[A-Za-z0-9_]+$') {
    throw "Nombre de base de datos inválido."
}

$pathQuery = "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000));"
$backupRoot = @(& sqlcmd -S $SqlServer -E -b -C -h -1 -W -Q $pathQuery) |
    ForEach-Object { $_.Trim() } | Where-Object { $_ } | Select-Object -First 1
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($backupRoot)) {
    throw "No se pudo obtener el directorio de respaldos de SQL Server."
}

$defaultRoot = [System.IO.Path]::GetFullPath($backupRoot)
if (-not [System.IO.Path]::IsPathRooted($defaultRoot)) {
    throw "El directorio de respaldo no es válido: $defaultRoot"
}
$resolvedRoot = Join-Path $defaultRoot $Database
$sqlRoot = $resolvedRoot.Replace("'", "''")
& sqlcmd -S $SqlServer -E -b -C -Q "EXEC master.dbo.xp_create_subdir N'$sqlRoot';"
if ($LASTEXITCODE -ne 0) { throw "SQL Server no pudo preparar su directorio exclusivo de respaldos." }

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupFile = Join-Path $resolvedRoot "${Database}_${timestamp}.bak"
$sqlPath = $backupFile.Replace("'", "''")
$backupSql = "BACKUP DATABASE [$Database] TO DISK = N'$sqlPath' WITH COPY_ONLY, INIT, CHECKSUM, STATS = 10; RESTORE VERIFYONLY FROM DISK = N'$sqlPath' WITH CHECKSUM;"
& sqlcmd -S $SqlServer -E -b -C -Q $backupSql
if ($LASTEXITCODE -ne 0) {
    throw "El respaldo no pudo completarse o verificarse."
}

$cutoff = (Get-Date).AddDays(-$RetenerDias).ToString("yyyy-MM-ddTHH:mm:ss")
$rotationSql = "EXEC master.dbo.xp_delete_file 0, N'$sqlRoot', N'bak', N'$cutoff', 0;"
& sqlcmd -S $SqlServer -E -b -C -Q $rotationSql
if ($LASTEXITCODE -ne 0) { throw "El respaldo se creó, pero no se pudo completar la rotación segura." }

$stateRoot = Join-Path $env:LOCALAPPDATA "LunaLav\demo"
$localBackupRoot = Join-Path $env:LOCALAPPDATA "LunaLav\respaldo"
New-Item -ItemType Directory -Force -Path $localBackupRoot | Out-Null

# Base y archivos se respaldan por separado: SQL Server verifica el .bak y este
# archivo comprimido conserva fotos, claves de protección y configuración local.
$stateItems = @(Get-ChildItem -LiteralPath $stateRoot -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne 'logs' })
if ($stateItems.Count -gt 0) {
    $stateArchive = Join-Path $localBackupRoot "LunaLav_estado_$timestamp.zip"
    Compress-Archive -LiteralPath $stateItems.FullName -DestinationPath $stateArchive -CompressionLevel Optimal -Force
    if (-not (Test-Path -LiteralPath $stateArchive) -or (Get-Item -LiteralPath $stateArchive).Length -lt 1) {
        throw "No se pudo verificar el respaldo de archivos locales."
    }

    Get-ChildItem -LiteralPath $localBackupRoot -Filter 'LunaLav_estado_*.zip' -File |
        Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-$RetenerDias) } |
        Remove-Item -Force
    Write-Host "Archivos locales respaldados: $stateArchive" -ForegroundColor Green
}

Write-Host "Respaldo verificado por SQL Server: $backupFile" -ForegroundColor Green
Write-Host "Retención: $RetenerDias días" -ForegroundColor DarkGray
