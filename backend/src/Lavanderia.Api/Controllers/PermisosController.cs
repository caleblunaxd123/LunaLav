using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/[controller]")]
[Authorize(Roles = "ADMIN")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class PermisosController : TenantAwareControllerBase
{
    private readonly IRolPermisoRepository _repo;
    private readonly IRolRepository _roles;
    public PermisosController(IRolPermisoRepository repo, IRolRepository roles)
    {
        _repo = repo;
        _roles = roles;
    }

    [HttpGet("modulos")]
    public ActionResult<List<string>> Modulos() => Ok(Domain.Modulos.Todos.ToList());

    [HttpGet]
    public async Task<ActionResult<List<PermisoItemDto>>> ObtenerMatriz(CancellationToken ct)
    {
        var matriz = await _repo.ObtenerMatrizAsync(NegocioId, ct);
        return Ok(matriz
            .Where(p => p.RolCodigo != "ADMIN") // ADMIN siempre tiene acceso total, no se edita
            .Select(p => new PermisoItemDto(p.RolId, p.Modulo, p.PuedeAcceder))
            .ToList());
    }

    [HttpPut]
    public async Task<IActionResult> Guardar([FromBody] ActualizarPermisosRequest req, CancellationToken ct)
    {
        var rolesEditables = (await _roles.ListarPorNegocioAsync(NegocioId, ct))
            .Where(r => !r.EsSistema)   // ADMIN (sistema) siempre tiene acceso total, no se edita
            .Select(r => r.Id)
            .ToHashSet();

        if (req.Permisos.Any(p => !rolesEditables.Contains(p.RolId)))
            return BadRequest(new { mensaje = "La solicitud incluye un rol que no puede administrarse desde este negocio." });
        if (req.Permisos.Any(p => !Domain.Modulos.Todos.Contains(p.Modulo)))
            return BadRequest(new { mensaje = "La solicitud incluye un módulo no válido." });
        if (req.Permisos.GroupBy(p => new { p.RolId, p.Modulo }).Any(g => g.Count() > 1))
            return BadRequest(new { mensaje = "La solicitud contiene permisos duplicados." });

        foreach (var item in req.Permisos)
        {
            await _repo.GuardarAsync(item.RolId, item.Modulo, item.PuedeAcceder, NegocioId, ct);
        }
        return NoContent();
    }
}
