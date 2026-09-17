using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// CRUD de sedes/sucursales del negocio (solo ADMIN). Cualquier usuario autenticado puede
/// listar las sedes de su negocio (para el selector del header); crear/editar es solo ADMIN.
/// </summary>
[Route("api/[controller]")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class SedesController : TenantAwareControllerBase
{
    private readonly ISedeRepository _repo;
    public SedesController(ISedeRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<ActionResult<List<SedeDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarPorNegocioAsync(NegocioId, ct)).Select(Map).ToList());

    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<SedeDto>> Crear([FromBody] SedeDto dto, CancellationToken ct)
    {
        var nombre = dto.Nombre.Trim();
        if (await _repo.ExisteNombreAsync(nombre, NegocioId, ct: ct))
            return Conflict(new { mensaje = "Ya existe una sede con ese nombre." });
        var id = await _repo.CrearAsync(new Sede
        {
            NegocioId = NegocioId,
            Nombre = nombre,
            Direccion = Limpiar(dto.Direccion),
            Telefono = Limpiar(dto.Telefono),
            Activo = dto.Activo
        }, ct);
        var creada = await _repo.ObtenerPorIdAsync(id, ct);
        return CreatedAtAction(nameof(Listar), Map(creada!));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] SedeDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, ct);
        if (existente is null || existente.NegocioId != NegocioId) return NotFound();

        var nombre = dto.Nombre.Trim();
        if (await _repo.ExisteNombreAsync(nombre, NegocioId, id, ct))
            return Conflict(new { mensaje = "Ya existe otra sede con ese nombre." });
        if (!dto.Activo && existente.Activo && await EsUltimaSedeActivaAsync(id, ct))
            return BadRequest(new { mensaje = MensajeUltimaSede });
        existente.Nombre = nombre;
        existente.Direccion = Limpiar(dto.Direccion);
        existente.Telefono = Limpiar(dto.Telefono);
        existente.Activo = dto.Activo;
        await _repo.ActualizarAsync(existente, NegocioId, ct);
        return NoContent();
    }

    [HttpPatch("{id:int}/estado")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoUsuarioRequest req, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, ct);
        if (existente is null || existente.NegocioId != NegocioId) return NotFound();
        if (!req.Activo && existente.Activo && await EsUltimaSedeActivaAsync(id, ct))
            return BadRequest(new { mensaje = MensajeUltimaSede });
        await _repo.CambiarEstadoAsync(id, req.Activo, NegocioId, ct);
        return NoContent();
    }

    private const string MensajeUltimaSede =
        "No puedes desactivar la única sede activa. Toda empresa debe tener al menos una sede activa.";

    /// <summary>True si la sede indicada es la única activa del negocio (no se puede desactivar).</summary>
    private async Task<bool> EsUltimaSedeActivaAsync(int sedeId, CancellationToken ct)
    {
        var activas = (await _repo.ListarPorNegocioAsync(NegocioId, ct)).Where(s => s.Activo).ToList();
        return activas.Count <= 1 && activas.All(s => s.Id == sedeId);
    }

    private static SedeDto Map(Sede s) => new()
    {
        Id = s.Id,
        Nombre = s.Nombre,
        Direccion = s.Direccion,
        Telefono = s.Telefono,
        Activo = s.Activo
    };

    private static string? Limpiar(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
