using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// Administración de áreas de lavado (solo ADMIN). El endpoint público /api/areas-lavado
/// (en CatalogosController) devuelve solo las activas, ordenadas, para el flujo de pedidos.
/// </summary>
[Route("api/areas-lavado-admin")]
[Authorize(Roles = "ADMIN")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class AreasLavadoAdminController : TenantAwareControllerBase
{
    private readonly IAreaLavadoRepository _repo;
    private readonly Lavanderia.Api.Infrastructure.ISqlConnectionFactory _factory;
    public AreasLavadoAdminController(IAreaLavadoRepository repo, Lavanderia.Api.Infrastructure.ISqlConnectionFactory factory)
    {
        _repo = repo;
        _factory = factory;
    }

    [HttpGet]
    public async Task<ActionResult<List<AreaLavadoEditableDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodasAsync(SedeRequeridaId, ct)).Select(Map).ToList());

    [HttpPost]
    public async Task<ActionResult<AreaLavadoEditableDto>> Crear([FromBody] AreaLavadoEditableDto dto, CancellationToken ct)
    {
        var nombre = dto.Nombre.Trim();
        if (await _repo.ExisteNombreAsync(nombre, SedeRequeridaId, ct: ct))
            return Conflict(new { mensaje = "Ya existe un area con ese nombre en esta sede." });
        if (await _repo.ExisteOrdenAsync(dto.Orden, SedeRequeridaId, ct: ct))
            return Conflict(new { mensaje = "Ya existe un area con ese orden en esta sede." });
        var id = await _repo.CrearAsync(new AreaLavado
        {
            SedeId = SedeRequeridaId,
            Nombre = nombre,
            Orden = dto.Orden,
            TiempoEstMinutos = dto.TiempoEstMinutos,
            Activa = dto.Activa
        }, ct);
        var creada = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        return CreatedAtAction(nameof(Listar), Map(creada!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] AreaLavadoEditableDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (existente is null) return NotFound();

        var nombre = dto.Nombre.Trim();
        if (await _repo.ExisteNombreAsync(nombre, SedeRequeridaId, id, ct))
            return Conflict(new { mensaje = "Ya existe otra area con ese nombre en esta sede." });
        if (await _repo.ExisteOrdenAsync(dto.Orden, SedeRequeridaId, id, ct))
            return Conflict(new { mensaje = "Ya existe otra area con ese orden en esta sede." });
        if (existente.Activa && !dto.Activa && await EsUltimaAreaActivaAsync(id, ct))
            return BadRequest(new { mensaje = "No puedes desactivar la última área activa: los pedidos de esta sede no podrían avanzar. Crea o reactiva otra área primero." });
        existente.Nombre = nombre;
        existente.Orden = dto.Orden;
        existente.TiempoEstMinutos = dto.TiempoEstMinutos;
        existente.Activa = dto.Activa;
        await _repo.ActualizarAsync(existente, SedeRequeridaId, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (existente is null) return NotFound();

        // Una sede sin áreas activas queda operativamente rota: se pueden crear pedidos pero
        // no avanzar por el flujo. Detectado en auditoría QA (sedes reales en ese estado).
        if (existente.Activa && await EsUltimaAreaActivaAsync(id, ct))
            return BadRequest(new { mensaje = "No puedes desactivar la última área activa: los pedidos de esta sede no podrían avanzar. Crea o reactiva otra área primero." });

        if (await Lavanderia.Api.Infrastructure.CatalogoEliminacion.EliminarCatalogoAsync(_factory, "AreaLavado", "SedeId", id, SedeRequeridaId, ct))
            return Ok(new { mensaje = "Área eliminada.", eliminado = true });
        await _repo.CambiarEstadoAsync(id, false, SedeRequeridaId, ct);
        return Ok(new { mensaje = "No se puede eliminar: el área tiene pedidos asociados. Se desactivó.", eliminado = false });
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> Reactivar(int id, [FromBody] CambiarEstadoUsuarioRequest req, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (existente is null) return NotFound();
        if (existente.Activa && !req.Activo && await EsUltimaAreaActivaAsync(id, ct))
            return BadRequest(new { mensaje = "No puedes desactivar la última área activa: los pedidos de esta sede no podrían avanzar. Crea o reactiva otra área primero." });
        await _repo.CambiarEstadoAsync(id, req.Activo, SedeRequeridaId, ct);
        return NoContent();
    }

    /// <summary>True si el área indicada es la única activa que queda en la sede actual.</summary>
    private async Task<bool> EsUltimaAreaActivaAsync(int areaId, CancellationToken ct)
    {
        var activas = await _repo.ListarActivasAsync(SedeRequeridaId, ct);
        return activas.Count == 1 && activas[0].Id == areaId;
    }

    private static AreaLavadoEditableDto Map(AreaLavado a) => new()
    {
        Id = a.Id,
        Nombre = a.Nombre,
        Orden = a.Orden,
        TiempoEstMinutos = a.TiempoEstMinutos,
        Activa = a.Activa,
        EnUso = a.EnUso
    };
}
