using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// Roles de acceso propios del negocio (flexibles). El dueño crea/edita/elimina
/// sus roles; ADMIN es un rol de sistema fijo. Los permisos por módulo de cada
/// rol se administran en PermisosController ("Permisos y accesos").
/// </summary>
[Route("api/roles-acceso")]
[Authorize(Roles = "ADMIN")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class RolesAccesoController : TenantAwareControllerBase
{
    private readonly IRolRepository _roles;
    private readonly IRolPermisoRepository _permisos;
    public RolesAccesoController(IRolRepository roles, IRolPermisoRepository permisos)
    {
        _roles = roles;
        _permisos = permisos;
    }

    [HttpGet]
    public async Task<ActionResult<List<RolAccesoDto>>> Listar(CancellationToken ct)
        => Ok((await _roles.ListarPorNegocioAsync(NegocioId, ct))
            .Select(r => new RolAccesoDto(r.Id, r.Nombre, r.EsSistema, r.EnUso)).ToList());

    [HttpPost]
    public async Task<ActionResult<RolAccesoDto>> Crear([FromBody] CrearRolRequest req, CancellationToken ct)
    {
        var nombre = req.Nombre?.Trim() ?? "";
        if (nombre.Length < 2 || nombre.Length > 60)
            return BadRequest(new { mensaje = "El nombre del rol debe tener entre 2 y 60 caracteres." });

        var existentes = await _roles.ListarPorNegocioAsync(NegocioId, ct);
        if (existentes.Any(r => string.Equals(r.Nombre, nombre, StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { mensaje = $"Ya existe un rol llamado “{nombre}”." });

        var codigo = "ROL_" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var id = await _roles.CrearAsync(NegocioId, codigo, nombre, ct);

        // Sembrar permisos (todo en false) para que el rol aparezca en la matriz.
        foreach (var modulo in Modulos.Todos)
            await _permisos.GuardarAsync(id, modulo, false, NegocioId, ct);

        return Ok(new RolAccesoDto(id, nombre, false, false));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Renombrar(int id, [FromBody] RenombrarRolRequest req, CancellationToken ct)
    {
        var rol = await _roles.ObtenerAsync(id, ct);
        if (rol is null || rol.NegocioId != NegocioId)
            return NotFound(new { mensaje = "Rol no encontrado." });
        if (rol.EsSistema)
            return BadRequest(new { mensaje = "Los roles de sistema no se pueden editar." });

        var nombre = req.Nombre?.Trim() ?? "";
        if (nombre.Length < 2 || nombre.Length > 60)
            return BadRequest(new { mensaje = "El nombre del rol debe tener entre 2 y 60 caracteres." });

        var existentes = await _roles.ListarPorNegocioAsync(NegocioId, ct);
        if (existentes.Any(r => r.Id != id && string.Equals(r.Nombre, nombre, StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { mensaje = $"Ya existe un rol llamado “{nombre}”." });

        await _roles.RenombrarAsync(id, nombre, NegocioId, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        var rol = await _roles.ObtenerAsync(id, ct);
        if (rol is null || rol.NegocioId != NegocioId)
            return NotFound(new { mensaje = "Rol no encontrado." });
        if (rol.EsSistema)
            return BadRequest(new { mensaje = "Los roles de sistema no se pueden eliminar." });
        if (await _roles.EnUsoAsync(id, ct))
            return BadRequest(new { mensaje = "No puedes eliminar un rol que tiene usuarios asignados. Cámbiales el rol primero." });

        await _roles.EliminarAsync(id, NegocioId, ct);
        return NoContent();
    }
}
