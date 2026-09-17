param(
    [string]$BaseUrl = "http://localhost:5002",
    [string]$Usuario = "admin",
    [Parameter(Mandatory = $true)]
    [string]$Password,
    [switch]$AllowNonQaTarget
)

$ErrorActionPreference = "Stop"
if (-not $AllowNonQaTarget -and $BaseUrl -notmatch '^https?://(localhost|127\.0\.0\.1):5002/?$') {
    throw "Por seguridad, esta prueba solo se ejecuta contra localhost:5002. Usa -AllowNonQaTarget conscientemente."
}

$script:token = $null
$script:refreshToken = $null
$script:passed = 0
$script:failed = 0
$script:results = [System.Collections.Generic.List[object]]::new()
$suffix = [DateTimeOffset]::Now.ToUnixTimeMilliseconds().ToString()

function Invoke-QaRequest {
    param(
        [string]$Method,
        [string]$Path,
        $Body = $null,
        [string]$Token = $script:token
    )

    $headers = @{}
    if ($Token) { $headers.Authorization = "Bearer $Token" }
    $params = @{
        Uri = "$($BaseUrl.TrimEnd('/'))$Path"
        Method = $Method
        Headers = $headers
        UseBasicParsing = $true
    }
    if ($null -ne $Body) {
        $params.ContentType = "application/json; charset=utf-8"
        $json = $Body | ConvertTo-Json -Depth 12 -Compress
        $params.Body = [System.Text.Encoding]::UTF8.GetBytes($json)
    }

    try {
        $response = Invoke-WebRequest @params
        $content = if ($response.Content) { $response.Content | ConvertFrom-Json } else { $null }
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $content; Raw = $response.Content }
    }
    catch {
        $response = $_.Exception.Response
        if ($null -eq $response) { throw }
        $status = [int]$response.StatusCode
        $raw = ""
        try {
            $reader = [System.IO.StreamReader]::new($response.GetResponseStream())
            $raw = $reader.ReadToEnd()
            $reader.Dispose()
        } catch { }
        $content = $null
        if ($raw) { try { $content = $raw | ConvertFrom-Json } catch { } }
        return [pscustomobject]@{ Status = $status; Body = $content; Raw = $raw }
    }
}

function Assert-Status {
    param([string]$Name, $Response, [int[]]$Expected)
    $ok = $Expected -contains $Response.Status
    if ($ok) { $script:passed++ } else { $script:failed++ }
    $detail = if ($Response.Body.mensaje) { [string]$Response.Body.mensaje } elseif ($Response.Raw -is [string] -and $Response.Raw) { $Response.Raw.Substring(0, [Math]::Min(180, $Response.Raw.Length)) } else { "" }
    $script:results.Add([pscustomobject]@{ Test = $Name; Ok = $ok; Status = $Response.Status; Expected = ($Expected -join ','); Detail = $detail })
    $mark = if ($ok) { "PASS" } else { "FAIL" }
    Write-Host ("[{0}] {1} (HTTP {2}; esperado {3})" -f $mark, $Name, $Response.Status, ($Expected -join '/')) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
    return $Response
}

function Test-Call {
    param([string]$Name, [string]$Method, [string]$Path, $Body = $null, [int[]]$Expected = @(200), [string]$Token = $script:token)
    $response = Invoke-QaRequest -Method $Method -Path $Path -Body $Body -Token $Token
    Assert-Status -Name $Name -Response $response -Expected $Expected | Out-Null
    return $response
}

