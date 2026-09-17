using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/plantillas-whatsapp-admin")]
[Authorize(Roles = "ADMIN")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class PlantillasWhatsappAdminController : TenantAwareControllerBase
{
    private readonly IPlantillaWhatsappRepository _repo;
    public PlantillasWhatsappAdminController(IPlantillaWhatsappRepository repo) => _repo = repo;

    [HttpGet]
    public async Task<ActionResult<List<PlantillaWhatsappDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodasAsync(NegocioId, ct)).Select(Map).ToList());

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] PlantillaWhatsappDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();

        existente.Mensaje = dto.Mensaje.Trim();
        existente.Activa = dto.Activa;
        await _repo.ActualizarAsync(existente, NegocioId, ct);
        return NoContent();
    }

    private static PlantillaWhatsappDto Map(Domain.PlantillaWhatsapp p) => new()
    {
        Id = p.Id,
        Evento = p.Evento,
        Mensaje = p.Mensaje,
        Activa = p.Activa
    };
}
