[CmdletBinding()]
param(
    [string]$SqlServer = "localhost\SQLEXPRESS",
    [ValidateRange(1024, 65535)]
    [int]$Port = 5005
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$frontend = Join-Path $root "frontend"
$apiProject = Join-Path $root "backend\src\Lavanderia.Api\Lavanderia.Api.csproj"
$apiWwwroot = Join-Path $root "backend\src\Lavanderia.Api\wwwroot"
$distBrowser = Join-Path $frontend "dist\lunalav\browser"
$buildRoot = Join-Path $root ".demo-build"
$publishDir = Join-Path $buildRoot "publish"
$localState = Join-Path $env:LOCALAPPDATA "LunaLav\demo"
$apiOut = Join-Path $buildRoot "api.out.log"
$apiErr = Join-Path $buildRoot "api.err.log"

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "No se encontró '$Name' en PATH."
    }
}

function Get-OrCreateSecret([string]$Name, [int]$Bytes = 48) {
    New-Item -ItemType Directory -Path $localState -Force | Out-Null
    $path = Join-Path $localState $Name
    if (-not (Test-Path -LiteralPath $path)) {
        $buffer = New-Object byte[] $Bytes
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
        [Convert]::ToBase64String($buffer) | Set-Content -LiteralPath $path -Encoding ascii -NoNewline
    }
    return (Get-Content -LiteralPath $path -Raw).Trim()
}

function Wait-Ready([string]$Url, [int]$Seconds = 90) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return }
        } catch {
            Start-Sleep -Seconds 2
        }
    } while ((Get-Date) -lt $deadline)
    throw "La demo no respondió a tiempo: $Url"
}

function Remove-DirectoryWithRetry([string]$Path, [int]$Attempts = 8) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        } catch {
            if ($attempt -eq $Attempts) { throw }
            Start-Sleep -Milliseconds 750
        }
    }
}

Assert-Command "dotnet"
Assert-Command "node"
Assert-Command "npm.cmd"
Assert-Command "sqlcmd"

Write-Host "[1/5] Preparando la base aislada LunaLav..." -ForegroundColor Cyan
$migraciones = Get-ChildItem (Join-Path $root "backend\db\scripts") -Filter "*.sql" |
    Sort-Object Name

# El primer script crea la base. Los demás se registran para no volver a aplicar
# migraciones históricas que, después de cambios de esquema posteriores, ya no
# necesariamente son repetibles.
$primeraMigracion = $migraciones | Where-Object Name -eq "001_schema.sql" | Select-Object -First 1
& sqlcmd -S $SqlServer -E -b -C -I -d master -i $primeraMigracion.FullName
if ($LASTEXITCODE -ne 0) { throw "Falló la migración $($primeraMigracion.Name)." }