try {
    Test-Call "Health" GET "/health" -Expected 200 -Token "" | Out-Null
    Test-Call "Rechaza acceso sin token" GET "/api/clientes" -Expected 401 -Token "" | Out-Null
    Test-Call "Rechaza credenciales invalidas" POST "/api/auth/login" @{ usuario = $Usuario; password = "incorrecta-$suffix" } -Expected 401 -Token "" | Out-Null

    $login = Test-Call "Login administrador" POST "/api/auth/login" @{ usuario = $Usuario; password = $Password } -Expected 200 -Token ""
    if ($login.Status -ne 200) { throw "No fue posible autenticar la bateria QA." }
    $script:token = $login.Body.accessToken
    $script:refreshToken = $login.Body.refreshToken
    $me = Test-Call "Sesion autenticada" GET "/api/auth/me" -Expected 200
    $adminId = [int]$me.Body.id

    # Simula dos pestañas renovando exactamente el mismo refresh token. Solo una debe ganar;
    # aceptar ambas permitiría reutilizar una credencial ya consumida y crear sesiones paralelas.
    Add-Type -AssemblyName System.Net.Http
    $concurrencyClient = [System.Net.Http.HttpClient]::new()
    try {
        $concurrencyClient.DefaultRequestHeaders.Add('X-Forwarded-For', '203.0.113.200')
        $loginConcurrencyBody = @{
            usuario = $Usuario
            password = $Password
        } | ConvertTo-Json -Compress
        $loginConcurrencyContent = [System.Net.Http.StringContent]::new(
            $loginConcurrencyBody, [System.Text.Encoding]::UTF8, 'application/json')
        $loginConcurrencyResponse = $concurrencyClient.PostAsync(
            "$($BaseUrl.TrimEnd('/'))/api/auth/login", $loginConcurrencyContent).Result

        $statuses = @([int]$loginConcurrencyResponse.StatusCode)
        $winningRefreshToken = $null
        if ([int]$loginConcurrencyResponse.StatusCode -eq 200) {
            $loginConcurrency = $loginConcurrencyResponse.Content.ReadAsStringAsync().Result | ConvertFrom-Json
            $refreshBody = @{ refreshToken = $loginConcurrency.refreshToken } | ConvertTo-Json -Compress
            $refreshTasks = @()
            1..2 | ForEach-Object {
                $refreshContent = [System.Net.Http.StringContent]::new(
                    $refreshBody, [System.Text.Encoding]::UTF8, 'application/json')
                $refreshTasks += $concurrencyClient.PostAsync(
                    "$($BaseUrl.TrimEnd('/'))/api/auth/refresh", $refreshContent)
            }
            [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]$refreshTasks)
            $statuses = @($refreshTasks | ForEach-Object { [int]$_.Result.StatusCode })
            $winner = @($refreshTasks | Where-Object { [int]$_.Result.StatusCode -eq 200 }) | Select-Object -First 1
            if ($winner) {
                $winningRefreshToken = ($winner.Result.Content.ReadAsStringAsync().Result | ConvertFrom-Json).refreshToken
            }
        }

        $concurrencyOk = @($statuses | Where-Object { $_ -eq 200 }).Count -eq 1 -and
            @($statuses | Where-Object { $_ -eq 401 }).Count -eq 1
        $concurrencyResult = [pscustomobject]@{
            Status = if ($concurrencyOk) { 200 } else { 500 }
            Body = [pscustomobject]@{ mensaje = "Estados concurrentes: $($statuses -join ',')" }
            Raw = ""
        }
        Assert-Status -Name "Sesion: refresh atomico entre pestanas" -Response $concurrencyResult -Expected @(200) | Out-Null
        if ($winningRefreshToken) {
            Invoke-QaRequest -Method POST -Path "/api/auth/logout" -Body @{ refreshToken = $winningRefreshToken } -Token "" | Out-Null
        }
    }
    finally {
        $concurrencyClient.Dispose()
    }

    if ($null -eq $login.Body.usuario.sedeId) {
        $sedesDisponibles = Test-Call "Sesion: listar sedes disponibles" GET "/api/sedes" -Expected 200
        $sedeActiva = @($sedesDisponibles.Body | Where-Object { $_.activo })[0]
        if ($null -eq $sedeActiva) { throw "No existe una sede activa para ejecutar la bateria QA." }

        $seleccion = Test-Call "Sesion: seleccionar sede" POST "/api/auth/seleccionar-sede" @{
            sedeId = $sedeActiva.id
            refreshToken = $script:refreshToken
        } -Expected 200
        $script:token = $seleccion.Body.accessToken
        $script:refreshToken = $seleccion.Body.refreshToken

        $renovacion = Test-Call "Sesion: conserva sede al renovar" POST "/api/auth/refresh" @{
            refreshToken = $script:refreshToken
        } -Expected 200 -Token ""
        if ([int]$renovacion.Body.usuario.sedeId -ne [int]$sedeActiva.id) {
            throw "La renovacion de sesion no conservo la sede seleccionada."
        }
        $script:token = $renovacion.Body.accessToken
        $script:refreshToken = $renovacion.Body.refreshToken
    }

    # Catalogos de operacion
    $categoryName = "QA Categoria $suffix"
    $category = Test-Call "Categoria: crear" POST "/api/categorias" @{ nombre = $categoryName; activa = $true } -Expected 201
    $categoryId = [int]$category.Body.id
    Test-Call "Categoria: duplicado" POST "/api/categorias" @{ nombre = $categoryName.ToLowerInvariant(); activa = $true } -Expected 409 | Out-Null
    Test-Call "Categoria: actualizar" PUT "/api/categorias/$categoryId" @{ id = $categoryId; nombre = "$categoryName Editada"; activa = $true } -Expected 204 | Out-Null

    $serviceName = "QA Servicio $suffix"
    $service = Test-Call "Servicio: crear" POST "/api/servicios-admin" @{ nombre = $serviceName; precio = 12.5; unidad = "UNIDAD"; categoriaId = $categoryId; activo = $true } -Expected 201
    $serviceId = [int]$service.Body.id
    Test-Call "Servicio: duplicado" POST "/api/servicios-admin" @{ nombre = $serviceName.ToLowerInvariant(); precio = 8; unidad = "UNIDAD"; categoriaId = $categoryId; activo = $true } -Expected 409 | Out-Null
    Test-Call "Servicio: categoria inexistente" POST "/api/servicios-admin" @{ nombre = "QA Servicio Huerfano $suffix"; precio = 8; unidad = "UNIDAD"; categoriaId = 2147483647; activo = $true } -Expected 400 | Out-Null
    Test-Call "Servicio: actualizar" PUT "/api/servicios-admin/$serviceId" @{ id = $serviceId; nombre = "$serviceName Editado"; precio = 14.5; unidad = "UNIDAD"; categoriaId = $categoryId; activo = $true } -Expected 204 | Out-Null

    $expenseTypeName = "QA Gasto $suffix"
    $expenseType = Test-Call "Tipo de gasto: crear" POST "/api/tipos-gasto-admin" @{ nombre = $expenseTypeName; activo = $true } -Expected 201
    $expenseTypeId = [int]$expenseType.Body.id
    Test-Call "Tipo de gasto: duplicado" POST "/api/tipos-gasto-admin" @{ nombre = $expenseTypeName.ToLowerInvariant(); activo = $true } -Expected 409 | Out-Null
    Test-Call "Tipo de gasto: actualizar" PUT "/api/tipos-gasto-admin/$expenseTypeId" @{ id = $expenseTypeId; nombre = "$expenseTypeName Editado"; activo = $true } -Expected 204 | Out-Null

    $areaName = "QA Area $suffix"
    $areasActuales = Test-Call "Area: listar para reservar orden" GET "/api/areas-lavado-admin" -Expected 200
    $ordenesOcupados = @($areasActuales.Body | ForEach-Object { [int]$_.orden })
    $ordenesLibres = @(1..100 | Where-Object { $_ -notin $ordenesOcupados } | Sort-Object -Descending)
    if ($ordenesLibres.Count -lt 2) { throw "No hay dos ordenes de area libres para ejecutar la bateria QA." }
    $areaOrden = [int]$ordenesLibres[0]
    $areaOrdenEditado = [int]$ordenesLibres[1]
    $area = Test-Call "Area: crear" POST "/api/areas-lavado-admin" @{ nombre = $areaName; orden = $areaOrden; tiempoEstMinutos = 20; activa = $true } -Expected 201
    $areaId = [int]$area.Body.id
    Test-Call "Area: nombre duplicado" POST "/api/areas-lavado-admin" @{ nombre = $areaName.ToLowerInvariant(); orden = 91; tiempoEstMinutos = 20; activa = $true } -Expected 409 | Out-Null
    Test-Call "Area: orden duplicado" POST "/api/areas-lavado-admin" @{ nombre = "QA Otra Area $suffix"; orden = $areaOrden; tiempoEstMinutos = 20; activa = $true } -Expected 409 | Out-Null
    Test-Call "Area: actualizar" PUT "/api/areas-lavado-admin/$areaId" @{ id = $areaId; nombre = "$areaName Editada"; orden = $areaOrdenEditado; tiempoEstMinutos = 25; activa = $true } -Expected 204 | Out-Null

    # Inventario
    $supplyName = "QA Detergente $suffix"
    $supply = Test-Call "Insumo: crear" POST "/api/insumos" @{ nombre = $supplyName; unidadMedida = "L"; stockActual = 5; stockMinimo = 2; activo = $true } -Expected 201
    $supplyId = [int]$supply.Body.id
    Test-Call "Insumo: duplicado" POST "/api/insumos" @{ nombre = $supplyName.ToLowerInvariant(); unidadMedida = "L"; stockActual = 1; stockMinimo = 1; activo = $true } -Expected 409 | Out-Null
    Test-Call "Insumo: consumo mayor al stock" POST "/api/insumos/$supplyId/movimientos" @{ tipo = "CONSUMO"; cantidad = 999; descripcion = "QA" } -Expected 400 | Out-Null
    Test-Call "Insumo: compra" POST "/api/insumos/$supplyId/movimientos" @{ tipo = "COMPRA"; cantidad = 3; costoTotal = 30; metodoPago = "YAPE"; tipoGastoId = $expenseTypeId; descripcion = "Compra QA" } -Expected 200 | Out-Null
    Test-Call "Insumo: historial" GET "/api/insumos/movimientos?insumoId=$supplyId" -Expected 200 | Out-Null
    Test-Call "Insumo: desactivar" DELETE "/api/insumos/$supplyId" -Expected 200 | Out-Null
    Test-Call "Insumo: bloquea movimiento inactivo" POST "/api/insumos/$supplyId/movimientos" @{ tipo = "AJUSTE"; cantidad = 1; descripcion = "No debe entrar" } -Expected 409 | Out-Null

    # CRM
    $clientPhone = "9" + $suffix.Substring([Math]::Max(0, $suffix.Length - 8))
    $client = Test-Call "Cliente: crear" POST "/api/clientes" @{ nombre = "QA Cliente $suffix"; celular = $clientPhone; dni = $suffix.Substring([Math]::Max(0, $suffix.Length - 8)); direccion = "Direccion QA"; puntos = 0 } -Expected 201
    $clientId = [int]$client.Body.id
    Test-Call "Cliente: celular invalido" POST "/api/clientes" @{ nombre = "QA Invalido"; celular = "123" } -Expected 400 | Out-Null
    Test-Call "Cliente: mismo nombre y celular duplicado" POST "/api/clientes" @{ nombre = "QA Cliente $suffix"; celular = $clientPhone } -Expected 409 | Out-Null
    $sharedPhoneClient = Test-Call "Cliente: permite celular familiar compartido" POST "/api/clientes" @{ nombre = "QA Familiar $suffix"; celular = $clientPhone } -Expected 201
    $sharedPhoneClientId = [int]$sharedPhoneClient.Body.id
    Test-Call "Cliente: actualizar" PUT "/api/clientes/$clientId" @{ id = $clientId; nombre = "QA Cliente Editado $suffix"; celular = $clientPhone; direccion = "Direccion editada"; puntos = 0 } -Expected 204 | Out-Null
    Test-Call "Cliente: sumar puntos" POST "/api/clientes/$clientId/puntos" @{ motivo = "QA fidelizacion"; puntos = 10; tipo = "SUMA" } -Expected 204 | Out-Null
    Test-Call "Cliente: tipo de puntos invalido" POST "/api/clientes/$clientId/puntos" @{ motivo = "QA invalido"; puntos = 1; tipo = "OTRO" } -Expected 400 | Out-Null
    Test-Call "Cliente: historial puntos" GET "/api/clientes/$clientId/puntos" -Expected 200 | Out-Null

    # Personal, repartidores y sedes
    $driver = Test-Call "Motorizado: crear" POST "/api/motorizados" @{ nombre = "QA Repartidor $suffix"; celular = $clientPhone; activo = $true } -Expected 201
    $driverId = [int]$driver.Body.id
    Test-Call "Motorizado: celular invalido" POST "/api/motorizados" @{ nombre = "QA Invalido"; celular = "123"; activo = $true } -Expected 400 | Out-Null
    Test-Call "Motorizado: celular duplicado" POST "/api/motorizados" @{ nombre = "QA Duplicado"; celular = $clientPhone; activo = $true } -Expected 409 | Out-Null
    Test-Call "Motorizado: actualizar" PUT "/api/motorizados/$driverId" @{ id = $driverId; nombre = "QA Repartidor Editado $suffix"; celular = $clientPhone; activo = $true } -Expected 204 | Out-Null

    $staffRole = Test-Call "Rol de personal: crear" POST "/api/roles-personal" @{ nombre = "QA Operario $suffix"; activo = $true } -Expected 201
    $staffRoleId = [int]$staffRole.Body.id
    Test-Call "Rol de personal: duplicado" POST "/api/roles-personal" @{ nombre = "QA Operario $suffix"; activo = $true } -Expected 409 | Out-Null
    $employee = Test-Call "Personal: crear" POST "/api/personal" @{ nombre = "QA Empleado $suffix"; dni = "12345678"; celular = $clientPhone; cargo = "QA Operario"; fechaIngreso = (Get-Date).ToString('yyyy-MM-dd'); activo = $true } -Expected 201
    $employeeId = [int]$employee.Body.id
    Test-Call "Personal: DNI invalido" POST "/api/personal" @{ nombre = "QA DNI Invalido"; dni = "123"; activo = $true } -Expected 400 | Out-Null
    Test-Call "Personal: DNI duplicado" POST "/api/personal" @{ nombre = "QA DNI Duplicado"; dni = "12345678"; activo = $true } -Expected 409 | Out-Null
    Test-Call "Personal: celular duplicado" POST "/api/personal" @{ nombre = "QA Celular Duplicado"; celular = $clientPhone; activo = $true } -Expected 409 | Out-Null
    Test-Call "Personal: actualizar" PUT "/api/personal/$employeeId" @{ id = $employeeId; nombre = "QA Empleado Editado $suffix"; dni = "12345678"; celular = $clientPhone; cargo = "QA Operario"; fechaIngreso = (Get-Date).ToString('yyyy-MM-dd'); activo = $true } -Expected 204 | Out-Null
    Test-Call "Personal: desactivar" DELETE "/api/personal/$employeeId" -Expected 200 | Out-Null

    $branch = Test-Call "Sede: crear" POST "/api/sedes" @{ nombre = "QA Sede $suffix"; direccion = "Calle QA 123"; telefono = $clientPhone; activo = $true } -Expected 201
    $branchId = [int]$branch.Body.id
    Test-Call "Sede: duplicado" POST "/api/sedes" @{ nombre = "QA Sede $suffix"; direccion = "Otra"; activo = $true } -Expected 409 | Out-Null
    Test-Call "Sede: actualizar" PUT "/api/sedes/$branchId" @{ id = $branchId; nombre = "QA Sede Editada $suffix"; direccion = "Calle QA 456"; telefono = $clientPhone; activo = $true } -Expected 204 | Out-Null

    # Promociones
    $today = (Get-Date).ToString('yyyy-MM-dd')
    $tomorrow = (Get-Date).AddDays(1).ToString('yyyy-MM-dd')
    $yesterday = (Get-Date).AddDays(-1).ToString('yyyy-MM-dd')
    $promoCode = "QA" + $suffix.Substring([Math]::Max(0, $suffix.Length - 10))
    $promo = Test-Call "Promocion: crear" POST "/api/promociones" @{ tipo = "CODIGO"; descripcion = "QA promocion $suffix"; descuentoPct = 10; servicioId = $serviceId; cantidadMinima = 1; fechaInicio = $today; fechaFin = $tomorrow; activa = $true; codigo = $promoCode } -Expected 201
    $promoId = [int]$promo.Body.id
    Test-Call "Promocion: fechas invertidas" POST "/api/promociones" @{ tipo = "CODIGO"; descripcion = "QA fechas invalidas"; descuentoPct = 10; cantidadMinima = 1; fechaInicio = $tomorrow; fechaFin = $yesterday; activa = $true; codigo = "X$promoCode" } -Expected 400 | Out-Null
    Test-Call "Promocion: codigo duplicado" POST "/api/promociones" @{ tipo = "CODIGO"; descripcion = "QA codigo duplicado"; descuentoPct = 5; cantidadMinima = 1; activa = $true; codigo = $promoCode.ToLowerInvariant() } -Expected 409 | Out-Null
    Test-Call "Promocion: servicio inexistente" POST "/api/promociones" @{ tipo = "VOLUMEN"; descripcion = "QA servicio invalido"; descuentoPct = 5; servicioId = 2147483647; cantidadMinima = 2; activa = $true } -Expected 400 | Out-Null
    Test-Call "Promocion: validar codigo" GET "/api/pedidos/promocion/validar?codigo=$promoCode" -Expected 200 | Out-Null

    # Usuarios y autorizacion por modulo
    $roles = Test-Call "Usuarios: listar roles" GET "/api/usuarios/roles" -Expected 200
    $branches = Test-Call "Usuarios: listar sedes" GET "/api/sedes" -Expected 200
    $operatorRole = @($roles.Body | Where-Object { $_.codigo -ne 'ADMIN' })[0]
    $activeBranch = @($branches.Body | Where-Object { $_.activo })[0]
    Test-Call "Usuario: password debil" POST "/api/usuarios" @{ usuario = "qaweak$suffix"; nombreCompleto = "QA Weak"; password = "123"; rolId = $operatorRole.id; sedeId = $activeBranch.id; activo = $true } -Expected 400 | Out-Null
    Test-Call "Usuario: bloquea autodesactivacion" PATCH "/api/usuarios/$adminId/estado" @{ activo = $false } -Expected 400 | Out-Null

    $modules = Test-Call "Permisos: listar modulos" GET "/api/permisos/modulos" -Expected 200
    $permissionsMatrix = Test-Call "Permisos: obtener configuracion actual" GET "/api/permisos" -Expected 200
    $originalPermissions = @($permissionsMatrix.Body | Where-Object { [int]$_.rolId -eq [int]$operatorRole.id } | ForEach-Object {
        @{ rolId = [int]$_.rolId; modulo = [string]$_.modulo; puedeAcceder = [bool]$_.puedeAcceder }
    })
    Test-Call "Permisos: rechaza modulo invalido" PUT "/api/permisos" @{ permisos = @(@{ rolId = $operatorRole.id; modulo = "NO_EXISTE"; puedeAcceder = $true }) } -Expected 400 | Out-Null
    Test-Call "Permisos: rechaza duplicados" PUT "/api/permisos" @{ permisos = @(
        @{ rolId = $operatorRole.id; modulo = "CLIENTES"; puedeAcceder = $true },
        @{ rolId = $operatorRole.id; modulo = "CLIENTES"; puedeAcceder = $false }
    ) } -Expected 400 | Out-Null
    $limitedPermissions = @($modules.Body | ForEach-Object {
        @{ rolId = $operatorRole.id; modulo = [string]$_; puedeAcceder = ($_ -in @('INICIO', 'CLIENTES')) }
    })
    Test-Call "Permisos: guardar rol limitado" PUT "/api/permisos" @{ permisos = $limitedPermissions } -Expected 204 | Out-Null

    $limitedUserName = "qauser" + $suffix
    $limitedPassword = "Qa$suffix!"
    $limitedUser = Test-Call "Usuario: crear limitado" POST "/api/usuarios" @{ usuario = $limitedUserName; nombreCompleto = "QA Usuario Limitado"; email = "qa$suffix@example.com"; password = $limitedPassword; rolId = $operatorRole.id; sedeId = $activeBranch.id; activo = $true } -Expected 201
    $limitedUserId = [int]$limitedUser.Body.id
    Test-Call "Usuario: duplicado" POST "/api/usuarios" @{ usuario = $limitedUserName; nombreCompleto = "QA Duplicado"; password = $limitedPassword; rolId = $operatorRole.id; sedeId = $activeBranch.id; activo = $true } -Expected 409 | Out-Null
    $limitedLogin = Test-Call "Usuario limitado: login" POST "/api/auth/login" @{ usuario = $limitedUserName; password = $limitedPassword } -Expected 200 -Token ""
    if ($limitedLogin.Status -eq 200) {
        $limitedToken = [string]$limitedLogin.Body.accessToken
        Test-Call "Usuario limitado: permite Clientes" GET "/api/clientes?limite=1" -Expected 200 -Token $limitedToken | Out-Null
        Test-Call "Usuario limitado: deniega Inventario" GET "/api/insumos" -Expected 403 -Token $limitedToken | Out-Null
        Test-Call "Usuario limitado: deniega Ajustes" GET "/api/sedes" -Expected 403 -Token $limitedToken | Out-Null
        Test-Call "Usuario limitado: logout" POST "/api/auth/logout" @{ refreshToken = $limitedLogin.Body.refreshToken } -Expected 204 -Token "" | Out-Null
    }
    Test-Call "Usuario: actualizar" PUT "/api/usuarios/$limitedUserId" @{ id = $limitedUserId; usuario = $limitedUserName; nombreCompleto = "QA Usuario Editado"; email = "qa$suffix@example.com"; password = $null; rolId = $operatorRole.id; sedeId = $activeBranch.id; activo = $true } -Expected 204 | Out-Null
    Test-Call "Usuario: desactivar" PATCH "/api/usuarios/$limitedUserId/estado" @{ activo = $false } -Expected 204 | Out-Null
    Test-Call "Usuario inactivo: rechaza login" POST "/api/auth/login" @{ usuario = $limitedUserName; password = $limitedPassword } -Expected 401 -Token "" | Out-Null

    # Configuracion del negocio, WhatsApp, pagos online y facturacion
    $businessConfig = Test-Call "Configuracion: consultar" GET "/api/configuracion" -Expected 200
    Test-Call "Configuracion: guardar sin cambios" PUT "/api/configuracion" $businessConfig.Body -Expected 204 | Out-Null
    $templates = Test-Call "WhatsApp: listar plantillas" GET "/api/plantillas-whatsapp-admin" -Expected 200
    if (@($templates.Body).Count -gt 0) {
        $template = @($templates.Body)[0]
        Test-Call "WhatsApp: rechaza mensaje vacio" PUT "/api/plantillas-whatsapp-admin/$($template.id)" @{ id = $template.id; evento = $template.evento; mensaje = ""; activa = $template.activa } -Expected 400 | Out-Null
        Test-Call "WhatsApp: actualizar plantilla" PUT "/api/plantillas-whatsapp-admin/$($template.id)" $template -Expected 204 | Out-Null
    }
    Test-Call "Pagos online: consultar configuracion" GET "/api/pagos/configuracion" -Expected 200 | Out-Null
    Test-Call "Pagos online: rechaza proveedor no soportado" PUT "/api/pagos/configuracion" @{ proveedor = "PAYPAL"; activo = $false } -Expected 400 | Out-Null
    Test-Call "Pagos online: rechaza llave RSA incompleta" PUT "/api/pagos/configuracion" @{ proveedor = "IZIPAY"; publicKey = "invalida"; activo = $false } -Expected 400 | Out-Null
    Test-Call "Pagos online: bloquea activacion pendiente" PUT "/api/pagos/configuracion" @{ proveedor = "IZIPAY"; activo = $true } -Expected 400 | Out-Null
    Test-Call "Facturacion: consultar configuracion" GET "/api/facturacion/configuracion" -Expected 200 | Out-Null
    Test-Call "Facturacion: rechaza ambiente invalido" PUT "/api/facturacion/configuracion" @{ ambiente = "OTRO"; serieBoleta = "B001"; serieFactura = "F001"; activo = $false } -Expected 400 | Out-Null
    Test-Call "Facturacion: rechaza RUC no numerico" PUT "/api/facturacion/configuracion" @{ rucEmisor = "ABCDEFGHIJK"; ambiente = "BETA"; serieBoleta = "B001"; serieFactura = "F001"; activo = $false } -Expected 400 | Out-Null
    Test-Call "Facturacion: rechaza serie invalida" PUT "/api/facturacion/configuracion" @{ ambiente = "BETA"; serieBoleta = "X001"; serieFactura = "F001"; activo = $false } -Expected 400 | Out-Null
    Test-Call "Facturacion: no activa configuracion incompleta" PUT "/api/facturacion/configuracion" @{ razonSocial = "QA"; rucEmisor = "20123456789"; ambiente = "BETA"; serieBoleta = "B001"; serieFactura = "F001"; activo = $true } -Expected 400 | Out-Null
    Test-Call "Facturacion: listar comprobantes" GET "/api/facturacion/comprobantes?pagina=1&tamanoPagina=5" -Expected 200 | Out-Null

    # Pedidos y transiciones de negocio
    $orderBody = @{ clienteId = $clientId; modalidad = "Tienda"; items = @(@{ servicioId = $serviceId; cantidad = 1; descripcion = "QA" }); descuentoPct = 0; esUrgente = $false; montoPagado = 0; metodoPagoInicial = "EFECTIVO"; fechaEntregaEst = (Get-Date).AddDays(2).ToString('s') }
    $order = Test-Call "Pedido: crear tienda" POST "/api/pedidos" $orderBody -Expected 201
    $orderId = [int]$order.Body.id
    Test-Call "Pedido: sobrepago" POST "/api/pedidos/$orderId/pagos" @{ monto = 999999; metodo = "EFECTIVO"; descripcion = "No debe entrar" } -Expected 400 | Out-Null
    Test-Call "Pedido: agregar item" POST "/api/pedidos/$orderId/items" @{ servicioId = $serviceId; cantidad = 1; descripcion = "QA extra" } -Expected 204 | Out-Null
    $order = Test-Call "Pedido: consultar actualizado" GET "/api/pedidos/$orderId" -Expected 200
    Test-Call "Pedido: fecha pasada" PUT "/api/pedidos/$orderId/fecha-entrega" @{ fecha = (Get-Date).AddDays(-2).ToString('s'); motivo = "QA" } -Expected 400 | Out-Null
    Test-Call "Pedido: fecha futura" PUT "/api/pedidos/$orderId/fecha-entrega" @{ fecha = (Get-Date).AddDays(3).ToString('s'); motivo = "QA reprogramacion" } -Expected 204 | Out-Null
    for ($i = 0; $i -lt 20; $i++) {
        $current = Test-Call "Pedido: consultar flujo $i" GET "/api/pedidos/$orderId" -Expected 200
        if ($current.Body.estadoProceso -eq 'LISTO') { break }
        $advance = Test-Call "Pedido: avanzar flujo $i" POST "/api/pedidos/$orderId/siguiente-area" @{} -Expected 204
        if ($advance.Status -ne 204) { break }
    }
    Test-Call "Pedido: no entrega con deuda" POST "/api/pedidos/$orderId/siguiente-area" @{ recibidoPor = "QA Cliente" } -Expected 400 | Out-Null
    $order = Test-Call "Pedido: saldo antes de pagar" GET "/api/pedidos/$orderId" -Expected 200
    $saldo = [decimal]$order.Body.total - [decimal]$order.Body.montoPagado
    Test-Call "Pedido: pagar saldo" POST "/api/pedidos/$orderId/pagos" @{ monto = $saldo; metodo = "YAPE"; descripcion = "QA pago total" } -Expected 204 | Out-Null
    Test-Call "Pedido: entregar" POST "/api/pedidos/$orderId/siguiente-area" @{ recibidoPor = "QA Cliente" } -Expected 204 | Out-Null
    Test-Call "Pedido final: bloquea pago" POST "/api/pedidos/$orderId/pagos" @{ monto = 1; metodo = "EFECTIVO" } -Expected 400 | Out-Null
    Test-Call "Pedido final: bloquea item" POST "/api/pedidos/$orderId/items" @{ servicioId = $serviceId; cantidad = 1 } -Expected 400 | Out-Null
    Test-Call "Pedido final: bloquea avance" POST "/api/pedidos/$orderId/siguiente-area" @{} -Expected 400 | Out-Null

    Test-Call "Delivery: exige direccion" POST "/api/pedidos" @{ clienteId = $clientId; modalidad = "Delivery"; items = @(@{ servicioId = $serviceId; cantidad = 1 }); montoPagado = 0; metodoPagoInicial = "EFECTIVO"; fechaEntregaEst = (Get-Date).AddDays(2).ToString('s') } -Expected 400 | Out-Null
    $delivery = Test-Call "Delivery: crear con destino" POST "/api/pedidos" @{ clienteId = $clientId; modalidad = "Delivery"; direccionEntrega = "Av. QA 123"; distritoEntrega = "Comas"; referenciaEntrega = "Puerta azul"; latitudEntrega = -11.94; longitudEntrega = -77.06; costoDelivery = 5; items = @(@{ servicioId = $serviceId; cantidad = 1 }); montoPagado = 0; metodoPagoInicial = "EFECTIVO"; fechaEntregaEst = (Get-Date).AddDays(2).ToString('s') } -Expected 201
    $deliveryId = [int]$delivery.Body.id
    Test-Call "Delivery: asignar repartidor" PUT "/api/pedidos/$deliveryId/motorizado" @{ motorizadoId = $driverId } -Expected 204 | Out-Null
    $tracking = Test-Call "Delivery: link cliente" GET "/api/pedidos/$deliveryId/link-seguimiento" -Expected 200
    $driverLink = Test-Call "Delivery: link repartidor" GET "/api/pedidos/$deliveryId/link-repartidor" -Expected 200
    if ($tracking.Status -eq 200) { Test-Call "Delivery: seguimiento publico" GET "/api/pago-publico/$($tracking.Body.token)" -Expected 200 -Token "" | Out-Null }
    if ($driverLink.Status -eq 200) {
        $driverToken = $driverLink.Body.token
        Test-Call "Delivery: vista publica repartidor" GET "/api/repartidor/$driverToken" -Expected 200 -Token "" | Out-Null
        Test-Call "Delivery: no inicia ruta antes de estar listo" POST "/api/repartidor/$driverToken/iniciar-ruta" @{} -Expected 400 -Token "" | Out-Null
        Test-Call "Delivery: no comparte ubicacion sin iniciar ruta" POST "/api/repartidor/$driverToken/ubicacion" @{ lat = -11.94; lng = -77.06 } -Expected 400 -Token "" | Out-Null
        Test-Call "Delivery: rechaza coordenadas invalidas" POST "/api/repartidor/$driverToken/ubicacion" @{ lat = 999; lng = 999 } -Expected 400 -Token "" | Out-Null
        Test-Call "Delivery: no entrega antes de estar listo" POST "/api/repartidor/$driverToken/entregado" @{} -Expected 400 -Token "" | Out-Null
    }

    $cancelOrder = Test-Call "Pedido: crear para anular" POST "/api/pedidos" $orderBody -Expected 201
    $cancelOrderId = [int]$cancelOrder.Body.id
    Test-Call "Pedido: anular" POST "/api/pedidos/$cancelOrderId/anular" @{ motivo = "Prueba integral QA" } -Expected 204 | Out-Null
    Test-Call "Pedido anulado: bloquea pago" POST "/api/pedidos/$cancelOrderId/pagos" @{ monto = 1; metodo = "EFECTIVO" } -Expected 400 | Out-Null

    # Caja y reportes
    Test-Call "Caja: metodo invalido" POST "/api/caja/gastos" @{ monto = 5; metodoPago = "BITCOIN"; descripcion = "No debe entrar" } -Expected 400 | Out-Null
    Test-Call "Caja: registrar gasto" POST "/api/caja/gastos" @{ monto = 5; metodoPago = "TRANSFERENCIA"; tipoGastoId = $expenseTypeId; descripcion = "QA gasto" } -Expected 201 | Out-Null
    Test-Call "Caja: bloquea cuadre futuro" POST "/api/caja/cuadres" @{ fecha = (Get-Date).AddDays(1).ToString('s'); usuarioId = $adminId; cajaInicial = 0; totalContado = 0; corte = 0 } -Expected 400 | Out-Null
    Test-Call "Caja: movimientos" GET "/api/caja/movimientos?fecha=$((Get-Date).ToString('yyyy-MM-dd'))" -Expected 200 | Out-Null

    foreach ($report in @('sla','vista-gerencial','consolidado','ordenes-pendientes','gastos','general','servicios','cuadres-caja','cuadres-diarios','ordenes-mensual','almacen','anulados','registro-entregas','pagos','descuento-directo')) {
        Test-Call "Reporte: $report" GET "/api/reportes/$report" -Expected 200 | Out-Null
    }
    Test-Call "Reporte: exportacion Excel" GET "/api/reportes/export/general" -Expected 200 | Out-Null

    # Desactivaciones al final de los catalogos creados
    Test-Call "Promocion: eliminar" DELETE "/api/promociones/$promoId" -Expected 204 | Out-Null
    Test-Call "Rol de personal: desactivar" DELETE "/api/roles-personal/$staffRoleId" -Expected 200 | Out-Null
    Test-Call "Tipo de gasto: desactivar" DELETE "/api/tipos-gasto-admin/$expenseTypeId" -Expected 200 | Out-Null
    Test-Call "Categoria: desactivar" DELETE "/api/categorias/$categoryId" -Expected 200 | Out-Null
}
finally {
    if ($script:token) {
        if ($originalPermissions -and $originalPermissions.Count -gt 0) {
            Test-Call "Limpieza: restaurar permisos del rol" PUT "/api/permisos" @{ permisos = $originalPermissions } -Expected 204 | Out-Null
        }
        if ($deliveryId) { Test-Call "Limpieza: anular delivery QA" POST "/api/pedidos/$deliveryId/anular" @{ motivo = "Limpieza automatica de prueba QA" } -Expected 204 | Out-Null }
        if ($sharedPhoneClientId) { Test-Call "Limpieza: desactivar familiar QA" DELETE "/api/clientes/$sharedPhoneClientId" -Expected 200 | Out-Null }
        if ($clientId) { Test-Call "Limpieza: desactivar cliente QA" DELETE "/api/clientes/$clientId" -Expected 200 | Out-Null }
        if ($driverId) { Test-Call "Limpieza: desactivar motorizado QA" PATCH "/api/motorizados/$driverId/estado" @{ activo = $false } -Expected 204 | Out-Null }
        if ($serviceId) { Test-Call "Limpieza: desactivar servicio QA" DELETE "/api/servicios-admin/$serviceId" -Expected 200 | Out-Null }
        if ($areaId) { Test-Call "Limpieza: desactivar area QA" DELETE "/api/areas-lavado-admin/$areaId" -Expected 200 | Out-Null }
        if ($branchId) { Test-Call "Limpieza: desactivar sede QA" PATCH "/api/sedes/$branchId/estado" @{ activo = $false } -Expected 204 | Out-Null }
    }

    if ($script:refreshToken) {
        $logout = Invoke-QaRequest -Method POST -Path "/api/auth/logout" -Body @{ refreshToken = $script:refreshToken } -Token ""
        Assert-Status -Name "Logout y revocacion de refresh token" -Response $logout -Expected @(204) | Out-Null
    }

    Write-Host ""
    Write-Host ("Resumen QA: {0} aprobadas, {1} fallidas, {2} totales." -f $script:passed, $script:failed, ($script:passed + $script:failed)) -ForegroundColor Cyan
    if ($script:failed -gt 0) {
        Write-Host "Fallos:" -ForegroundColor Red
        $script:results | Where-Object { -not $_.Ok } | Format-Table Test, Status, Expected, Detail -AutoSize
    }
}

if ($script:failed -gt 0) { exit 1 }
