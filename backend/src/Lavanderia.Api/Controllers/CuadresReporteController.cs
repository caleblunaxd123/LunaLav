using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// Reporte mensual de cuadres diarios (cierres de días anteriores). Vive aparte de
/// ReportesController porque su acceso NO es solo del módulo REPORTES: forma parte de la
/// operación de caja, así que también lo ven la coordinadora y la trabajadora (módulo CAJA).
/// </summary>
[Route("api/reportes")]
[Authorize(Policy = "Modulo:CAJA_O_REPORTES")]
public class CuadresReporteController : TenantAwareControllerBase
{
    private readonly IReporteRepository _repo;
    public CuadresReporteController(IReporteRepository repo) => _repo = repo;

    [HttpGet("cuadres-diarios")]
    public async Task<ActionResult<CuadresDiariosReporteDto>> CuadresDiarios([FromQuery] int? anio, [FromQuery] int? mes, CancellationToken ct)
    {
        var hoy = DateTime.Today;
        var a = anio ?? hoy.Year;
        var m = mes ?? hoy.Month;
        if (m < 1 || m > 12) return BadRequest(new { mensaje = "Mes inválido." });
        return Ok(await _repo.CuadresDiariosAsync(a, m, SedeRequeridaId, ct));
    }
}
