using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/insumos")]
[Microsoft.AspNetCore.Authorization.Authorize(Policy = "Modulo:INVENTARIO")]
public class InsumosController : TenantAwareControllerBase
{
    private readonly IInsumoRepository _repo;
    private readonly ICajaRepository _caja;
    private readonly Lavanderia.Api.Infrastructure.ISqlConnectionFactory _factory;
    public InsumosController(IInsumoRepository repo, ICajaRepository caja, Lavanderia.Api.Infrastructure.ISqlConnectionFactory factory)
    {
        _repo = repo;
        _caja = caja;
        _factory = factory;
    }

    [HttpGet]
    public async Task<ActionResult<List<InsumoDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodosAsync(SedeRequeridaId, ct)).Select(Map).ToList());

    [HttpGet("bajo-stock")]
    public async Task<ActionResult<List<InsumoDto>>> BajoStock(CancellationToken ct)
        => Ok((await _repo.ListarBajoStockAsync(SedeRequeridaId, ct)).Select(Map).ToList());

    /// <summary>Barras de tendencia de la ventana de Inventario: consumo de insumos por día.</summary>
    [HttpGet("tendencia-consumo")]
    public async Task<ActionResult<List<TendenciaPuntoDto>>> TendenciaConsumo([FromQuery] int dias = 14, CancellationToken ct = default)
    {
        dias = Math.Clamp(dias, 7, 60);
        var desde = Lavanderia.Api.Infrastructure.TendenciaBuilder.DesdeDias(dias);
        var datos = await _repo.ContarConsumoPorDiaAsync(desde, SedeRequeridaId, ct);
        return Ok(Lavanderia.Api.Infrastructure.TendenciaBuilder.SerieDiaria(datos, dias));
    }

    [HttpPost]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "ADMIN")] // crear el catálogo es tarea del administrador
    public async Task<ActionResult<InsumoDto>> Crear([FromBody] InsumoDto dto, CancellationToken ct)
    {
        var nombre = dto.Nombre.Trim();
        var unidad = dto.UnidadMedida.Trim();
        if (await _repo.ExisteNombreAsync(nombre, SedeRequeridaId, ct: ct))
            return Conflict(new { mensaje = "Ya existe un insumo con ese nombre en esta sede." });

        var id = await _repo.CrearAsync(new Insumo
        {
            SedeId = SedeRequeridaId,
            Nombre = nombre,
            UnidadMedida = unidad,
            Clase = NormalizarClase(dto.Clase),
            ContenidoValor = dto.ContenidoValor is > 0 ? dto.ContenidoValor : null,
            ContenidoUnidad = string.IsNullOrWhiteSpace(dto.ContenidoUnidad) ? null : dto.ContenidoUnidad.Trim(),
            StockActual = Math.Max(0, dto.StockActual),
            StockMinimo = Math.Max(0, dto.StockMinimo),
            Activo = dto.Activo,
            FechaIngreso = dto.FechaIngreso,
            FechaVencimiento = dto.FechaVencimiento
        }, ct);
        var creado = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        return CreatedAtAction(nameof(Listar), Map(creado!));
    }

    /// <summary>Carga masiva de insumos. Valida fila por fila, omite duplicados por nombre
    /// (dentro del archivo y contra la sede) y filas inválidas, sin abortar el resto.</summary>
    [HttpPost("importar")]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<ImportarInsumosResultado>> Importar([FromBody] ImportarInsumosRequest req, CancellationToken ct)
    {
        if (req.Filas is null || req.Filas.Count == 0)
            return BadRequest(new { mensaje = "No se recibió ninguna fila para importar." });
        if (req.Filas.Count > 1000)
            return BadRequest(new { mensaje = "Máximo 1000 insumos por importación. Divide el archivo en partes." });

        var resultado = new ImportarInsumosResultado();
        var nombresLote = new HashSet<string>();
        var fila = 0;
        foreach (var f in req.Filas)
        {
            fila++;
            var nombre = (f.Nombre ?? "").Trim();
            var unidad = (f.UnidadMedida ?? "").Trim();

            if (nombre.Length == 0 && unidad.Length == 0 && f.StockActual == 0 && f.StockMinimo == 0) continue; // fila vacía

            if (nombre.Length < 2 || nombre.Length > 80)
            { resultado.Errores.Add(new() { Fila = fila, Nombre = nombre, Motivo = "Nombre inválido (2 a 80 caracteres)." }); continue; }
            if (unidad.Length == 0) unidad = "und";
            if (unidad.Length > 20) unidad = unidad[..20];
            if (f.StockActual < 0 || f.StockActual > 1_000_000)
            { resultado.Errores.Add(new() { Fila = fila, Nombre = nombre, Motivo = "Stock actual fuera de rango." }); continue; }
            if (f.StockMinimo < 0 || f.StockMinimo > 1_000_000)
            { resultado.Errores.Add(new() { Fila = fila, Nombre = nombre, Motivo = "Stock mínimo fuera de rango." }); continue; }

            if (!nombresLote.Add(nombre.ToUpperInvariant()))
            { resultado.Omitidos++; resultado.Errores.Add(new() { Fila = fila, Nombre = nombre, Motivo = "Repetido dentro del archivo." }); continue; }
            if (await _repo.ExisteNombreAsync(nombre, SedeRequeridaId, ct: ct))
            { resultado.Omitidos++; resultado.Errores.Add(new() { Fila = fila, Nombre = nombre, Motivo = "Ya existe un insumo con ese nombre." }); continue; }

            await _repo.CrearAsync(new Insumo
            {
                SedeId = SedeRequeridaId,
                Nombre = nombre,
                UnidadMedida = unidad,
                StockActual = f.StockActual,
                StockMinimo = f.StockMinimo,
                Clase = NormalizarClase(f.Clase),
                Activo = true
            }, ct);
            resultado.Creados++;
        }

        return Ok(resultado);
    }

    [HttpPut("{id:int}")]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "ADMIN")] // editar el insumo: solo administrador
    public async Task<IActionResult> Actualizar(int id, [FromBody] InsumoDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (existente is null) return NotFound();

        var nombre = dto.Nombre.Trim();
        if (await _repo.ExisteNombreAsync(nombre, SedeRequeridaId, id, ct))
            return Conflict(new { mensaje = "Ya existe otro insumo con ese nombre en esta sede." });

        existente.Nombre = nombre;
        existente.UnidadMedida = dto.UnidadMedida.Trim();
        existente.Clase = NormalizarClase(dto.Clase);
        existente.ContenidoValor = dto.ContenidoValor is > 0 ? dto.ContenidoValor : null;
        existente.ContenidoUnidad = string.IsNullOrWhiteSpace(dto.ContenidoUnidad) ? null : dto.ContenidoUnidad.Trim();
        existente.StockMinimo = Math.Max(0, dto.StockMinimo);
        existente.Activo = dto.Activo;
        existente.FechaIngreso = dto.FechaIngreso;
        existente.FechaVencimiento = dto.FechaVencimiento;
        await _repo.ActualizarAsync(existente, SedeRequeridaId, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (existente is null) return NotFound();
        if (await Lavanderia.Api.Infrastructure.CatalogoEliminacion.EliminarCatalogoAsync(_factory, "Insumo", "SedeId", id, SedeRequeridaId, ct))
            return Ok(new { mensaje = "Insumo eliminado.", eliminado = true });
        await _repo.CambiarEstadoAsync(id, false, SedeRequeridaId, ct);
        return Ok(new { mensaje = "No se puede eliminar: el insumo tiene movimientos registrados. Se desactivó.", eliminado = false });
    }

    [HttpPatch("{id:int}/estado")]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoUsuarioRequest req, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (existente is null) return NotFound();
        await _repo.CambiarEstadoAsync(id, req.Activo, SedeRequeridaId, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/movimientos")]
    public async Task<IActionResult> RegistrarMovimiento(int id, [FromBody] RegistrarMovimientoInsumoRequest req, CancellationToken ct)
    {
        req.Tipo = req.Tipo.Trim().ToUpperInvariant();
        var tiposValidos = new[] { "COMPRA", "CONSUMO", "AJUSTE" };
        if (!tiposValidos.Contains(req.Tipo)) return BadRequest(new { mensaje = "Tipo de movimiento inválido." });

        var insumo = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (insumo is null) return NotFound();
        if (!insumo.Activo)
            return Conflict(new { mensaje = "El insumo está inactivo. Reactívalo antes de registrar movimientos." });

        if (req.Tipo != "AJUSTE" && req.Cantidad <= 0)
            return BadRequest(new { mensaje = "La cantidad debe ser mayor a 0." });
        if (req.Tipo == "AJUSTE" && req.Cantidad == 0)
            return BadRequest(new { mensaje = "El ajuste debe aumentar o disminuir el stock; la cantidad no puede ser 0." });

        if (req.CostoTotal is < 0)
            return BadRequest(new { mensaje = "El costo total no puede ser negativo." });

        if (req.Fecha is DateTime fecha && fecha.Date > DateTime.Today)
            return BadRequest(new { mensaje = "La fecha de compra no puede estar en el futuro." });

        if (!string.IsNullOrWhiteSpace(req.MetodoPago))
        {
            req.MetodoPago = req.MetodoPago.Trim().ToUpperInvariant();
            var metodosValidos = new[] { "EFECTIVO", "YAPE", "PLIN", "TRANSFERENCIA", "POS", "TARJETA" };
            if (!metodosValidos.Contains(req.MetodoPago))
                return BadRequest(new { mensaje = "Método de pago inválido." });
        }

        if (req.Tipo == "CONSUMO" && req.Cantidad > insumo.StockActual)
            return BadRequest(new { mensaje = $"No hay suficiente stock. Disponible: {insumo.StockActual} {insumo.UnidadMedida}." });

        if (req.Tipo == "COMPRA" && req.TipoGastoId is int tipoGastoId)
        {
            var tipoGasto = await _caja.ObtenerTipoGastoPorIdAsync(tipoGastoId, NegocioId, ct);
            if (tipoGasto is null)
                return BadRequest(new { mensaje = "El tipo de gasto no pertenece a este negocio." });
        }

        var movimiento = new MovimientoInsumo
        {
            SedeId = SedeRequeridaId,
            InsumoId = id,
            InsumoNombre = insumo.Nombre,
            Tipo = req.Tipo,
            Cantidad = req.Cantidad,
            CostoTotal = req.CostoTotal,
            // Cualquier tipo puede fecharse en el pasado (registrar un movimiento atrasado).
            // La hora se pone "ahora" para conservar el orden dentro del día.
            Fecha = req.Fecha is DateTime f ? f.Date + DateTime.Now.TimeOfDay : DateTime.Now,
            UsuarioId = UsuarioId,
            Descripcion = req.Descripcion
        };

        try
        {
            var movimientoId = await _repo.RegistrarMovimientoAsync(movimiento, req.MetodoPago, req.TipoGastoId, ct);
            return Ok(new { id = movimientoId, mensaje = "Movimiento registrado." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    /// <summary>Corrige la fecha y la nota de un movimiento ya registrado (no cambia el stock). Solo ADMIN.</summary>
    [HttpPut("movimientos/{id:int}")]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> EditarMovimiento(int id, [FromBody] EditarMovimientoInsumoRequest req, CancellationToken ct)
    {
        if (req.Fecha.Date > DateTime.Today)
            return BadRequest(new { mensaje = "La fecha no puede estar en el futuro." });
        var descripcion = string.IsNullOrWhiteSpace(req.Descripcion) ? null : req.Descripcion.Trim();
        var ok = await _repo.EditarMovimientoAsync(id, req.Fecha, descripcion, SedeRequeridaId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>Elimina un movimiento y revierte su efecto en el stock (y el gasto de caja vinculado). Solo ADMIN.</summary>
    [HttpDelete("movimientos/{id:int}")]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> EliminarMovimiento(int id, CancellationToken ct)
    {
        try
        {
            var ok = await _repo.EliminarMovimientoAsync(id, SedeRequeridaId, ct);
            return ok ? Ok(new { mensaje = "Movimiento eliminado y stock corregido." }) : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpGet("movimientos")]
    public async Task<ActionResult<List<MovimientoInsumoDto>>> ListarMovimientos(
        [FromQuery] int? insumoId,
        [FromQuery] DateTime? desde,
        [FromQuery] DateTime? hasta,
        CancellationToken ct = default)
    {
        var h = (hasta ?? DateTime.Today).Date.AddDays(1);
        var d = (desde ?? h.AddDays(-31)).Date;
        var list = await _repo.ListarMovimientosAsync(insumoId, d, h, SedeRequeridaId, ct);
        return Ok(list.Select(m => new MovimientoInsumoDto
        {
            Id = m.Id,
            InsumoId = m.InsumoId,
            InsumoNombre = m.InsumoNombre,
            Tipo = m.Tipo,
            Cantidad = m.Cantidad,
            CostoTotal = m.CostoTotal,
            Fecha = m.Fecha,
            UsuarioNombre = m.UsuarioNombre,
            Descripcion = m.Descripcion
        }).ToList());
    }

    private static InsumoDto Map(Insumo i) => new()
    {
        Id = i.Id,
        Nombre = i.Nombre,
        UnidadMedida = i.UnidadMedida,
        Clase = i.Clase,
        ContenidoValor = i.ContenidoValor,
        ContenidoUnidad = i.ContenidoUnidad,
        StockActual = i.StockActual,
        StockMinimo = i.StockMinimo,
        Activo = i.Activo,
        UltimaCompra = i.UltimaCompra,
        FechaIngreso = i.FechaIngreso,
        FechaVencimiento = i.FechaVencimiento,
        EnUso = i.EnUso
    };

    /// <summary>Normaliza la clase de inventario; si viene vacía o inválida, usa INSUMO (consumible).</summary>
    private static string NormalizarClase(string? clase) => InventarioReglas.NormalizarClase(clase);
}
