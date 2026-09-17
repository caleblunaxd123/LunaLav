using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[ApiController]
[Route("api/interesados-demo")]
public class InteresadosDemoController : ControllerBase
{
    private readonly ISqlConnectionFactory _db;
    public InteresadosDemoController(ISqlConnectionFactory db) => _db = db;

    [HttpPost]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("public-write")]
    public async Task<IActionResult> Crear([FromBody] CrearInteresadoDemoRequest req, CancellationToken ct)
    {
        if (!req.Consentimiento) return BadRequest(new { mensaje = "Necesitamos tu autorización para contactarte sobre la prueba." });
        var plan = req.PlanInteres.Trim().ToUpperInvariant();
        if (plan is not ("BASICO" or "FACTURA" or "MULTISEDE")) return BadRequest(new { mensaje = "Plan inválido." });
        await using var conn = _db.Create();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT dbo.InteresadoDemo(Nombre,Negocio,Celular,Email,PlanInteres,Consentimiento) VALUES(@nombre,@negocio,@celular,@email,@plan,1)";
        cmd.Parameters.AddWithValue("@nombre", req.Nombre.Trim()); cmd.Parameters.AddWithValue("@negocio", req.Negocio.Trim());
        cmd.Parameters.AddWithValue("@celular", req.Celular.Trim()); cmd.Parameters.AddWithValue("@email", (object?)req.Email?.Trim() ?? DBNull.Value); cmd.Parameters.AddWithValue("@plan", plan);
        await cmd.ExecuteNonQueryAsync(ct);
        return Ok(new { mensaje = "Solicitud recibida. Te contactaremos para activar tu prueba personalizada." });
    }
}
