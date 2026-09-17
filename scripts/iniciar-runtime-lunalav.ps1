[CmdletBinding()]
param(
    [string]$SqlServer = "localhost\SQLEXPRESS",
    [int[]]$Ports = @(5004, 5005)
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$publishDir = Join-Path $root ".demo-build\publish"
$apiDll = Join-Path $publishDir "Lavanderia.Api.dll"
$buildRoot = Join-Path $root ".demo-build"
$localState = Join-Path $env:LOCALAPPDATA "LunaLav\demo"

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

function Wait-Ready([string]$Url, [int]$Seconds = 60) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return }
        } catch {
            Start-Sleep -Seconds 2
        }
    } while ((Get-Date) -lt $deadline)
    throw "LunaLav no respondió a tiempo: $Url"
}

if (-not (Test-Path -LiteralPath $apiDll)) {
    throw "No existe la publicación de LunaLav. Ejecuta primero scripts\iniciar-demo-lunalav.ps1."
}

New-Item -ItemType Directory -Path $buildRoot, $localState -Force | Out-Null

Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*$apiDll*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ConnectionStrings__Sql = "Server=$SqlServer;Database=LunaLav;Trusted_Connection=True;TrustServerCertificate=True;"
$env:Jwt__SecretKey = Get-OrCreateSecret "jwt-secret.txt" 64
$env:SeedAdmin__Password = Get-OrCreateSecret "demo-admin.txt" 24
$env:SeedAdmin__NombreNegocio = "Lavandería Demo LunaLav"
$env:SeedAdmin__Slug = "demo"
$env:SeedPropietario__Password = Get-OrCreateSecret "propietario.txt" 32
$env:DataProtection__KeysPath = Join-Path $localState "keys"
$env:Fotos__Directorio = Join-Path $localState "fotos"

foreach ($port in $Ports | Sort-Object -Unique) {
    if ($port -lt 1024 -or $port -gt 65535) { throw "Puerto inválido: $port" }
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$port"
    $outLog = Join-Path $buildRoot "api-$port.out.log"
    $errLog = Join-Path $buildRoot "api-$port.err.log"
    Remove-Item -LiteralPath $outLog, $errLog -Force -ErrorAction SilentlyContinue
    $process = Start-Process -FilePath "dotnet" -ArgumentList @($apiDll) -WorkingDirectory $publishDir `
        -RedirectStandardOutput $outLog -RedirectStandardError $errLog -WindowStyle Hidden -PassThru
    Wait-Ready "http://127.0.0.1:$port/health/ready"
    Write-Host "LunaLav activo en http://127.0.0.1:$port (PID $($process.Id))" -ForegroundColor Green
}
