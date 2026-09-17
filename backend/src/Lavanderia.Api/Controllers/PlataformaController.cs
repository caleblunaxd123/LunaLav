using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// Configuración del dueño del SaaS (fila única): datos de cobro (Yape) y contacto, usados en
/// los recordatorios de cobro y los recibos. Solo el rol PROPIETARIO.
/// </summary>
[ApiController]
[Authorize(Roles = "PROPIETARIO")]
[Route("api/plataforma")]
public class PlataformaController : ControllerBase
{
    private readonly IConfiguracionPlataformaRepository _cfg;
    public PlataformaController(IConfiguracionPlataformaRepository cfg) => _cfg = cfg;

    [HttpGet("configuracion")]
    public async Task<ActionResult<ConfiguracionPlataformaDto>> Obtener(CancellationToken ct)
    {
        var c = await _cfg.ObtenerAsync(ct);
        return Ok(new ConfiguracionPlataformaDto
        {
            NombrePlataforma = c.NombrePlataforma,
            YapeNombre = c.YapeNombre,
            YapeNumero = c.YapeNumero,
            ContactoSoporte = c.ContactoSoporte,
            DiasAvisoCobro = c.DiasAvisoCobro
        });
    }

    [HttpPut("configuracion")]
    public async Task<IActionResult> Actualizar([FromBody] ConfiguracionPlataformaDto dto, CancellationToken ct)
    {
        await _cfg.ActualizarAsync(new ConfiguracionPlataforma
        {
            NombrePlataforma = string.IsNullOrWhiteSpace(dto.NombrePlataforma) ? "LunaLav" : dto.NombrePlataforma.Trim(),
            YapeNombre = Limpio(dto.YapeNombre),
            YapeNumero = Limpio(dto.YapeNumero),
            ContactoSoporte = Limpio(dto.ContactoSoporte),
            DiasAvisoCobro = Math.Clamp(dto.DiasAvisoCobro, 0, 60)
        }, ct);
        return NoContent();
    }

    private static string? Limpio(string? v)
    {
        var t = v?.Trim();
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }
}
