using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/categorias")]
[Authorize(Roles = "ADMIN")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class CategoriasController : TenantAwareControllerBase
{
    private readonly ICategoriaRepository _repo;
    private readonly Lavanderia.Api.Infrastructure.ISqlConnectionFactory _factory;
    public CategoriasController(ICategoriaRepository repo, Lavanderia.Api.Infrastructure.ISqlConnectionFactory factory)
    {
        _repo = repo;
        _factory = factory;
    }

    [HttpGet]
    public async Task<ActionResult<List<CategoriaDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodasAsync(NegocioId, ct)).Select(Map).ToList());

    [HttpPost]
    public async Task<ActionResult<CategoriaDto>> Crear([FromBody] CategoriaDto dto, CancellationToken ct)
    {
        var nombre = dto.Nombre.Trim();
        if (await _repo.ExisteNombreAsync(nombre, NegocioId, ct: ct))
            return Conflict(new { mensaje = "Ya existe una categoria con ese nombre." });
        var id = await _repo.CrearAsync(new Categoria { NegocioId = NegocioId, Nombre = nombre, Activa = dto.Activa }, ct);
        var creada = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        return CreatedAtAction(nameof(Listar), Map(creada!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] CategoriaDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        var nombre = dto.Nombre.Trim();
        if (await _repo.ExisteNombreAsync(nombre, NegocioId, id, ct))
            return Conflict(new { mensaje = "Ya existe otra categoria con ese nombre." });
        existente.Nombre = nombre;
        existente.Activa = dto.Activa;
        await _repo.ActualizarAsync(existente, NegocioId, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        if (await Lavanderia.Api.Infrastructure.CatalogoEliminacion.EliminarCatalogoAsync(_factory, "Categoria", "NegocioId", id, NegocioId, ct))
            return Ok(new { mensaje = "Categoría eliminada.", eliminado = true });
        await _repo.CambiarEstadoAsync(id, false, NegocioId, ct);
        return Ok(new { mensaje = "No se puede eliminar: la categoría está en uso. Se desactivó.", eliminado = false });
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> Reactivar(int id, [FromBody] CambiarEstadoUsuarioRequest req, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();
        await _repo.CambiarEstadoAsync(id, req.Activo, NegocioId, ct);
        return NoContent();
    }

    private static CategoriaDto Map(Categoria c) => new() { Id = c.Id, Nombre = c.Nombre, Activa = c.Activa, EnUso = c.EnUso };
}
