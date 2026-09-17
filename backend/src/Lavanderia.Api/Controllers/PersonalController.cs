using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/personal")]
[Authorize(Roles = "ADMIN")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class PersonalController : TenantAwareControllerBase
{
    private readonly IEmpleadoRepository _repo;
    private readonly Lavanderia.Api.Infrastructure.ISqlConnectionFactory _factory;
    public PersonalController(IEmpleadoRepository repo, Lavanderia.Api.Infrastructure.ISqlConnectionFactory factory)
    {
        _repo = repo;
        _factory = factory;
    }

    [HttpGet]
    public async Task<ActionResult<List<EmpleadoDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodosAsync(SedeRequeridaId, ct)).Select(Map).ToList());

    [HttpPost]
    public async Task<ActionResult<EmpleadoDto>> Crear([FromBody] EmpleadoDto dto, CancellationToken ct)
    {
        var dni = Limpiar(dto.Dni);
        var celular = Limpiar(dto.Celular);
        var error = await ValidarDuplicadosAsync(dni, celular, null, ct);
        if (error is not null) return error;
        var id = await _repo.CrearAsync(new Empleado
        {
            SedeId = SedeRequeridaId,
            Nombre = dto.Nombre.Trim(),
            Dni = dni,
            Celular = celular,
            Cargo = Limpiar(dto.Cargo),
            FechaIngreso = dto.FechaIngreso,
            Activo = dto.Activo
        }, ct);
        var creado = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        return CreatedAtAction(nameof(Listar), Map(creado!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] EmpleadoDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (existente is null) return NotFound();

        var dni = Limpiar(dto.Dni);
        var celular = Limpiar(dto.Celular);
        var error = await ValidarDuplicadosAsync(dni, celular, id, ct);
        if (error is not null) return error;
        existente.Nombre = dto.Nombre.Trim();
        existente.Dni = dni;
        existente.Celular = celular;
        existente.Cargo = Limpiar(dto.Cargo);
        existente.FechaIngreso = dto.FechaIngreso;
        existente.Activo = dto.Activo;
        await _repo.ActualizarAsync(existente, SedeRequeridaId, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (existente is null) return NotFound();

        if (await Lavanderia.Api.Infrastructure.CatalogoEliminacion.EliminarCatalogoAsync(_factory, "Empleado", "SedeId", id, SedeRequeridaId, ct))
            return Ok(new { mensaje = "Empleado eliminado.", eliminado = true });
        await _repo.CambiarEstadoAsync(id, false, SedeRequeridaId, ct);
        return Ok(new { mensaje = "No se puede eliminar: el empleado tiene registros asociados. Se desactivó.", eliminado = false });
    }

    [HttpPatch("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoUsuarioRequest req, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (existente is null) return NotFound();
        await _repo.CambiarEstadoAsync(id, req.Activo, SedeRequeridaId, ct);
        return NoContent();
    }

    private static EmpleadoDto Map(Empleado e) => new()
    {
        Id = e.Id,
        Nombre = e.Nombre,
        Dni = e.Dni,
        Celular = e.Celular,
        Cargo = e.Cargo,
        FechaIngreso = e.FechaIngreso,
        Activo = e.Activo
    };

    private async Task<ConflictObjectResult?> ValidarDuplicadosAsync(string? dni, string? celular, int? excluirId, CancellationToken ct)
    {
        if (dni is not null && await _repo.ExisteDniAsync(dni, SedeRequeridaId, excluirId, ct))
            return Conflict(new { mensaje = "Ya existe un empleado con ese DNI en esta sede." });
        if (celular is not null && await _repo.ExisteCelularAsync(celular, SedeRequeridaId, excluirId, ct))
            return Conflict(new { mensaje = "Ya existe un empleado con ese celular en esta sede." });
        return null;
    }

    private static string? Limpiar(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
