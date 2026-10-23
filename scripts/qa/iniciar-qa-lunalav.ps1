<#
.SYNOPSIS
  Levanta un entorno QA/staging AISLADO de LunaLav en http://127.0.0.1:5099.

.DESCRIPTION
  - Base de datos: LunaLavQA (nunca LunaLav). El script se niega a arrancar si la cadena de
    conexión no apunta a LunaLavQA.
  - No copia appsettings.Local.json (secretos de producción): sin Gmail, sin Culqi, sin SUNAT.
  - Los códigos de verificación del alta se escriben en el buzón de pruebas
    (%LOCALAPPDATA%\LunaLav\qa\buzon), nunca se envían por correo.
  - Los recordatorios de pago quedan desactivados.

  Crear la base QA (una vez): ver docs del repo AGENTEIA (docs/lunalav/QA_STAGING.md); los
  scripts de backend/db/scripts contienen "USE LunaLav" y NO deben ejecutarse sin reescribir.
#>
param(
    [int]$Port = 5099,
    [string]$SqlServer = ".\SQLEXPRESS",
    [switch]$SinCompilar
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$frontend = Join-Path $root "frontend"
$apiProject = Join-Path $root "backend\src\Lavanderia.Api\Lavanderia.Api.csproj"
$qaState = Join-Path $env:LOCALAPPDATA "LunaLav\qa"
$publishDir = Join-Path $qaState "publish"
$connection = "Server=$SqlServer;Database=LunaLavQA;Trusted_Connection=True;TrustServerCertificate=True;"

if ($connection -notmatch 'Database=LunaLavQA;') { throw "La conexión QA no apunta a LunaLavQA. Abortado." }
$existe = & sqlcmd -S $SqlServer -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID('LunaLavQA') IS NULL THEN 0 ELSE 1 END"
if (($existe | Select-Object -Last 1).Trim() -ne '1') { throw "No existe la base LunaLavQA. Créala primero (ver QA_STAGING.md)." }

if (-not $SinCompilar) {
    Push-Location $frontend
    try {
        & cmd.exe /c "npm.cmd run build 2>&1"
        if ($LASTEXITCODE -ne 0) { throw "npm run build falló." }
    } finally { Pop-Location }
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
    & dotnet publish $apiProject -c Release -o $publishDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló." }
    $wwwroot = Join-Path $publishDir "wwwroot"
    New-Item -ItemType Directory -Force $wwwroot | Out-Null
    Copy-Item -Path (Join-Path $frontend "dist\lunalav\browser\*") -Destination $wwwroot -Recurse -Force
}
if (Test-Path (Join-Path $publishDir "appsettings.Local.json")) { throw "appsettings.Local.json no debe existir en QA." }

# Credenciales de QA: se generan una vez y se reutilizan (la base QA ya sembró los usuarios con ellas).
$secretos = Join-Path $qaState "secretos"
New-Item -ItemType Directory -Force (Join-Path $qaState "buzon"), (Join-Path $qaState "keys"), (Join-Path $qaState "fotos"), $secretos | Out-Null
function Get-OrCreateSecret([string]$Nombre, [int]$Bytes = 24) {
    $ruta = Join-Path $secretos $Nombre
    if (-not (Test-Path -LiteralPath $ruta)) {
        $buffer = New-Object byte[] $Bytes
        [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
        [Convert]::ToBase64String($buffer) | Set-Content -LiteralPath $ruta -Encoding ascii -NoNewline
    }
    return (Get-Content -LiteralPath $ruta -Raw).Trim()
}
$env:ASPNETCORE_ENVIRONMENT = "QA"
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$env:ConnectionStrings__Sql = $connection
$env:Jwt__SecretKey = Get-OrCreateSecret "jwt.txt" 48
$env:SeedAdmin__Password = Get-OrCreateSecret "admin-demo.txt" 18
$env:SeedPropietario__Password = Get-OrCreateSecret "propietario.txt" 18
$env:DataProtection__KeysPath = Join-Path $qaState "keys"
$env:Fotos__Directorio = Join-Path $qaState "fotos"
$env:Registro__BuzonPruebas = Join-Path $qaState "buzon"
$env:RecordatorioPago__Habilitado = "false"
$env:Culqi__PublicKey = ""
$env:Culqi__SecretKey = ""
$env:Gmail__ClientId = ""
$env:Gmail__ClientSecret = ""

Write-Host "LunaLav QA en http://127.0.0.1:$Port (base LunaLavQA, buzón $($env:Registro__BuzonPruebas))"
Write-Host "Credenciales QA (usuarios admin y propietario): $secretos"
# La raíz de contenido (wwwroot, appsettings) es la carpeta publicada, no la carpeta desde donde se lanza.
Set-Location $publishDir
& dotnet (Join-Path $publishDir "Lavanderia.Api.dll")
