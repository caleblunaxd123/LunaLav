using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Infrastructure;
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
    private readonly ISqlConnectionFactory _db;
    public PlataformaController(IConfiguracionPlataformaRepository cfg, ISqlConnectionFactory db) { _cfg = cfg; _db = db; }

    [HttpGet("interesados")]
    public async Task<ActionResult<List<InteresadoDemoDto>>> Interesados(CancellationToken ct)
    {
        var lista = new List<InteresadoDemoDto>();
        await using var conn = _db.Create(); await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TOP 200 Id,Nombre,Negocio,Celular,Email,PlanInteres,Consentimiento,EstadoSeguimiento,NotaSeguimiento,MensajeAprobado,FechaAprobacion,FechaUltimoSeguimiento,FechaCreacion FROM dbo.InteresadoDemo ORDER BY FechaCreacion DESC";
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct)) lista.Add(new(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.IsDBNull(4) ? null : rd.GetString(4), rd.GetString(5), rd.GetBoolean(6), rd.GetString(7), rd.IsDBNull(8) ? null : rd.GetString(8), rd.IsDBNull(9) ? null : rd.GetString(9), rd.IsDBNull(10) ? null : rd.GetDateTime(10), rd.IsDBNull(11) ? null : rd.GetDateTime(11), rd.GetDateTime(12)));
        return Ok(lista);
    }

    [HttpPatch("interesados/{id:int}/aprobar")]
    public async Task<IActionResult> AprobarSeguimiento(int id, [FromBody] AprobarSeguimientoInteresadoRequest req, CancellationToken ct)
    {
        var canal = req.Canal.Trim().ToUpperInvariant();
        if (canal is not ("WHATSAPP" or "EMAIL")) return BadRequest(new { mensaje = "Canal inválido." });
        await using var conn = _db.Create(); await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE dbo.InteresadoDemo
            SET EstadoSeguimiento=N'APROBADO_PARA_ENVIO', MensajeAprobado=@mensaje, NotaSeguimiento=@nota,
                AprobadoPor=@usuario, FechaAprobacion=SYSUTCDATETIME()
            WHERE Id=@id AND Consentimiento=1";
        cmd.Parameters.AddWithValue("@id", id); cmd.Parameters.AddWithValue("@mensaje", (object?)req.Mensaje?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nota", (object?)req.Nota?.Trim() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@usuario", User.Identity?.Name ?? "Propietario");
        if (await cmd.ExecuteNonQueryAsync(ct) == 0) return NotFound(new { mensaje = "Interesado no encontrado o sin consentimiento." });
        return NoContent();
    }

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
