using Lavanderia.Api.Services.Facturacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Lavanderia.Api.Controllers;

/// <summary>Consulta de documentos de identidad para autocompletar y validar formularios.</summary>
[ApiController]
[Authorize]
[Route("api/documentos")]
[EnableRateLimiting("public-read")]
public class DocumentosController(RucConsultaService rucs) : ControllerBase
{
    /// <summary>Razón social, estado y condición de un RUC según el padrón de SUNAT.</summary>
    [HttpGet("ruc/{ruc}")]
    public async Task<IActionResult> Ruc(string ruc, CancellationToken ct)
    {
        var r = await rucs.ConsultarAsync(ruc, ct);
        return Ok(new
        {
            r.Ruc, r.FormatoValido, r.Verificado, r.Existe, r.RazonSocial, r.Estado, r.Condicion, r.Direccion,
            r.ActivoHabido, r.Problema, r.Advertencia
        });
    }

    /// <summary>Nombre de una persona por DNI, para autocompletar al registrar clientes.</summary>
    [HttpGet("dni/{dni}")]
    public async Task<IActionResult> Dni(string dni, CancellationToken ct) => Ok(await rucs.ConsultarDniAsync(dni, ct));
}
