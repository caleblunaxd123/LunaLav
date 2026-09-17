using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lavanderia.Api.Controllers;

[ApiController]
[Route("api/pago-publico")]
[AllowAnonymous]
public class PagoPublicoController : ControllerBase
{
    private readonly IPagosRepository _pagos;
    private readonly IPedidoRepository _pedidos;
    private readonly IPedidoService _pedidoService;
    private readonly IConfiguracionNegocioRepository _configNegocio;
    private readonly IRutaRepartoRepository _rutas;
    private readonly IPedidoFotoRepository _fotos;
    private readonly IAlmacenamientoFotos _almacen;

    public PagoPublicoController(
        IPagosRepository pagos,
        IPedidoRepository pedidos,
        IPedidoService pedidoService,
        IConfiguracionNegocioRepository configNegocio,
        IRutaRepartoRepository rutas,
        IPedidoFotoRepository fotos,
        IAlmacenamientoFotos almacen)
    {
        _pagos = pagos;
        _pedidos = pedidos;
        _pedidoService = pedidoService;
        _configNegocio = configNegocio;
        _rutas = rutas;
        _fotos = fotos;
        _almacen = almacen;
    }

    [HttpGet("{token:guid}")]
    [EnableRateLimiting("public-read")]
    public async Task<ActionResult<SeguimientoPedidoDto>> Obtener(Guid token, CancellationToken ct)
    {
        var solicitud = await _pagos.ObtenerPorTokenAsync(token, ct);
        if (solicitud is null || solicitud.FechaExpiracion <= DateTime.Now)
            return NotFound(new { mensaje = "El enlace no existe o ya expiró. Solicita uno nuevo a la lavandería." });

        var pedido = await _pedidos.ObtenerPorIdAsync(solicitud.PedidoId, solicitud.SedeId, ct);
        if (pedido is null) return NotFound(new { mensaje = "El pedido ya no existe." });

        var config = await _configNegocio.ObtenerAsync(solicitud.NegocioId, ct);
        var saldo = Math.Max(0m, pedido.Total - pedido.MontoPagado);

        // Seguimiento en vivo del reparto: posición del repartidor, estado de ruta y ETA.
        var ruta = await _rutas.ObtenerPorPedidoAsync(pedido.Id, solicitud.SedeId, ct);
        var distancia = ruta is null ? null : SeguimientoRutaCalculo.DistanciaAlDestino(ruta);
        var estadoRuta = ruta is null ? "SIN_RUTA" : SeguimientoRutaCalculo.DeterminarEstado(ruta, distancia);

        // Fotos de evidencia que el cliente puede ver (recepción/entrega de sus prendas).
        var fotos = await _fotos.ListarPorPedidoAsync(pedido.Id, solicitud.SedeId, ct);

        return Ok(new SeguimientoPedidoDto
        {
            NombreNegocio = config?.NombreNegocio ?? "Lavandería",
            LogoUrl = config?.LogoUrl,
            ColorPrimario = string.IsNullOrWhiteSpace(config?.ColorPrimario) ? "#0b57d0" : config!.ColorPrimario,
            TelefonoNegocio = config?.Telefono,
            DireccionNegocio = config?.Direccion,
            NumeroPedido = pedido.Numero,
            Modalidad = pedido.Modalidad,
            DireccionEntrega = pedido.DireccionEntrega,
            DistritoEntrega = pedido.DistritoEntrega,
            ReferenciaEntrega = pedido.ReferenciaEntrega,
            LatitudEntrega = pedido.LatitudEntrega,
            LongitudEntrega = pedido.LongitudEntrega,
            ResumenEstado = ConstruirResumenEstado(pedido),
            FechaCompromiso = pedido.FechaEntregaEst,
            EtiquetaFechaCompromiso = EtiquetaFechaCompromiso(pedido.Modalidad),
            Pasos = ConstruirPasos(pedido),
            Items = pedido.Items
                .Select(i => new SeguimientoPedidoItemDto(i.ServicioNombre ?? "Servicio", i.Cantidad))
                .ToList(),
            Anulado = pedido.Anulado,
            Total = pedido.Total,
            MontoPagado = pedido.MontoPagado,
            Saldo = saldo,
            RequierePago = false,
            ProveedorPagos = "IZIPAY",
            MensajePagoOnline = saldo > 0.01m
                ? "El pago online con Izipay estará disponible cuando la pasarela termine su validación. Coordina el pago directamente con la lavandería."
                : null,
            MotorizadoNombre = pedido.MotorizadoNombre,
            MotorizadoCelular = pedido.MotorizadoCelular,
            PuedeReprogramar = !pedido.Anulado && pedido.EstadoProceso != "ENTREGADO",
            EstadoRuta = estadoRuta,
            RutaIniciadaEn = ruta?.RutaIniciadaEn,
            MotorizadoLat = ruta?.MotorizadoLat,
            MotorizadoLng = ruta?.MotorizadoLng,
            MotorizadoUbicadoEn = ruta?.MotorizadoUbicadoEn,
            DistanciaMetros = distancia,
            EtaMinutos = SeguimientoRutaCalculo.EtaMinutos(distancia),
            Fotos = fotos.Select(f => new SeguimientoFotoDto(f.Id, f.Momento, f.FechaSubida)).ToList()
        });
    }

