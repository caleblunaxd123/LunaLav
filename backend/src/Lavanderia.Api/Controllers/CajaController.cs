using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/[controller]")]
[Authorize(Policy = "Modulo:CAJA")]
public class CajaController : TenantAwareControllerBase
{
    private readonly ICajaRepository _repo;
    public CajaController(ICajaRepository repo) => _repo = repo;

    [HttpGet("tipos-gasto")]
    public async Task<ActionResult<List<TipoGastoDto>>> TiposGasto(CancellationToken ct)
        => Ok((await _repo.ListarTiposGastoAsync(NegocioId, ct)).Select(t => new TipoGastoDto(t.Id, t.Nombre)).ToList());

    [HttpGet("movimientos")]
    public async Task<ActionResult<List<MovimientoCajaDto>>> Movimientos(
        [FromQuery] DateTime? fecha, [FromQuery] int? usuarioId, CancellationToken ct)
    {
        var lista = await _repo.ListarMovimientosAsync(fecha ?? DateTime.Today, usuarioId, SedeRequeridaId, ct);
        return Ok(lista.Select(Map).ToList());
    }

    [HttpGet("usuarios-dia")]
    public async Task<ActionResult<List<UsuarioDelDiaDto>>> UsuariosDelDia([FromQuery] DateTime? fecha, CancellationToken ct)
        => Ok(await _repo.UsuariosDelDiaAsync(fecha ?? DateTime.Today, SedeRequeridaId, ct));

    [HttpGet("cuadres/del-usuario")]
    public async Task<ActionResult<CuadreCajaDto>> CuadreDelUsuario(
        [FromQuery] DateTime fecha, [FromQuery] int usuarioId, CancellationToken ct)
    {
        var c = await _repo.ObtenerCuadreDelUsuarioAsync(fecha, usuarioId, SedeRequeridaId, ct);
        if (c is null) return NotFound();
        return Ok(MapCuadre(c));
    }

    [HttpPost("gastos")]
    public async Task<ActionResult<MovimientoCajaDto>> RegistrarGasto([FromBody] RegistrarGastoRequest req, CancellationToken ct)
    {
        var metodosValidos = new[] { "EFECTIVO", "YAPE", "PLIN", "TRANSFERENCIA", "POS" };
        if (!metodosValidos.Contains(req.MetodoPago.ToUpperInvariant()))
            return BadRequest(new { mensaje = "Método de pago inválido." });
        if (req.Monto <= 0)
            return BadRequest(new { mensaje = "El monto debe ser mayor a 0." });
        if (req.TipoGastoId is int tipoGastoId)
        {
            var tipoGasto = await _repo.ObtenerTipoGastoPorIdAsync(tipoGastoId, NegocioId, ct);
            if (tipoGasto is null)
                return BadRequest(new { mensaje = "El tipo de gasto no pertenece a este negocio." });
        }

        var mov = new MovimientoCaja
        {
            SedeId = SedeRequeridaId,
            Fecha = DateTime.Now,
            Tipo = "GASTO",
            MetodoPago = req.MetodoPago.ToUpperInvariant(),
            Monto = req.Monto,
            Descripcion = req.Descripcion,
            TipoGastoId = req.TipoGastoId,
            UsuarioId = UsuarioId
        };
        mov.Id = await _repo.RegistrarGastoAsync(mov, NegocioId, ct);
        return CreatedAtAction(nameof(Movimientos), Map(mov));
    }

