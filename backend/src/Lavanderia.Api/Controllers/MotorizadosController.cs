using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>Catalogo de repartidores por sede. El listado activo (usado al asignar un pedido)
/// es accesible a cualquiera con modulo PEDIDOS; el CRUD completo solo a ADMIN.</summary>
[Route("api/motorizados")]
[Authorize(Policy = "Modulo:PEDIDOS")]
public class MotorizadosController : TenantAwareControllerBase
{
    private readonly IMotorizadoRepository _repo;
    public MotorizadosController(IMotorizadoRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<ActionResult<List<MotorizadoDto>>> ListarActivos(CancellationToken ct)
        => Ok((await _repo.ListarActivosAsync(SedeRequeridaId, ct)).Select(Map).ToList());

    [HttpGet("todos")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<List<MotorizadoDto>>> ListarTodos(CancellationToken ct)
        => Ok((await _repo.ListarTodosAsync(SedeRequeridaId, ct)).Select(Map).ToList());

    [HttpPost]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<MotorizadoDto>> Crear([FromBody] MotorizadoDto dto, CancellationToken ct)
    {
        var celular = Limpiar(dto.Celular);
        if (celular is not null && await _repo.ExisteCelularAsync(celular, SedeRequeridaId, ct: ct))
            return Conflict(new { mensaje = "Ya existe un repartidor con ese celular en esta sede." });
        var id = await _repo.CrearAsync(new Motorizado
        {
            SedeId = SedeRequeridaId,
            Nombre = dto.Nombre.Trim(),
            Celular = celular,
            Activo = dto.Activo
        }, ct);
        var creado = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        return CreatedAtAction(nameof(ListarTodos), Map(creado!));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] MotorizadoDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (existente is null) return NotFound();

        var celular = Limpiar(dto.Celular);
        if (celular is not null && await _repo.ExisteCelularAsync(celular, SedeRequeridaId, id, ct))
            return Conflict(new { mensaje = "Ya existe otro repartidor con ese celular en esta sede." });
        existente.Nombre = dto.Nombre.Trim();
        existente.Celular = celular;
        existente.Activo = dto.Activo;
        await _repo.ActualizarAsync(existente, SedeRequeridaId, ct);
        return NoContent();
    }

    [HttpPatch("{id:int}/estado")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoUsuarioRequest req, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (existente is null) return NotFound();
        await _repo.CambiarEstadoAsync(id, req.Activo, SedeRequeridaId, ct);
        return NoContent();
    }

    private static MotorizadoDto Map(Motorizado m) => new()
    {
        Id = m.Id,
        Nombre = m.Nombre,
        Celular = m.Celular,
        Activo = m.Activo
    };

    private static string? Limpiar(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
