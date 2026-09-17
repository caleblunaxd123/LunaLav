param(
    [string]$BaseUrl = "http://127.0.0.1:5000/api",
    [string]$EmpresaSlug = "demo",
    [string]$Usuario = "admin",
    [string]$Password = "admin123",
    [int]$SedeId = 1
)

$ErrorActionPreference = "Stop"

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "E2E facturacion: $Message" }
}

function Expect-Status([scriptblock]$Action, [int]$StatusCode, [string]$Message) {
    try {
        & $Action | Out-Null
        throw "E2E facturacion: $Message (la solicitud no fallo)"
    }
    catch {
        $actual = [int]$_.Exception.Response.StatusCode
        if ($actual -ne $StatusCode) {
            throw "E2E facturacion: $Message (esperado $StatusCode, recibido $actual)"
        }
    }
}

$loginBody = @{ usuario = $Usuario; password = $Password; empresaSlug = $EmpresaSlug } | ConvertTo-Json
$login = Invoke-RestMethod -Method Post -Uri "$BaseUrl/auth/login" -ContentType "application/json" -Body $loginBody
Assert-True (-not [string]::IsNullOrWhiteSpace($login.accessToken)) "el login no devolvio accessToken"

$headers = @{ Authorization = "Bearer $($login.accessToken)" }
$sedeBody = @{ sedeId = $SedeId; refreshToken = $login.refreshToken } | ConvertTo-Json
$sesion = Invoke-RestMethod -Method Post -Uri "$BaseUrl/auth/seleccionar-sede" -Headers $headers -ContentType "application/json" -Body $sedeBody
$headers.Authorization = "Bearer $($sesion.accessToken)"

$config = Invoke-RestMethod -Uri "$BaseUrl/facturacion/configuracion" -Headers $headers
Assert-True (@("APISUNAT", "SUNAT_DIRECTO") -contains $config.proveedor) "proveedor de emision desconocido"

$listado = Invoke-RestMethod -Uri "$BaseUrl/facturacion/comprobantes?pagina=1&tamanoPagina=15&estado=SIMULADO" -Headers $headers
Assert-True ($null -ne $listado.total) "la bandeja no devolvio paginacion"

if ($listado.items.Count -gt 0) {
    $id = [int]$listado.items[0].id
    $detalle = Invoke-RestMethod -Uri "$BaseUrl/facturacion/comprobantes/$id" -Headers $headers
    Assert-True ($detalle.detalles.Count -gt 0) "el detalle no contiene el snapshot de lineas"
    Assert-True ($detalle.esSimulado -eq $true) "los datos demo no estan marcados como simulados"

    $pdfPath = Join-Path $env:TEMP "lunalav-e2e-comprobante.pdf"
    Invoke-WebRequest -Uri "$BaseUrl/facturacion/comprobantes/$id/pdf" -Headers $headers -OutFile $pdfPath
    Assert-True ((Get-Item $pdfPath).Length -gt 1000) "el PDF generado esta vacio"
    Remove-Item -LiteralPath $pdfPath -Force

    Expect-Status { Invoke-RestMethod -Uri "$BaseUrl/facturacion/comprobantes/$id/xml" -Headers $headers } 404 "un simulado no debe exponer XML"
}

Expect-Status { Invoke-RestMethod -Uri "$BaseUrl/facturacion/comprobantes?tipo=NOTA" -Headers $headers } 400 "el filtro fiscal invalido debe rechazarse"
Expect-Status {
    Invoke-RestMethod -Method Post -Uri "$BaseUrl/pedidos/0/comprobante" -Headers $headers -ContentType "application/json" -Body '{"tipo":"NOTA"}'
} 400 "el tipo de comprobante invalido debe rechazarse"

Write-Host "OK: recorrido E2E de facturacion electronica."