$crearRegistro = @"
IF OBJECT_ID(N'dbo.SchemaMigration', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SchemaMigration (
        Nombre NVARCHAR(200) NOT NULL PRIMARY KEY,
        AplicadaEn DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;
"@
& sqlcmd -S $SqlServer -E -b -C -I -d LunaLav -Q $crearRegistro
if ($LASTEXITCODE -ne 0) { throw "No se pudo preparar el registro de migraciones." }

$aplicadas = @(& sqlcmd -S $SqlServer -E -C -I -d LunaLav -h -1 -W -Q "SET NOCOUNT ON; SELECT Nombre FROM dbo.SchemaMigration;") |
    ForEach-Object { $_.Trim() } | Where-Object { $_ }

# Compatibilidad con una base creada antes de existir SchemaMigration: si ya tiene
# las columnas de las migraciones 061 y 062, registrar el historial sin repetirlo.
$esquemaActual = (@(& sqlcmd -S $SqlServer -E -C -I -d LunaLav -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN COL_LENGTH(N'dbo.Insumo', N'FechaIngreso') IS NOT NULL AND COL_LENGTH(N'dbo.Cliente', N'Celular') >= 40 THEN 1 ELSE 0 END;") | Select-Object -First 1).Trim()
if ($aplicadas.Count -eq 0 -and $esquemaActual -eq "1") {
    foreach ($sql in $migraciones) {
        $nombreSeguro = $sql.Name.Replace("'", "''")
        & sqlcmd -S $SqlServer -E -b -C -I -d LunaLav -Q "IF NOT EXISTS (SELECT 1 FROM dbo.SchemaMigration WHERE Nombre = N'$nombreSeguro') INSERT dbo.SchemaMigration (Nombre) VALUES (N'$nombreSeguro');"
        if ($LASTEXITCODE -ne 0) { throw "No se pudo registrar $($sql.Name)." }
    }
    $aplicadas = @($migraciones.Name)
}

if ($aplicadas -notcontains $primeraMigracion.Name) {
    & sqlcmd -S $SqlServer -E -b -C -I -d LunaLav -Q "INSERT dbo.SchemaMigration (Nombre) VALUES (N'001_schema.sql');"
    if ($LASTEXITCODE -ne 0) { throw "No se pudo registrar 001_schema.sql." }
    $aplicadas += $primeraMigracion.Name
}

foreach ($sql in $migraciones | Where-Object Name -ne "001_schema.sql") {
    if ($aplicadas -contains $sql.Name) { continue }
    & sqlcmd -S $SqlServer -E -b -C -I -d LunaLav -i $sql.FullName
    if ($LASTEXITCODE -ne 0) { throw "Falló la migración $($sql.Name)." }
    $nombreSeguro = $sql.Name.Replace("'", "''")
    & sqlcmd -S $SqlServer -E -b -C -I -d LunaLav -Q "INSERT dbo.SchemaMigration (Nombre) VALUES (N'$nombreSeguro');"
    if ($LASTEXITCODE -ne 0) { throw "No se pudo registrar $($sql.Name)." }
}

Write-Host "[2/5] Compilando LunaLav..." -ForegroundColor Cyan
Push-Location $frontend
try {
    if (-not (Test-Path -LiteralPath (Join-Path $frontend "node_modules"))) {
        & npm.cmd ci
        if ($LASTEXITCODE -ne 0) { throw "npm ci falló." }
    }
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw "La compilación de Angular falló." }
} finally {
    Pop-Location
}

Write-Host "[3/5] Publicando API y frontend..." -ForegroundColor Cyan
if (Test-Path -LiteralPath $apiWwwroot) {
    Remove-DirectoryWithRetry $apiWwwroot
}
New-Item -ItemType Directory -Path $apiWwwroot -Force | Out-Null
Copy-Item -Path (Join-Path $distBrowser "*") -Destination $apiWwwroot -Recurse -Force

Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*$publishDir*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

if (Test-Path -LiteralPath $publishDir) {
    Remove-DirectoryWithRetry $publishDir
}
& dotnet publish $apiProject -c Release -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló." }

Write-Host "[4/5] Iniciando demo aislada..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $localState -Force | Out-Null
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$env:ConnectionStrings__Sql = "Server=$SqlServer;Database=LunaLav;Trusted_Connection=True;TrustServerCertificate=True;"
$env:Jwt__SecretKey = Get-OrCreateSecret "jwt-secret.txt" 64
$env:SeedAdmin__Password = Get-OrCreateSecret "demo-admin.txt" 24
$env:SeedAdmin__NombreNegocio = "Lavandería Demo LunaLav"
$env:SeedAdmin__Slug = "demo"
$env:SeedPropietario__Password = Get-OrCreateSecret "propietario.txt" 32
$env:DataProtection__KeysPath = Join-Path $localState "keys"
$env:Fotos__Directorio = Join-Path $localState "fotos"

New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
Remove-Item -LiteralPath $apiOut, $apiErr -Force -ErrorAction SilentlyContinue
$apiDll = Join-Path $publishDir "Lavanderia.Api.dll"
$process = Start-Process -FilePath "dotnet" -ArgumentList @($apiDll) -WorkingDirectory $publishDir `
    -RedirectStandardOutput $apiOut -RedirectStandardError $apiErr -WindowStyle Hidden -PassThru
Wait-Ready "http://127.0.0.1:$Port/health/ready"

Write-Host "[5/5] Demo lista" -ForegroundColor Green
Write-Host "Local:  http://127.0.0.1:$Port/demo/login"
Write-Host "Pública: https://demo.lunalav.pe/demo/login (cuando DNS esté activo)"
Write-Host "Usuario: admin"
Write-Host "Clave:   guardada localmente en $(Join-Path $localState 'demo-admin.txt')"
Write-Host "PID:     $($process.Id)"