    /// <summary>Sirve una foto de evidencia al cliente desde su enlace público (sin login),
    /// validando que la foto pertenezca al pedido de ese token.</summary>
    [HttpGet("{token:guid}/fotos/{fotoId:int}")]
    [EnableRateLimiting("public-read")]
    public async Task<IActionResult> Foto(Guid token, int fotoId, CancellationToken ct)
    {
        var solicitud = await _pagos.ObtenerPorTokenAsync(token, ct);
        if (solicitud is null || solicitud.FechaExpiracion <= DateTime.Now) return NotFound();

        var foto = await _fotos.ObtenerParaPedidoAsync(fotoId, solicitud.PedidoId, ct);
        if (foto is null) return NotFound();

        var stream = _almacen.Abrir(foto.NegocioId, foto.PedidoId, foto.NombreArchivo);
        if (stream is null) return NotFound();
        return File(stream, foto.ContentType);
    }

    /// <summary>Permite al cliente, desde el link público, pedir una nueva fecha/hora de
    /// entrega o recojo sin tener que llamar a la lavandería.</summary>
    [HttpPost("{token:guid}/reprogramar")]
    [EnableRateLimiting("public-write")]
    public async Task<IActionResult> Reprogramar(Guid token, [FromBody] ReprogramarPedidoPublicoRequest req, CancellationToken ct)
    {
        var solicitud = await _pagos.ObtenerPorTokenAsync(token, ct);
        if (solicitud is null || solicitud.FechaExpiracion <= DateTime.Now)
            return NotFound(new { mensaje = "El enlace no existe o ya expiró. Solicita uno nuevo a la lavandería." });

        var pedido = await _pedidos.ObtenerPorIdAsync(solicitud.PedidoId, solicitud.SedeId, ct);
        if (pedido is null) return NotFound(new { mensaje = "El pedido ya no existe." });

        // El personal puede poner cualquier fecha desde el panel, pero desde el link publico
        // (sin autenticacion) se acota a una ventana razonable para evitar reprogramaciones absurdas.
        if (req.NuevaFecha < DateTime.Now.AddHours(2))
            return BadRequest(new { mensaje = "La nueva fecha debe tener al menos dos horas de anticipación." });
        if (req.NuevaFecha > DateTime.Now.AddDays(30))
            return BadRequest(new { mensaje = "Elige una fecha dentro de los próximos 30 días." });

        try
        {
            await _pedidoService.CambiarFechaEntregaAsync(
                pedido.Id,
                new CambiarFechaEntregaRequest { Fecha = req.NuevaFecha, Motivo = "Reprogramado por el cliente desde el portal público" },
                null,
                solicitud.SedeId,
                "CLIENTE_PORTAL",
                ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    private static List<PasoSeguimientoDto> ConstruirPasos(Pedido pedido)
    {
        var codigos = new[] { "PENDIENTE", "EN_PROCESO", "LISTO", "ENTREGADO" };
        var nombres = new Dictionary<string, string>
        {
            ["PENDIENTE"] = pedido.Modalidad switch
            {
                "Recojo" => "Recojo programado",
                "Delivery" => "Recojo programado",
                _ => "Pedido recibido"
            },
            ["EN_PROCESO"] = pedido.Modalidad switch
            {
                "Recojo" or "Delivery" => "En lavandería",
                _ => "En proceso"
            },
            ["LISTO"] = pedido.Modalidad switch
            {
                "Delivery" => "En ruta",
                _ => "Listo para recoger"
            },
            ["ENTREGADO"] = pedido.Modalidad switch
            {
                "Delivery" => "Entregado",
                _ => "Entregado"
            }
        };

        var idxActual = Array.IndexOf(codigos, pedido.EstadoProceso);
        return codigos.Select((codigo, index) => new PasoSeguimientoDto(
            codigo,
            nombres[codigo],
            Alcanzado: idxActual >= 0 && index <= idxActual,
            Actual: index == idxActual
        )).ToList();
    }

    private static string ConstruirResumenEstado(Pedido pedido)
    {
        if (pedido.Anulado) return "Este pedido fue anulado.";

        return pedido.EstadoProceso switch
        {
            "PENDIENTE" => pedido.Modalidad switch
            {
                "Recojo" or "Delivery" => "Ya registramos tu pedido y lo dejamos listo para el servicio a domicilio.",
                _ => "Tu pedido fue recibido correctamente."
            },
            "EN_PROCESO" => "Estamos trabajando tu pedido.",
            "LISTO" => pedido.Modalidad switch
            {
                "Delivery" => "Tu pedido está listo y saldrá a ruta.",
                _ => "Tu pedido ya está listo para recoger."
            },
            "ENTREGADO" => pedido.Modalidad == "Delivery"
                ? "El pedido ya fue entregado."
                : "El pedido ya fue entregado o retirado.",
            _ => "Puedes seguir el avance de tu pedido desde esta página."
        };
    }

    private static string EtiquetaFechaCompromiso(string modalidad)
    {
        return modalidad == "Delivery"
            ? "Entrega estimada"
            : "Fecha estimada";
    }
}
