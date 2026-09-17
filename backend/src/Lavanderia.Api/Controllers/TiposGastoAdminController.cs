using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// Administración de tipos de gasto (solo ADMIN). El endpoint público /api/caja/tipos-gasto
/// (en CajaController) devuelve solo los activos, para el modal de registrar gasto.
/// </summary>
[Route("api/tipos-gasto-admin")]
[Authorize(Roles = "ADMIN")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class TiposGastoAdminController : TenantAwareControllerBase
{
    private readonly ICajaRepository _repo;
    private readonly Lavanderia.Api.Infrastructure.ISqlConnectionFactory _factory;
    public TiposGastoAdminController(ICajaRepository repo, Lavanderia.Api.Infrastructure.ISqlConnectionFactory factory)
    {
        _repo = repo;
        _factory = factory;
    }

    [HttpGet]
    public async Task<ActionResult<List<TipoGastoEditableDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodosTiposGastoAsync(NegocioId, ct)).Select(Map).ToList());

    [HttpPost]
    public async Task<ActionResult<TipoGastoEditableDto>> Crear([FromBody] TipoGastoEditableDto dto, CancellationToken ct)
    {
        var nombre = dto.Nombre.Trim();
        if (await _repo.ExisteNombreTipoGastoAsync(nombre, NegocioId, ct: ct))
            return Conflict(new { mensaje = "Ya existe un tipo de gasto con ese nombre." });
        var id = await _repo.CrearTipoGastoAsync(new TipoGasto { NegocioId = NegocioId, Nombre = nombre, Activo = dto.Activo }, ct);
        var creado = await _repo.ObtenerTipoGastoPorIdAsync(id, NegocioId, ct);
        return CreatedAtAction(nameof(Listar), Map(creado!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] TipoGastoEditableDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerTipoGastoPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        var nombre = dto.Nombre.Trim();
        if (await _repo.ExisteNombreTipoGastoAsync(nombre, NegocioId, id, ct))
            return Conflict(new { mensaje = "Ya existe otro tipo de gasto con ese nombre." });
        existente.Nombre = nombre;
        existente.Activo = dto.Activo;
        await _repo.ActualizarTipoGastoAsync(existente, NegocioId, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
    {
        var existente = await _repo.ObtenerTipoGastoPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        // TipoGasto no tiene FK real desde MovimientoCaja, así que el DELETE no fallaría solo:
        // hay que verificar el uso explícitamente para no dejar movimientos huérfanos.
        var usos = await _repo.ContarUsoTipoGastoAsync(id, NegocioId, ct);
        if (usos == 0)
        {
            await Lavanderia.Api.Infrastructure.CatalogoEliminacion.EliminarCatalogoAsync(_factory, "TipoGasto", "NegocioId", id, NegocioId, ct);
            return Ok(new { mensaje = "Tipo de gasto eliminado.", eliminado = true });
        }
        await _repo.CambiarEstadoTipoGastoAsync(id, false, NegocioId, ct);
        return Ok(new { mensaje = $"No se puede eliminar: está usado en {usos} movimiento(s) de caja. Se desactivó.", eliminado = false });
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> Reactivar(int id, [FromBody] CambiarEstadoUsuarioRequest req, CancellationToken ct)
    {
        var existente = await _repo.ObtenerTipoGastoPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();
        await _repo.CambiarEstadoTipoGastoAsync(id, req.Activo, NegocioId, ct);
        return NoContent();
    }

    private static TipoGastoEditableDto Map(TipoGasto t) => new() { Id = t.Id, Nombre = t.Nombre, Activo = t.Activo, EnUso = t.EnUso };
}
