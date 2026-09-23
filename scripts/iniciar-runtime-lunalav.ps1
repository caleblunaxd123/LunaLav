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
$runtimeLog = Join-Path $buildRoot "runtime-supervisor.log"

function Write-RuntimeLog([string]$Message) {
    $line = "{0:u} {1}" -f (Get-Date), $Message
    Write-Host $Message
    Add-Content -LiteralPath $runtimeLog -Value $line -Encoding utf8
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

function Wait-Ready([string]$Url, [int]$Seconds = 60) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 5
            if ($response.StatusCode -eq 200) { return $true }
        } catch {
            Start-Sleep -Seconds 2
        }
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Wait-SqlReady([string]$Server, [int]$Seconds = 240) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    Write-RuntimeLog "Esperando SQL Server $Server..."
    do {
        & sqlcmd -S $Server -E -C -l 5 -d LunaLav -Q "SET NOCOUNT ON; SELECT 1;" *> $null
        if ($LASTEXITCODE -eq 0) {
            Write-RuntimeLog "SQL Server disponible."
            return $true
        }
        Start-Sleep -Seconds 5
    } while ((Get-Date) -lt $deadline)
    return $false
}

if (-not (Test-Path -LiteralPath $apiDll)) {
    throw "No existe la publicación de LunaLav. Ejecuta primero scripts\iniciar-demo-lunalav.ps1."
}

New-Item -ItemType Directory -Path $buildRoot, $localState -Force | Out-Null
Write-RuntimeLog "Inicio del supervisor LunaLav. Puertos solicitados: $($Ports -join ', ')."

if (-not (Wait-SqlReady -Server $SqlServer)) {
    Write-RuntimeLog "ERROR: SQL Server no respondió dentro del tiempo esperado."
    throw "SQL Server no está disponible después de 240 segundos."
}

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

$failedPorts = [System.Collections.Generic.List[int]]::new()
foreach ($port in $Ports | Sort-Object -Unique) {
    if ($port -lt 1024 -or $port -gt 65535) { throw "Puerto inválido: $port" }
    if (Wait-Ready "http://127.0.0.1:$port/health/ready" 3) {
        Write-RuntimeLog "Puerto $port ya estaba activo y saludable; se conserva la instancia."
        continue
    }
    $started = $false
    for ($attempt = 1; $attempt -le 3 -and -not $started; $attempt++) {
        $env:ASPNETCORE_URLS = "http://127.0.0.1:$port"
        $outLog = Join-Path $buildRoot "api-$port.out.log"
        $errLog = Join-Path $buildRoot "api-$port.err.log"
        Remove-Item -LiteralPath $outLog, $errLog -Force -ErrorAction SilentlyContinue
        Write-RuntimeLog "Iniciando puerto $port (intento $attempt de 3)..."
        $process = Start-Process -FilePath "dotnet" -ArgumentList @($apiDll) -WorkingDirectory $publishDir `
            -RedirectStandardOutput $outLog -RedirectStandardError $errLog -WindowStyle Hidden -PassThru
        $started = Wait-Ready "http://127.0.0.1:$port/health/ready" 75
        if ($started -and $process.HasExited) {
            Write-RuntimeLog "El proceso del puerto $port terminó antes de quedar estable."
            $started = $false
        }
        if (-not $started) {
            Write-RuntimeLog "Puerto $port no quedó listo en el intento $attempt."
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 5
        }
    }
    if ($started) {
        Write-RuntimeLog "LunaLav activo en http://127.0.0.1:$port (PID $($process.Id))."
    } else {
        $failedPorts.Add($port)
    }
}

if ($failedPorts.Count -gt 0) {
    Write-RuntimeLog "ERROR: no iniciaron los puertos $($failedPorts -join ', ')."
    throw "No se pudieron iniciar todos los puertos de LunaLav."
}
Write-RuntimeLog "Todos los servicios LunaLav están saludables."