    [HttpPost("cuadres")]
    public async Task<ActionResult<CuadreCajaDto>> GuardarCuadre([FromBody] GuardarCuadreRequest req, CancellationToken ct)
    {
        var sedeId = SedeRequeridaId;
        var usuarioCierreId = req.UsuarioId ?? UsuarioId;
        if (req.Fecha.Date > DateTime.Today)
            return BadRequest(new { mensaje = "No se puede guardar un cuadre para una fecha futura." });
        if (usuarioCierreId != UsuarioId && !User.IsInRole("ADMIN"))
            return Forbid();

        if (usuarioCierreId != UsuarioId)
        {
            var usuariosSede = await _repo.UsuariosDelDiaAsync(req.Fecha.Date, sedeId, ct);
            if (!usuariosSede.Any(u => u.Id == usuarioCierreId))
                return BadRequest(new { mensaje = "El colaborador no pertenece a esta sede o no tiene movimientos en la fecha indicada." });
        }

        if (req.Corte > req.TotalContado)
            return BadRequest(new { mensaje = "El corte no puede ser mayor que el efectivo contado." });

        // Los importes derivados se calculan siempre desde los movimientos persistidos. El
        // navegador solo informa el efectivo contado, la caja inicial y el corte fisico.
        var movimientos = await _repo.ListarMovimientosAsync(req.Fecha.Date, usuarioCierreId, sedeId, ct);
        var ingresosEfectivo = movimientos
            .Where(m => m.Tipo == "INGRESO" && m.MetodoPago == "EFECTIVO")
            .Sum(m => m.Monto);
        var gastosEfectivo = movimientos
            .Where(m => m.Tipo == "GASTO" && m.MetodoPago == "EFECTIVO")
            .Sum(m => m.Monto);
        var ingresosDigital = movimientos
            .Where(m => m.Tipo == "INGRESO" && m.MetodoPago is "YAPE" or "PLIN" or "TRANSFERENCIA")
            .Sum(m => m.Monto);
        var ingresosTarjeta = movimientos
            .Where(m => m.Tipo == "INGRESO" && m.MetodoPago is "POS" or "TARJETA")
            .Sum(m => m.Monto);
        var esperado = req.CajaInicial + ingresosEfectivo - gastosEfectivo;
        var diferencia = req.TotalContado - esperado;
        var cajaFinal = req.TotalContado - req.Corte;

        var cuadre = new CuadreCaja
        {
            SedeId = sedeId,
            Fecha = req.Fecha.Date,
            UsuarioId = usuarioCierreId,
            CajaInicial = req.CajaInicial,
            PedidosPagadosEfect = ingresosEfectivo,
            Gastos = gastosEfectivo,
            TotalContado = req.TotalContado,
            Diferencia = diferencia,
            CajaFinal = cajaFinal,
            Corte = req.Corte,
            IngresosDigital = ingresosDigital,
            IngresosTarjeta = ingresosTarjeta,
            Nota = req.Nota,
            Observaciones = req.Observaciones,
            DetalleConteo = string.IsNullOrWhiteSpace(req.DetalleConteo) ? null : req.DetalleConteo
        };
        var id = await _repo.GuardarCuadreAsync(cuadre, ct);
        var guardado = await _repo.ObtenerCuadreAsync(id, sedeId, ct);
        return Ok(MapCuadre(guardado!));
    }

    [HttpGet("cuadres/{id:int}")]
    public async Task<ActionResult<CuadreCajaDto>> ObtenerCuadre(int id, CancellationToken ct)
    {
        var c = await _repo.ObtenerCuadreAsync(id, SedeRequeridaId, ct);
        if (c is null) return NotFound();
        return Ok(MapCuadre(c));
    }

    [HttpGet("cuadres/ultimo-anterior")]
    public async Task<ActionResult<CuadreCajaDto>> ObtenerUltimoAnterior([FromQuery] DateTime fecha, CancellationToken ct)
    {
        var c = await _repo.ObtenerUltimoAnteriorAsync(fecha, SedeRequeridaId, ct);
        if (c is null) return NotFound();
        return Ok(MapCuadre(c));
    }

    private static MovimientoCajaDto Map(MovimientoCaja m) => new()
    {
        Id = m.Id,
        Fecha = m.Fecha,
        Tipo = m.Tipo,
        MetodoPago = m.MetodoPago,
        Monto = m.Monto,
        Descripcion = m.Descripcion,
        PedidoId = m.PedidoId,
        PedidoNumero = m.PedidoNumero,
        ClienteNombre = m.ClienteNombre,
        UsuarioNombre = m.UsuarioNombre,
        TipoGastoId = m.TipoGastoId,
        TipoGastoNombre = m.TipoGastoNombre
    };

    private static CuadreCajaDto MapCuadre(CuadreCaja c) => new()
    {
        Id = c.Id,
        Fecha = c.Fecha,
        UsuarioId = c.UsuarioId,
        UsuarioNombre = c.UsuarioNombre,
        CajaInicial = c.CajaInicial,
        PedidosPagadosEfect = c.PedidosPagadosEfect,
        Gastos = c.Gastos,
        TotalContado = c.TotalContado,
        Diferencia = c.Diferencia,
        CajaFinal = c.CajaFinal,
        Corte = c.Corte,
        IngresosDigital = c.IngresosDigital,
        IngresosTarjeta = c.IngresosTarjeta,
        Nota = c.Nota,
        Observaciones = c.Observaciones,
        DetalleConteo = c.DetalleConteo,
        FechaCreacion = c.FechaCreacion
    };
}
