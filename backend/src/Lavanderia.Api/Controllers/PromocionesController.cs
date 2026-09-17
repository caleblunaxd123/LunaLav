using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/[controller]")]
[Authorize(Roles = "ADMIN")]
[Authorize(Policy = "Modulo:PROMOCIONES")]
public class PromocionesController : TenantAwareControllerBase
{
    private readonly IPromocionRepository _repo;
    private readonly IServicioRepository _servicios;
    private readonly IClienteRepository _clientes;
    private readonly IConfiguracionNegocioRepository _config;
    public PromocionesController(
        IPromocionRepository repo,
        IServicioRepository servicios,
        IClienteRepository clientes,
        IConfiguracionNegocioRepository config)
    {
        _repo = repo;
        _servicios = servicios;
        _clientes = clientes;
        _config = config;
    }

    [HttpGet]
    public async Task<ActionResult<List<PromocionDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodasAsync(NegocioId, ct)).Select(Map).ToList());

    [HttpPost]
    public async Task<ActionResult<PromocionDto>> Crear([FromBody] PromocionDto dto, CancellationToken ct)
    {
        var error = await ValidarAsync(dto, null, ct);
        if (error is not null) return error;

        var id = await _repo.CrearAsync(new Promocion
        {
            NegocioId = NegocioId,
            Tipo = dto.Tipo.Trim(),
            Descripcion = dto.Descripcion.Trim(),
            DescuentoPct = dto.DescuentoPct,
            DescuentoMonto = dto.DescuentoMonto,
            ServicioId = dto.ServicioId,
            CantidadMinima = dto.CantidadMinima,
            FechaInicio = dto.FechaInicio,
            FechaFin = dto.FechaFin,
            Activa = dto.Activa,
            Codigo = dto.Codigo,
            // "Un solo uso": si el negocio marca MaxUsos = 1, la promo se desactiva sola al aplicarse
            // su código (ver ConsumirPorCodigoAsync). NULL = sin límite de usos.
            MaxUsos = dto.MaxUsos
        }, ct);
        var creada = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        return CreatedAtAction(nameof(Listar), Map(creada!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] PromocionDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        var error = await ValidarAsync(dto, id, ct);
        if (error is not null) return error;

        existente.Tipo = dto.Tipo.Trim();
        existente.Descripcion = dto.Descripcion.Trim();
        existente.DescuentoPct = dto.DescuentoPct;
        existente.DescuentoMonto = dto.DescuentoMonto;
        existente.ServicioId = dto.ServicioId;
        existente.CantidadMinima = dto.CantidadMinima;
        existente.FechaInicio = dto.FechaInicio;
        existente.FechaFin = dto.FechaFin;
        existente.Activa = dto.Activa;
        existente.Codigo = dto.Codigo;
        existente.MaxUsos = dto.MaxUsos;
        await _repo.ActualizarAsync(existente, NegocioId, ct);
        return NoContent();
    }

    private async Task<ObjectResult?> ValidarAsync(PromocionDto dto, int? excluirId, CancellationToken ct)
    {
        var tiposValidos = new[] { "VOLUMEN", "FRECUENCIA", "FIJA", "CODIGO" };
        dto.Tipo = dto.Tipo.Trim().ToUpperInvariant();
        dto.Codigo = string.IsNullOrWhiteSpace(dto.Codigo) ? null : dto.Codigo.Trim().ToUpperInvariant();

        if (!tiposValidos.Contains(dto.Tipo))
            return BadRequest(new { mensaje = "El tipo de promocion no es valido." });
        if (dto.FechaInicio.HasValue && dto.FechaFin.HasValue && dto.FechaFin < dto.FechaInicio)
            return BadRequest(new { mensaje = "La fecha final no puede ser anterior a la fecha inicial." });

        var tienePorcentaje = dto.DescuentoPct is > 0;
        var tieneMonto = dto.DescuentoMonto is > 0;
        if (!tienePorcentaje && !tieneMonto)
            return BadRequest(new { mensaje = "Indica un descuento mayor a cero, en porcentaje o en soles." });
        if (tienePorcentaje && tieneMonto)
            return BadRequest(new { mensaje = "Usa solo un tipo de descuento: porcentaje o monto fijo." });

        if (dto.ServicioId is int servicioId)
        {
            var servicio = await _servicios.ObtenerPorIdAsync(servicioId, NegocioId, ct);
            if (servicio is null || !servicio.Activo)
                return BadRequest(new { mensaje = "El servicio seleccionado no existe o esta inactivo." });
        }

        if (dto.Codigo is not null)
        {
            var duplicada = await _repo.BuscarPorCodigoAsync(dto.Codigo, NegocioId, ct);
            if (duplicada is not null && duplicada.Id != excluirId)
                return Conflict(new { mensaje = "Ya existe una promocion con ese codigo." });
        }

        return null;
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoPromocionRequest req, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();
        await _repo.CambiarEstadoAsync(id, req.Activa, NegocioId, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();
        await _repo.EliminarAsync(id, NegocioId, ct);
        return NoContent();
    }

    /// <summary>
    /// Generador automático de códigos de descuento. Crea un código único, de un solo uso y
    /// (según el caso) atado a un cliente, y devuelve un mensaje listo para enviar por WhatsApp.
    /// </summary>
    [HttpPost("generar")]
    public async Task<ActionResult<CodigoGeneradoDto>> Generar([FromBody] GenerarCodigoRequest req, CancellationToken ct)
    {
        var origen = (req.Origen ?? "").Trim().ToUpperInvariant();
        if (!new[] { "NUEVO", "CUMPLE", "REFERIDO", "PUNTOS" }.Contains(origen))
            return BadRequest(new { mensaje = "El tipo de código no es válido." });

        Cliente? cliente = null;
        if (req.ClienteId is int cid)
        {
            cliente = await _clientes.ObtenerPorIdAsync(cid, NegocioId, ct);
            if (cliente is null || !cliente.Activo)
                return BadRequest(new { mensaje = "El cliente no existe o está inactivo." });
        }
        if ((origen is "CUMPLE" or "PUNTOS") && cliente is null)
            return BadRequest(new { mensaje = "Este tipo de código requiere elegir un cliente." });

        var cfg = await _config.ObtenerAsync(NegocioId, ct);
        var maxPct = cfg?.MaxDescuentoPct ?? 0m;

        decimal? descuentoPct = null;
        decimal? descuentoMonto = null;
        int? puntosGastados = null;
        int diasVigencia;
        string prefijo;
        string descripcion;

        switch (origen)
        {
            case "NUEVO":
                descuentoPct = ClampPct(req.DescuentoPct ?? 10m, maxPct);
                diasVigencia = req.DiasVigencia ?? 30;
                prefijo = "NUEVO";
                descripcion = cliente is null ? "Bienvenida cliente nuevo" : $"Bienvenida — {cliente.Nombre}";
                break;
            case "CUMPLE":
                descuentoPct = ClampPct(req.DescuentoPct ?? 15m, maxPct);
                diasVigencia = req.DiasVigencia ?? 15;
                prefijo = "CUMPLE";
                descripcion = $"Cumpleaños de {cliente!.Nombre}";
                break;
            case "REFERIDO":
                descuentoPct = ClampPct(req.DescuentoPct ?? 10m, maxPct);
                diasVigencia = req.DiasVigencia ?? 30;
                prefijo = "REF";
                descripcion = cliente is null ? "Referido de un cliente" : $"Referido de {cliente.Nombre}";
                break;
            case "PUNTOS":
                if (cfg is null || cfg.ValorPuntoCanje <= 0)
                    return BadRequest(new { mensaje = "El canje de puntos está desactivado. Actívalo en Ajustes." });
                var pts = req.PuntosACanjear ?? 0;
                if (pts <= 0) return BadRequest(new { mensaje = "Indica cuántos puntos convertir." });
                if (pts > cliente!.Puntos) return BadRequest(new { mensaje = $"El cliente solo tiene {cliente.Puntos} puntos." });
                descuentoMonto = Math.Round(pts * cfg.ValorPuntoCanje, 2);
                puntosGastados = pts;
                diasVigencia = req.DiasVigencia ?? 30;
                prefijo = "PUNTOS";
                descripcion = $"Canje de {pts} puntos — {cliente.Nombre}";
                break;
            default:
                return BadRequest(new { mensaje = "El tipo de código no es válido." });
        }

        if ((descuentoPct is null or 0m) && (descuentoMonto is null or 0m))
            return BadRequest(new { mensaje = "El descuento debe ser mayor a cero." });

        var etiquetaCliente = cliente is not null ? SoloLetras(cliente.Nombre) : null;
        var prefijoFull = string.IsNullOrEmpty(etiquetaCliente) ? prefijo : $"{prefijo}-{etiquetaCliente}";
        var codigo = await _repo.GenerarCodigoUnicoAsync(prefijoFull, NegocioId, ct);

        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var vence = hoy.AddDays(diasVigencia);
        var id = await _repo.CrearAsync(new Promocion
        {
            NegocioId = NegocioId,
            Tipo = "CODIGO",
            Descripcion = descripcion,
            DescuentoPct = descuentoPct,
            DescuentoMonto = descuentoMonto,
            CantidadMinima = 1,
            FechaInicio = hoy,
            FechaFin = vence,
            Activa = true,
            Codigo = codigo,
            ClienteId = cliente?.Id,
            Origen = origen,
            MaxUsos = 1,
            Usos = 0
        }, ct);

        // PUNTOS: descontar los puntos del cliente. Si falla, se anula el código para no dejar
        // un descuento "gratis" sin respaldo de puntos.
        if (puntosGastados is int gastados)
        {
            try
            {
                await _clientes.AgregarMovimientoPuntosAsync(new MovimientoPuntos
                {
                    ClienteId = cliente!.Id,
                    Motivo = $"Canje por código {codigo}",
                    Puntos = gastados,
                    Tipo = "RESTA",
                    UsuarioId = UsuarioId
                }, NegocioId, ct);
            }
            catch
            {
                await _repo.CambiarEstadoAsync(id, false, NegocioId, ct);
                return BadRequest(new { mensaje = "No se pudieron descontar los puntos. Intenta de nuevo." });
            }
        }

        var creada = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        return Ok(new CodigoGeneradoDto
        {
            Promocion = Map(creada!),
            Celular = cliente?.Celular,
            MensajeWhatsapp = ConstruirMensaje(origen, cfg?.NombreNegocio ?? "nuestra lavandería",
                cliente?.Nombre, codigo, descuentoPct, descuentoMonto, vence)
        });
    }

    private static decimal ClampPct(decimal pct, decimal maxPct)
    {
        pct = Math.Max(0m, Math.Min(100m, pct));
        if (maxPct > 0m) pct = Math.Min(pct, maxPct);
        return pct;
    }

    /// <summary>Primer nombre en MAYÚSCULAS, solo letras A-Z, para armar un código legible.</summary>
    private static string SoloLetras(string nombre)
    {
        var primer = nombre.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var sinTildes = primer.Normalize(System.Text.NormalizationForm.FormD);
        var chars = sinTildes.Where(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z').ToArray();
        var limpio = new string(chars).ToUpperInvariant();
        return limpio.Length > 8 ? limpio[..8] : limpio;
    }

    private static string ConstruirMensaje(string origen, string negocio, string? cliente, string codigo,
        decimal? pct, decimal? monto, DateOnly vence)
    {
        var saludo = string.IsNullOrWhiteSpace(cliente) ? "¡Hola!" : $"¡Hola {cliente}!";
        var beneficio = pct is > 0 ? $"{pct:0.#}% de descuento" : $"S/ {monto:0.00} de descuento";
        var motivo = origen switch
        {
            "NUEVO" => "Te damos la bienvenida con",
            "CUMPLE" => "🎂 ¡Feliz cumpleaños! Te regalamos",
            "REFERIDO" => "Por recomendarnos, tienes",
            "PUNTOS" => "Canjeaste tus puntos por",
            _ => "Tienes"
        };
        var venceTxt = vence.ToDateTime(TimeOnly.MinValue).ToString("dd/MM/yyyy");
        return $"{saludo} {motivo} *{beneficio}* en {negocio}.\n\n" +
               $"Tu código: *{codigo}*\n" +
               $"Válido hasta el {venceTxt}. Menciónalo al dejar tu pedido. 🧺";
    }

    private static PromocionDto Map(Promocion p) => new()
    {
        Id = p.Id,
        Tipo = p.Tipo,
        Descripcion = p.Descripcion,
        DescuentoPct = p.DescuentoPct,
        DescuentoMonto = p.DescuentoMonto,
        ServicioId = p.ServicioId,
        ServicioNombre = p.ServicioNombre,
        CantidadMinima = p.CantidadMinima,
        FechaInicio = p.FechaInicio,
        FechaFin = p.FechaFin,
        Activa = p.Activa,
        Codigo = p.Codigo,
        ClienteId = p.ClienteId,
        ClienteNombre = p.ClienteNombre,
        Origen = p.Origen,
        MaxUsos = p.MaxUsos,
        Usos = p.Usos
    };
}
