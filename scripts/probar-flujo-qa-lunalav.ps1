[CmdletBinding()]
param(
    [string]$ApiBase = 'https://app.lunalav.pe/api',
    [switch]$ConservarEmpresa
)

$ErrorActionPreference = 'Stop'

function Convertir-JsonApi($valor) {
    return ($valor | ConvertTo-Json -Depth 12 -Compress)
}

function Invocar-Api {
    param(
        [ValidateSet('GET','POST','PUT','PATCH','DELETE')] [string]$Metodo,
        [string]$Ruta,
        $Cuerpo,
        [string]$Token
    )

    $parametros = @{ Uri = "$ApiBase$Ruta"; Method = $Metodo; Headers = @{ Accept = 'application/json' } }
    if ($Token) { $parametros.Headers.Authorization = "Bearer $Token" }
    if ($null -ne $Cuerpo) {
        $parametros.ContentType = 'application/json; charset=utf-8'
        $parametros.Body = Convertir-JsonApi $Cuerpo
    }
    return Invoke-RestMethod @parametros
}

function Verificar {
    param([bool]$Condicion, [string]$Mensaje)
    if (-not $Condicion) { throw "QA FALLÓ: $Mensaje" }
    $script:verificaciones.Add($Mensaje) | Out-Null
}

$verificaciones = [System.Collections.Generic.List[string]]::new()
$marca = Get-Date -Format 'yyyyMMddHHmmss'
$slug = "qa-semanal-$marca"
$nombre = "QA Operación Semanal $marca"
$passwordPath = 'C:\Users\Caleb\AppData\Local\LunaLav\demo\propietario.txt'
if (-not (Test-Path -LiteralPath $passwordPath)) { throw 'No se encontró la contraseña local del propietario.' }
$passwordPropietario = (Get-Content -LiteralPath $passwordPath -Raw).Trim()

