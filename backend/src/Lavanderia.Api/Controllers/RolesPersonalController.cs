using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/roles-personal")]
[Authorize(Roles = "ADMIN")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class RolesPersonalController : TenantAwareControllerBase
{
    private readonly IRolPersonalRepository _repo;
    private readonly Lavanderia.Api.Infrastructure.ISqlConnectionFactory _factory;
    public RolesPersonalController(IRolPersonalRepository repo, Lavanderia.Api.Infrastructure.ISqlConnectionFactory factory)
    {
        _repo = repo;
        _factory = factory;
    }

    [HttpGet]
    public async Task<ActionResult<List<RolPersonalDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodosAsync(NegocioId, ct)).Select(Map).ToList());

    [HttpPost]
    public async Task<ActionResult<RolPersonalDto>> Crear([FromBody] RolPersonalDto dto, CancellationToken ct)
    {
        var nombre = dto.Nombre.Trim();
        if (await _repo.ExisteNombreAsync(nombre, NegocioId, ct: ct))
            return Conflict(new { mensaje = "Ya existe un rol de personal con ese nombre." });
        var id = await _repo.CrearAsync(new RolPersonal { NegocioId = NegocioId, Nombre = nombre, Activo = dto.Activo }, ct);
        var creado = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        return CreatedAtAction(nameof(Listar), Map(creado!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] RolPersonalDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        var nombre = dto.Nombre.Trim();
        if (await _repo.ExisteNombreAsync(nombre, NegocioId, id, ct))
            return Conflict(new { mensaje = "Ya existe otro rol de personal con ese nombre." });
        existente.Nombre = nombre;
        existente.Activo = dto.Activo;
        await _repo.ActualizarAsync(existente, NegocioId, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        if (await Lavanderia.Api.Infrastructure.CatalogoEliminacion.EliminarCatalogoAsync(_factory, "RolPersonal", "NegocioId", id, NegocioId, ct))
            return Ok(new { mensaje = "Rol eliminado.", eliminado = true });
        await _repo.CambiarEstadoAsync(id, false, NegocioId, ct);
        return Ok(new { mensaje = "No se puede eliminar: el rol está en uso. Se desactivó.", eliminado = false });
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoUsuarioRequest req, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();
        await _repo.CambiarEstadoAsync(id, req.Activo, NegocioId, ct);
        return NoContent();
    }

    private static RolPersonalDto Map(RolPersonal r) => new() { Id = r.Id, Nombre = r.Nombre, Activo = r.Activo };
}
