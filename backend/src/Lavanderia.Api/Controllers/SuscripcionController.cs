using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>Aviso de vencimiento de suscripción que la propia empresa ve en su dashboard.
/// A diferencia de NegociosController (solo PROPIETARIO), esto lo consulta cualquier usuario
/// autenticado de la empresa, pero SIEMPRE acotado a su propio negocio (NegocioId del JWT).</summary>
[ApiController]
[Authorize]
[Route("api/suscripcion")]
public class SuscripcionController : TenantAwareControllerBase
{
    private readonly INegocioRepository _negocios;
    public SuscripcionController(INegocioRepository negocios) => _negocios = negocios;

    [HttpGet("mia")]
    public async Task<ActionResult<MiSuscripcionDto>> Mia(CancellationToken ct)
    {
        var n = await _negocios.ObtenerPorIdAsync(NegocioId, ct);
        if (n is null)
            return Ok(new MiSuscripcionDto(false, "OK", "", null, null, "ACTIVA"));

        int? dias = null;
        if (n.ProximoPago is DateOnly pp)
            dias = (pp.ToDateTime(TimeOnly.MinValue).Date - DateTime.Now.Date).Days;

        var vencida = n.EstadoSuscripcion == "VENCIDA" || (dias is int d0 && d0 < 0);
        if (vencida)
            return Ok(new MiSuscripcionDto(true, "VENCIDA",
                "Estimado cliente, tu suscripción ya venció. Por favor, renuévala para seguir usando el sistema.",
                n.ProximoPago, dias, n.EstadoSuscripcion));

        // Aviso solo dentro de los últimos 5 días antes del vencimiento.
        if (dias is int d && d >= 0 && d <= 5)
        {
            var plural = d == 1 ? "día" : "días";
            var texto = d == 0
                ? "Estimado cliente, tu suscripción vence hoy. Por favor, renuévala."
                : $"Estimado cliente, tu suscripción está próxima a vencer en {d} {plural}.";
            return Ok(new MiSuscripcionDto(true, "AVISO", texto, n.ProximoPago, dias, n.EstadoSuscripcion));
        }

        return Ok(new MiSuscripcionDto(false, "OK", "", n.ProximoPago, dias, n.EstadoSuscripcion));
    }
}
