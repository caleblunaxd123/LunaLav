using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lavanderia.Api.Controllers;

/// <summary>Pantalla pública que el repartidor abre en su celular (por un token de ruta) para
/// compartir su ubicación en vivo y marcar los hitos del reparto. No requiere sesión: el token
/// del link es la credencial. Reutiliza la lógica de pedidos para marcar la entrega.</summary>
[ApiController]
[Route("api/repartidor")]
[AllowAnonymous]
public class RepartidorController : ControllerBase
{
    private readonly IRutaRepartoRepository _rutas;
    private readonly IConfiguracionNegocioRepository _configNegocio;
    private readonly IPedidoService _pedidoService;
    private readonly ILogger<RepartidorController> _log;

    public RepartidorController(
        IRutaRepartoRepository rutas,
        IConfiguracionNegocioRepository configNegocio,
        IPedidoService pedidoService,
        ILogger<RepartidorController> log)
    {
        _rutas = rutas;
        _configNegocio = configNegocio;
        _pedidoService = pedidoService;
        _log = log;
    }

    [HttpGet("{token:guid}")]
    [EnableRateLimiting("public-read")]
    public async Task<ActionResult<RepartidorPedidoDto>> Obtener(Guid token, CancellationToken ct)
    {
        var r = await _rutas.ObtenerPorTokenAsync(token, ct);
        if (r is null) return NotFound(new { mensaje = "Link no encontrado o inválido." });

        var config = await _configNegocio.ObtenerAsync(r.NegocioId, ct);
        var distancia = SeguimientoRutaCalculo.DistanciaAlDestino(r);

        return Ok(new RepartidorPedidoDto
        {
            NombreNegocio = config?.NombreNegocio ?? "Lavandería",
            ColorPrimario = string.IsNullOrWhiteSpace(config?.ColorPrimario) ? "#0b57d0" : config!.ColorPrimario,
            NumeroPedido = r.Numero,
            ClienteNombre = r.ClienteNombre,
            ClienteCelular = r.ClienteCelular,
            DireccionEntrega = r.DireccionEntrega,
            DistritoEntrega = r.DistritoEntrega,
            ReferenciaEntrega = r.ReferenciaEntrega,
            LatitudEntrega = r.LatitudEntrega,
            LongitudEntrega = r.LongitudEntrega,
            Saldo = r.Saldo,
            Anulado = r.Anulado,
            Entregado = string.Equals(r.EstadoProceso, "ENTREGADO", StringComparison.OrdinalIgnoreCase),
            EstadoRuta = SeguimientoRutaCalculo.DeterminarEstado(r, distancia),
            RutaIniciadaEn = r.RutaIniciadaEn,
            YapeNumero = config?.YapeNumero,
            YapeTitular = config?.YapeTitular,
            YapeQrUrl = config?.YapeQrUrl
        });
    }

    [HttpPost("{token:guid}/iniciar-ruta")]
    [EnableRateLimiting("public-write")]
    public async Task<IActionResult> IniciarRuta(Guid token, CancellationToken ct)
    {
        var r = await _rutas.ObtenerPorTokenAsync(token, ct);
        if (r is null) return NotFound(new { mensaje = "Link no encontrado o inválido." });
        if (r.Anulado) return BadRequest(new { mensaje = "Este pedido fue anulado." });
        if (!string.Equals(r.EstadoProceso, "LISTO", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "El pedido todavía no está listo para salir a ruta." });

        await _rutas.IniciarRutaAsync(r.PedidoId, ct);
        await _rutas.MarcarNotifAsync(r.PedidoId, "ruta", ct);
        return NoContent();
    }

    [HttpPost("{token:guid}/ubicacion")]
    [EnableRateLimiting("repartidor-gps")]
    public async Task<ActionResult<UbicacionRepartidorResultDto>> Ubicacion(
        Guid token, [FromBody] UbicacionRepartidorRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return BadRequest(new { mensaje = "Coordenadas inválidas." });

        var r = await _rutas.ObtenerPorTokenAsync(token, ct);
        if (r is null) return NotFound(new { mensaje = "Link no encontrado o inválido." });
        if (r.Anulado || string.Equals(r.EstadoProceso, "ENTREGADO", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "Este pedido ya no está en reparto." });

        if (r.RutaIniciadaEn is null)
            return BadRequest(new { mensaje = "Inicia la ruta antes de compartir la ubicacion." });

        await _rutas.ActualizarUbicacionAsync(r.PedidoId, req.Lat, req.Lng, ct);

        // Recalcula con la posición recién guardada para devolver estado/distancia al repartidor
        // y marcar (una sola vez) los hitos de "cerca" y "llegó".
        r.MotorizadoLat = req.Lat;
        r.MotorizadoLng = req.Lng;
        r.MotorizadoUbicadoEn = DateTime.Now;
        var distancia = SeguimientoRutaCalculo.DistanciaAlDestino(r);
        var estado = SeguimientoRutaCalculo.DeterminarEstado(r, distancia);

        if ((estado is "CERCA" or "LLEGO") && !r.NotifCercaEnviada)
            await _rutas.MarcarNotifAsync(r.PedidoId, "cerca", ct);
        if (estado is "LLEGO" && !r.NotifLlegadaEnviada)
            await _rutas.MarcarNotifAsync(r.PedidoId, "llegada", ct);

        return Ok(new UbicacionRepartidorResultDto
        {
            EstadoRuta = estado,
            DistanciaMetros = distancia,
            EtaMinutos = SeguimientoRutaCalculo.EtaMinutos(distancia)
        });
    }

    [HttpPost("{token:guid}/entregado")]
    [EnableRateLimiting("public-write")]
    public async Task<IActionResult> Entregado(Guid token, CancellationToken ct)
    {
        var r = await _rutas.ObtenerPorTokenAsync(token, ct);
        if (r is null) return NotFound(new { mensaje = "Link no encontrado o inválido." });
        if (r.Anulado) return BadRequest(new { mensaje = "Este pedido fue anulado." });
        if (string.Equals(r.EstadoProceso, "ENTREGADO", StringComparison.OrdinalIgnoreCase))
            return NoContent();
        if (!string.Equals(r.EstadoProceso, "LISTO", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "El pedido todavía no está listo para entregar." });

        try
        {
            await _pedidoService.AvanzarSiguienteAreaAsync(r.PedidoId, null, r.SedeId, null, "REPARTIDOR", ct);
            await _rutas.MarcarNotifAsync(r.PedidoId, "llegada", ct);
            await _rutas.RevocarTokenAsync(r.PedidoId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            // Caso típico: saldo pendiente (delivery contra entrega). El repartidor debe coordinar el cobro.
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}