try {
    # 1) Panel propietario: alta, plan, monto recurrente y cobro de suscripción.
    $owner = Invocar-Api POST '/auth/login' @{ usuario='propietario'; password=$passwordPropietario }
    Verificar ($owner.usuario.rol -eq 'PROPIETARIO') 'Acceso del propietario al panel SaaS'

    $empresa = Invocar-Api POST '/negocios' @{
        nombre=$nombre; slug=$slug; rucEmpresa='20612345678'
        titularNombre='Cliente QA LunaLav'; titularEmail='qa-operacion@ejemplo.test'; titularCelular='999111222'
        sedeNombre='Sede Central QA'; adminUsuario='adminqa'; adminNombreCompleto='Administradora QA'; adminEmail='adminqa@ejemplo.test'; adminPassword='QaSegura2026!'
    } $owner.accessToken
    Verificar ($empresa.slug -eq $slug) 'Alta de empresa desde el panel de dueño'
    $empresaId = $empresa.id

    Invocar-Api PUT "/negocios/$empresaId/suscripcion" @{ planSuscripcion='PRO'; estadoSuscripcion='ACTIVA'; montoMensual=50; proximoPago=$null } $owner.accessToken | Out-Null
    $pagoSub = Invocar-Api POST "/negocios/$empresaId/pagos" @{ monto=50; metodo='YAPE'; meses=1; nota='Pago QA de suscripción mensual' } $owner.accessToken
    $fichaOwner = Invocar-Api GET "/negocios/$empresaId" $null $owner.accessToken
    Verificar ($fichaOwner.planSuscripcion -eq 'PRO' -and [decimal]$fichaOwner.montoMensual -eq 50 -and $fichaOwner.estadoSuscripcion -eq 'ACTIVA') 'Plan PRO y monto mensual S/ 50 guardados'
    Verificar ($pagoSub.monto -eq 50 -and $pagoSub.metodo -eq 'YAPE') 'Cobro de suscripción registrado y trazable'

    # 2) Cliente: login, ficha del negocio, sucursal y catálogos.
    $admin = Invocar-Api POST '/auth/login' @{ usuario='adminqa'; password='QaSegura2026!'; empresaSlug=$slug }
    Verificar ($admin.usuario.rol -eq 'ADMIN' -and $admin.usuario.negocioId -eq $empresaId) 'Inicio de sesión del administrador del cliente aislado por slug'
    $token = $admin.accessToken
    $cfg = Invocar-Api GET '/configuracion' $null $token
    $cfg.nombreNegocio = 'Lavandería QA Central'; $cfg.direccion='Av. Prueba 123, Lima'; $cfg.telefono='999111222'; $cfg.ruc='20612345678'
    $cfg.horarioAtencion='Lun-Sáb 08:00-20:00'; $cfg.metaMensual=5000; $cfg.costoDelivery=8; $cfg.valorPuntoCanje=0.10; $cfg.maxDescuentoPct=20
    Invocar-Api PUT '/configuracion' $cfg $token | Out-Null
    $cfgGuardada = Invocar-Api GET '/configuracion' $null $token
    Verificar ($cfgGuardada.nombreNegocio -eq 'Lavandería QA Central' -and [decimal]$cfgGuardada.costoDelivery -eq 8) 'Datos y tarifa de delivery del negocio guardados'

    $sedeSecundaria = Invocar-Api POST '/sedes' @{ nombre='Sucursal QA Norte'; direccion='Av. Norte 456'; telefono='999333444'; activo=$true } $token
    Verificar ($sedeSecundaria.nombre -eq 'Sucursal QA Norte') 'Alta de segunda sede'

    $servicios = @()
    $servicios += Invocar-Api POST '/servicios-admin' @{ nombre='Lavado por kilo'; precio=8.5; costo=2.5; unidad='kg'; activo=$true } $token
    $servicios += Invocar-Api POST '/servicios-admin' @{ nombre='Planchado por prenda'; precio=4; costo=0.8; unidad='prenda'; activo=$true } $token
    $servicios += Invocar-Api POST '/servicios-admin' @{ nombre='Edredón'; precio=28; costo=7; unidad='unidad'; activo=$true } $token
    Verificar ($servicios.Count -eq 3) 'Catálogo de servicios creado'

    # 3) Inventario: alta, favoritos, compra vinculada a gasto y consumo retrofechado.
    $tipoGasto = Invocar-Api POST '/tipos-gasto-admin' @{ nombre='Insumos de lavado QA'; activo=$true } $token
    $detergente = Invocar-Api POST '/insumos' @{ nombre='Detergente líquido QA'; unidadMedida='L'; clase='INSUMO'; stockActual=10; stockMinimo=5; activo=$true; contenidoValor=20; contenidoUnidad='L' } $token
    $suavizante = Invocar-Api POST '/insumos' @{ nombre='Suavizante QA'; unidadMedida='L'; clase='INSUMO'; stockActual=8; stockMinimo=3; activo=$true } $token
    Invocar-Api PATCH "/insumos/$($detergente.id)/favorito" @{ favorito=$true } $token | Out-Null
    $inicioSemana = (Get-Date).Date.AddDays(-6)
    Invocar-Api POST "/insumos/$($detergente.id)/movimientos" @{ tipo='COMPRA'; cantidad=12; costoTotal=72; metodoPago='EFECTIVO'; tipoGastoId=$tipoGasto.id; descripcion='Compra QA de detergente'; fecha=$inicioSemana } $token | Out-Null
    Invocar-Api POST "/insumos/$($detergente.id)/movimientos" @{ tipo='CONSUMO'; cantidad=4; descripcion='Consumo operativo QA'; fecha=$inicioSemana.AddDays(2); esMedicion=$false } $token | Out-Null
    $inventario = Invocar-Api GET '/insumos' $null $token
    $detergenteFinal = $inventario | Where-Object id -eq $detergente.id
    Verificar ($detergenteFinal.favorito -and [decimal]$detergenteFinal.stockActual -eq 18) 'Favorito e inventario con compra/consumo y stock correcto'

    # 4) Siete pedidos que cubren tienda, delivery, adelanto, deuda, descuento y urgente.
    $pedidos = @()
    for ($i = 0; $i -lt 7; $i++) {
        $fecha = $inicioSemana.AddDays($i).AddHours(10)
        $esDelivery = ($i % 2 -eq 1)
        $items = @(@{ servicioId=$servicios[0].id; cantidad=($i % 3) + 1; precioUnit=8.5; total=0; descripcion="Bolsa QA $($i + 1)" })
        if ($i % 3 -eq 0) { $items += @{ servicioId=$servicios[1].id; cantidad=2; precioUnit=4; total=0; descripcion='Planchado QA' } }
        $pagoInicial = if ($i % 3 -eq 0) { 0 } elseif ($i % 3 -eq 1) { 10 } else { 25 }
        $pedido = Invocar-Api POST '/pedidos' @{
            clienteNuevo=@{ nombre="Cliente QA $($i + 1)"; celular=("990000{0:D3}" -f ($i+1)); direccion=if($esDelivery){'Calle QA 100'}else{$null} }
            modalidad=if($esDelivery){'Delivery'}else{'Tienda'}; direccionEntrega=if($esDelivery){'Calle QA 100'}else{$null}; distritoEntrega=if($esDelivery){'Lima'}else{$null}; referenciaEntrega=if($esDelivery){'Puerta azul'}else{$null}; latitudEntrega=if($esDelivery){-12.0464}else{$null}; longitudEntrega=if($esDelivery){-77.0428}else{$null}
            items=$items; descuentoPct=if($i -eq 4){10}else{0}; esUrgente=($i -eq 5); recargoUrgentePct=20; montoPagado=$pagoInicial
            metodoPagoInicial=if($i % 2 -eq 0){'EFECTIVO'}else{'YAPE'}; fechaIngreso=$fecha; fechaEntregaEst=$fecha.AddDays(2); observaciones="Pedido semanal QA $($i + 1)"
        } $token
        $pedidos += $pedido
    }
    Verificar ($pedidos.Count -eq 7) 'Siete pedidos creados en fechas de una semana'

    # Flujo completo del primer pedido: cada área, entrega con pago mixto y validación de saldo.
    $primero = $pedidos[0]
    $areas = Invocar-Api GET '/areas-lavado' $null $token
    # El pedido nace en la primera área: se necesitan tantos avances como áreas activas
    # para llegar a LISTO (el último avance sale de Empacado y marca LISTO).
    foreach ($area in ($areas | Sort-Object orden)) { Invocar-Api POST "/pedidos/$($primero.id)/siguiente-area" @{} $token | Out-Null }
    $primeroActual = Invocar-Api GET "/pedidos/$($primero.id)" $null $token
    $entregas = @($primeroActual.items | ForEach-Object { @{ pedidoItemId=$_.id; cantidad=$_.cantidad } })
    $saldo = [decimal]$primeroActual.total - [decimal]$primeroActual.montoPagado
    $mitad = [math]::Round($saldo / 2, 2)
    $resultadoEntrega = Invocar-Api POST "/pedidos/$($primero.id)/entregar" @{ items=$entregas; pagos=@(@{ monto=$mitad; metodo='EFECTIVO' }, @{ monto=($saldo-$mitad); metodo='YAPE' }); recibidoPor='Cliente QA 1'; nota='Entrega final QA' } $token
    $primeroFinal = Invocar-Api GET "/pedidos/$($primero.id)" $null $token
    Verificar ($resultadoEntrega.estadoProceso -eq 'ENTREGADO' -and $primeroFinal.estadoPago -eq 'PAGADO') 'Flujo de áreas, entrega final y pago mixto'

    # Deja un segundo pedido LISTO sin entregar: valida el almacén/custodia, que debe mostrar
    # prendas terminadas pendientes de recojo.
    $segundo = $pedidos[1]
    foreach ($area in ($areas | Sort-Object orden)) { Invocar-Api POST "/pedidos/$($segundo.id)/siguiente-area" @{} $token | Out-Null }
    $segundoFinal = Invocar-Api GET "/pedidos/$($segundo.id)" $null $token
    Verificar ($segundoFinal.estadoProceso -eq 'LISTO') 'Pedido listo pendiente de recojo para validar almacén'

    # 5) Caja: gasto, consulta de movimientos y cuadre calculado por el servidor.
    Invocar-Api POST '/caja/gastos' @{ monto=12; metodoPago='EFECTIVO'; tipoGastoId=$tipoGasto.id; descripcion='Movilidad QA' } $token | Out-Null
    $hoyTexto = (Get-Date -Format 'yyyy-MM-dd')
    $movsHoy = @(Invocar-Api GET "/caja/movimientos?fecha=$hoyTexto" $null $token)
    # Invoke-RestMethod puede devolver el arreglo JSON como un único objeto desde una función;
    # se aplana explícitamente antes de filtrar los movimientos.
    if ($movsHoy.Count -eq 1 -and $movsHoy[0] -is [System.Array]) { $movsHoy = @($movsHoy[0]) }
    # -Property evita que PowerShell interprete el nombre de propiedad como un argumento
    # posicional al pasar por el helper de API.
    $efectivoIngresos = [decimal](($movsHoy | Where-Object { $_.tipo -eq 'INGRESO' -and $_.metodoPago -eq 'EFECTIVO' } | Measure-Object -Property monto -Sum).Sum)
    $efectivoGastos = [decimal](($movsHoy | Where-Object { $_.tipo -eq 'GASTO' -and $_.metodoPago -eq 'EFECTIVO' } | Measure-Object -Property monto -Sum).Sum)
    $cajaInicial = 100
    $esperado = $cajaInicial + $efectivoIngresos - $efectivoGastos
    $cuadre = Invocar-Api POST '/caja/cuadres' @{ fecha=(Get-Date).Date; cajaInicial=$cajaInicial; totalContado=$esperado; corte=50; nota='Cierre QA'; observaciones='Semana simulada'; detalleConteo='{"50":1}' } $token
    Verificar ([decimal]$cuadre.diferencia -eq 0 -and [decimal]$cuadre.cajaFinal -eq ($esperado - 50)) ("Cuadre de caja sin diferencia y corte calculado (ingresos efectivo={0}; gastos efectivo={1}; esperado={2}; contado={3}; diferencia={4})" -f $efectivoIngresos,$efectivoGastos,$esperado,$cuadre.totalContado,$cuadre.diferencia)

    # 6) Reportes y panel del propietario: lectura de datos operativos aislados.
    $desde = $inicioSemana.ToString('yyyy-MM-dd'); $hasta = (Get-Date).ToString('yyyy-MM-dd')
    $reporteGeneral = Invocar-Api GET "/reportes/general?desde=$desde&hasta=$hasta" $null $token
    $reporteServicios = Invocar-Api GET "/reportes/servicios?desde=$desde&hasta=$hasta" $null $token
    $reporteAlmacen = Invocar-Api GET '/reportes/almacen' $null $token
    $cuadresReporte = Invocar-Api GET "/reportes/cuadres-caja?desde=$desde&hasta=$hasta" $null $token
    $fichaFinal = Invocar-Api GET "/negocios/$empresaId" $null $owner.accessToken
    $cantGeneral = @($reporteGeneral.filas).Count; $cantServicios = @($reporteServicios.filas).Count
    $cantAlmacen = @($reporteAlmacen.filas).Count; $cantCuadres = @($cuadresReporte.filas).Count
    Verificar ($cantGeneral -gt 0 -and $cantServicios -gt 0 -and $cantAlmacen -gt 0 -and $cantCuadres -gt 0) ("Reportes general, servicios, almacén y cuadres responden con datos (general={0}; servicios={1}; almacén={2}; cuadres={3})" -f $cantGeneral,$cantServicios,$cantAlmacen,$cantCuadres)
    Verificar ($fichaFinal.sedes.Count -eq 2 -and $fichaFinal.pedidosMes -ge 7) 'Panel del dueño refleja sedes y pedidos del cliente QA'

    if (-not $ConservarEmpresa) {
        Invocar-Api PATCH "/negocios/$empresaId/estado" @{ activo=$false } $owner.accessToken | Out-Null
    }

    [pscustomobject]@{
        Estado = 'APROBADO'
        EmpresaQa = $nombre
        Slug = $slug
        EmpresaId = $empresaId
        ConservadaActiva = [bool]$ConservarEmpresa
        Verificaciones = $verificaciones
    } | ConvertTo-Json -Depth 6
}
catch {
    Write-Error $_
    exit 1
}
