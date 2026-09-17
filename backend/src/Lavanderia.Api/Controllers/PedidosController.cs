using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/[controller]")]
public class PedidosController : TenantAwareControllerBase
{
    private readonly IPedidoService _service;
    private readonly IPromocionRepository _promociones;
    private readonly IRutaRepartoRepository _rutas;
    private readonly IPedidoRepository _pedidos;
    public PedidosController(IPedidoService service, IPromocionRepository promociones, IRutaRepartoRepository rutas, IPedidoRepository pedidos)
    {
        _service = service;
        _promociones = promociones;
        _rutas = rutas;
        _pedidos = pedidos;
    }

    /// <summary>Barras de tendencia de la ventana de Pedidos: recibidos y entregados por día.</summary>
    [HttpGet("tendencia")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<TendenciaPedidosDto>> Tendencia([FromQuery] int dias = 14, CancellationToken ct = default)
    {
        dias = Math.Clamp(dias, 7, 60);
        var desde = Lavanderia.Api.Infrastructure.TendenciaBuilder.DesdeDias(dias);
        var recibidos = await _pedidos.ContarPorDiaAsync(desde, porEntrega: false, SedeRequeridaId, ct);
        var entregados = await _pedidos.ContarPorDiaAsync(desde, porEntrega: true, SedeRequeridaId, ct);
        return Ok(new TendenciaPedidosDto(
            Lavanderia.Api.Infrastructure.TendenciaBuilder.SerieDiaria(recibidos, dias),
            Lavanderia.Api.Infrastructure.TendenciaBuilder.SerieDiaria(entregados, dias)));
    }

    /// <summary>Tendencia de ventas (S/ por día) del dashboard, para el rango de días elegido.</summary>
    [HttpGet("ventas-tendencia")]
    [Authorize(Policy = "Modulo:INICIO")]
    public async Task<ActionResult<List<PuntoTendenciaDto>>> VentasTendencia([FromQuery] int dias = 30, CancellationToken ct = default)
    {
        dias = Math.Clamp(dias, 7, 180);
        var desde = DateTime.Today.AddDays(-(dias - 1));
        var porDia = await _pedidos.VentasPorDiaAsync(desde, SedeRequeridaId, ct);
        var serie = new List<PuntoTendenciaDto>(dias);
        for (var i = 0; i < dias; i++)
        {
            var d = desde.AddDays(i);
            serie.Add(new PuntoTendenciaDto(d.ToString("yyyy-MM-dd"), porDia.TryGetValue(d.Date, out var v) ? v : 0m));
        }
        return Ok(serie);
    }

    [HttpGet]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<PagedResultDto<PedidoDto>>> Listar(
        [FromQuery] string? filtro,
        [FromQuery] string? busqueda,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        [FromQuery] string? campoFecha,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 15,
        CancellationToken ct = default)
        => Ok(await _service.ListarPaginadoAsync(
            filtro, busqueda, desde, hasta, campoFecha,
            Math.Max(1, pagina), Math.Clamp(tamanoPagina, 1, 200), SedeRequeridaId, ct));

    [HttpGet("por-cliente/{clienteId:int}")]
    [Authorize(Policy = "Modulo:CLIENTES")]
    public async Task<ActionResult<PagedResultDto<PedidoDto>>> ListarPorCliente(
        int clienteId,
        [FromQuery] string? filtro,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 10,
        CancellationToken ct = default)
        => Ok(await _service.ListarPorClienteAsync(clienteId, filtro, Math.Max(1, pagina), Math.Clamp(tamanoPagina, 1, 200), SedeRequeridaId, ct));

    [HttpGet("{id:int}")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<PedidoDto>> Obtener(int id, CancellationToken ct)
    {
        var p = await _service.ObtenerAsync(id, SedeRequeridaId, ct);
        if (p is null) return NotFound();
        return Ok(p);
    }

    [HttpPost]
    [Authorize(Policy = "Modulo:REGISTRAR")]
    public async Task<ActionResult<PedidoDto>> Crear([FromBody] CrearPedidoRequest req, CancellationToken ct)
    {
        try
        {
            var pedido = await _service.CrearAsync(req, UsuarioId, NegocioId, SedeRequeridaId, ct);
            // Marca el consumo del código si se aplicó uno (los de un solo uso quedan agotados).
            // Best-effort: no debe tumbar la creación del pedido si algo falla al contabilizar.
            if (!string.IsNullOrWhiteSpace(req.CodigoPromocion))
            {
                try { await _promociones.ConsumirPorCodigoAsync(req.CodigoPromocion, NegocioId, pedido.ClienteId, ct); }
                catch { /* el descuento ya se aplicó en el total; el conteo es secundario */ }
            }
            return CreatedAtAction(nameof(Obtener), new { id = pedido.Id }, pedido);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpPost("{id:int}/avanzar")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> Avanzar(int id, [FromBody] AvanzarAreaRequest req, CancellationToken ct)
    {
        try
        {
            await _service.AvanzarAreaAsync(id, req, UsuarioId, SedeRequeridaId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>
    /// Un click: mueve el pedido a la siguiente area del flujo (o LISTO si termino).
    /// Es la operacion mas usada por el trabajador.
    /// </summary>
    [HttpPost("{id:int}/siguiente-area")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> SiguienteArea(int id, [FromBody] SiguienteAreaRequest? req, CancellationToken ct)
    {
        try
        {
            await _service.AvanzarSiguienteAreaAsync(id, UsuarioId, SedeRequeridaId, req?.RecibidoPor, "USUARIO", ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpGet("{id:int}/historial")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<List<PedidoHistorialDto>>> Historial(int id, CancellationToken ct)
        => Ok(await _service.ObtenerHistorialAsync(id, SedeRequeridaId, ct));

    /// <summary>Cobros registrados del pedido, cada uno con su metodo de pago.</summary>
    [HttpGet("{id:int}/pagos")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<List<PagoPedidoDto>>> Pagos(int id, CancellationToken ct)
        => Ok(await _service.ObtenerPagosAsync(id, SedeRequeridaId, ct));

    [HttpGet("dashboard")]
    [Authorize(Policy = "Modulo:INICIO")]
    public async Task<ActionResult<DashboardDto>> Dashboard(CancellationToken ct)
    {
        var dto = await _service.DashboardAsync(NegocioId, SedeRequeridaId, ct);
        var modulos = User.FindAll("mod").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool TieneModulo(string modulo) => User.IsInRole("ADMIN") || modulos.Contains(modulo);

        if (!TieneModulo("CAJA") && !TieneModulo("REPORTES"))
        {
            dto.CobradoDelDia = null;
            dto.SaldoPorCobrar = null;
            dto.CajaEsperadaHoy = null;
        }
        if (!TieneModulo("INVENTARIO")) dto.InsumosBajoStock = null;
        if (!TieneModulo("AJUSTES"))
        {
            dto.ComprobantesPendientes = null;
            dto.ComprobantesRechazados = null;
        }
        if (!TieneModulo("PEDIDOS"))
        {
            dto.TotalPedidosEstancados = 0;
            dto.TotalPedidosAbandonados = 0;
            dto.PedidosEstancados.Clear();
            dto.PedidosAbandonados.Clear();
        }

        return Ok(dto);
    }

    [HttpGet("contadores")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<PedidoContadoresDto>> Contadores(CancellationToken ct)
        => Ok(await _service.ContadoresAsync(SedeRequeridaId, ct));

    [HttpGet("siguiente-numero")]
    [Authorize(Policy = "Modulo:REGISTRAR")]
    public async Task<ActionResult<int>> SiguienteNumero(CancellationToken ct)
        => Ok(await _service.SiguienteNumeroAsync(SedeRequeridaId, ct));

    /// <summary>
    /// Valida un código de promoción para usarlo en Registrar. Cualquier usuario autenticado puede
    /// consultarlo (no solo ADMIN), a diferencia del CRUD de promociones.
    /// </summary>
    [HttpGet("promocion/validar")]
    [Authorize(Policy = "Modulo:REGISTRAR")]
    public async Task<ActionResult<PromocionValidaDto>> ValidarCodigoPromocion([FromQuery] string codigo, [FromQuery] int? clienteId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return BadRequest(new { mensaje = "Indica un código." });

        var promo = await _promociones.BuscarPorCodigoAsync(codigo, NegocioId, ct);
        if (promo is null) return NotFound(new { mensaje = "Código no encontrado." });
        if (!promo.Activa) return BadRequest(new { mensaje = "Esta promoción ya no está activa." });

        var hoy = DateOnly.FromDateTime(DateTime.Today);
        if (promo.FechaInicio.HasValue && hoy < promo.FechaInicio.Value)
            return BadRequest(new { mensaje = "Esta promoción todavía no empieza." });
        if (promo.FechaFin.HasValue && hoy > promo.FechaFin.Value)
            return BadRequest(new { mensaje = "Esta promoción ya venció." });

        // Códigos generados de un solo uso / personales
        if (promo.MaxUsos.HasValue && promo.Usos >= promo.MaxUsos.Value)
            return BadRequest(new { mensaje = "Este código ya fue utilizado." });
        if (promo.ClienteId.HasValue && promo.ClienteId.Value != clienteId)
            return BadRequest(new { mensaje = clienteId is null
                ? "Este código es personal: primero elige el cliente del pedido."
                : "Este código pertenece a otro cliente." });

        return Ok(new PromocionValidaDto
        {
            Id = promo.Id,
            Descripcion = promo.Descripcion,
            DescuentoPct = promo.DescuentoPct,
            DescuentoMonto = promo.DescuentoMonto,
            ServicioId = promo.ServicioId,
            CantidadMinima = promo.CantidadMinima
        });
    }

    [HttpGet("abandonados")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<List<PedidoAbandonadoDto>>> Abandonados([FromQuery] int dias = 3, CancellationToken ct = default)
        => Ok(await _service.ListarAbandonadosAsync(Math.Max(1, dias), SedeRequeridaId, ct));

    [HttpPost("{id:int}/pagos")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> RegistrarPago(int id, [FromBody] RegistrarPagoRequest req, CancellationToken ct)
    {
        try
        {
            await _service.RegistrarPagoAsync(id, req, UsuarioId, SedeRequeridaId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Entregas del pedido (parciales y final), con el detalle de lo entregado y lo cobrado.</summary>
    [HttpGet("{id:int}/entregas")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<List<PedidoEntregaDto>>> Entregas(int id, CancellationToken ct)
        => Ok(await _service.ObtenerEntregasAsync(id, SedeRequeridaId, ct));

    /// <summary>
    /// Registra una entrega parcial o final: qué prendas se lleva el cliente ahora y con qué pagos
    /// (uno o varios métodos a la vez). No exige pagar el total; el saldo queda por cobrar.
    /// </summary>
    [HttpPost("{id:int}/entregar")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<object>> Entregar(int id, [FromBody] EntregarPedidoRequest req, CancellationToken ct)
    {
        try
        {
            var estado = await _service.EntregarAsync(id, req, UsuarioId, SedeRequeridaId, ct);
            return Ok(new { estadoProceso = estado });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpPost("{id:int}/items")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> AgregarItem(int id, [FromBody] AgregarItemRequest req, CancellationToken ct)
    {
        try
        {
            await _service.AgregarItemAsync(id, req, NegocioId, SedeRequeridaId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpPut("{id:int}/fecha-entrega")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> CambiarFechaEntrega(int id, [FromBody] CambiarFechaEntregaRequest req, CancellationToken ct)
    {
        try
        {
            await _service.CambiarFechaEntregaAsync(id, req, UsuarioId, SedeRequeridaId, "USUARIO", ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Convierte un pedido de Tienda a Delivery y, si le queda saldo, genera de una
    /// vez su link de seguimiento/pago (ver <see cref="LinkSeguimiento"/>).</summary>
    [HttpPost("{id:int}/convertir-delivery")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> ConvertirDelivery(int id, [FromBody] ConvertirDeliveryRequest req, CancellationToken ct)
    {
        try
        {
            await _service.ConvertirADeliveryAsync(id, req, NegocioId, SedeRequeridaId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Asigna (o quita, con motorizadoId null) el repartidor a cargo de este pedido.</summary>
    [HttpPut("{id:int}/motorizado")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> AsignarMotorizado(int id, [FromBody] AsignarMotorizadoRequest req, CancellationToken ct)
    {
        try
        {
            await _service.AsignarMotorizadoAsync(id, req.MotorizadoId, SedeRequeridaId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Devuelve el token del link público de seguimiento/pago de este pedido,
    /// generándolo si todavía no existe uno vigente. Solo aplica a pedidos Delivery.</summary>
    [HttpGet("{id:int}/link-seguimiento")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<LinkSeguimientoDto>> LinkSeguimiento(int id, CancellationToken ct)
    {
        try
        {
            var token = await _service.ObtenerOCrearLinkPagoAsync(id, NegocioId, SedeRequeridaId, ct);
            if (token is null) return NotFound(new { mensaje = "No se pudo habilitar el seguimiento para este pedido." });
            return Ok(new LinkSeguimientoDto(token.Value));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Devuelve el token del link que el repartidor abre en su celular para compartir
    /// su ubicación en vivo. Solo aplica a pedidos Delivery.</summary>
    [HttpGet("{id:int}/link-repartidor")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<LinkRepartidorDto>> LinkRepartidor(int id, CancellationToken ct)
    {
        var ruta = await _rutas.ObtenerPorPedidoAsync(id, SedeRequeridaId, ct);
        if (ruta is null) return NotFound(new { mensaje = "Pedido no encontrado." });
        if (!string.Equals(ruta.Modalidad, "Delivery", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "El seguimiento del repartidor solo aplica a pedidos Delivery." });
        if (ruta.Anulado || ruta.EstadoProceso is "ENTREGADO" or "ANULADO" or "DONADO")
            return BadRequest(new { mensaje = "El pedido ya finalizó y no puede generar un enlace de repartidor." });

        var token = await _rutas.AsegurarTokenAsync(id, SedeRequeridaId, ct);
        return Ok(new LinkRepartidorDto(token));
    }

    [HttpPost("{id:int}/anular")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    [Authorize(Roles = "ADMIN,COORDINADOR")] // anular es acción sensible: no basta con tener el módulo
    public async Task<IActionResult> Anular(int id, [FromBody] AnularPedidoRequest req, CancellationToken ct)
    {
        try
        {
            await _service.AnularAsync(id, req.Motivo, UsuarioId, NegocioId, SedeRequeridaId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Envía a donación un pedido con mucho tiempo en custodia (desde el reporte de Almacén).</summary>
    [HttpPost("{id:int}/donar")]
    [Authorize(Policy = "Modulo:REPORTES")]
    [Authorize(Roles = "ADMIN,COORDINADOR")] // donar dispone de prendas del cliente: requiere rol de confianza
    public async Task<IActionResult> Donar(int id, CancellationToken ct)
    {
        try
        {
            await _service.DonarAsync(id, UsuarioId, SedeRequeridaId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Reenvía un pedido pendiente directo a almacén (LISTO), desde el reporte de Órdenes Pendientes.</summary>
    [HttpPost("{id:int}/reenviar-almacen")]
    [Authorize(Policy = "Modulo:REPORTES")]
    public async Task<IActionResult> ReenviarAlmacen(int id, CancellationToken ct)
    {
        try
        {
            await _service.ReenviarAlmacenAsync(id, UsuarioId, SedeRequeridaId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}
