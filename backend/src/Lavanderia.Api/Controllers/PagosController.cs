using System.Text.RegularExpressions;
using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services.Facturacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/pagos")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class PagosController : TenantAwareControllerBase
{
    private static readonly Regex CodigoComercioValido = new("^[A-Za-z0-9_-]{3,50}$", RegexOptions.Compiled);

    private readonly IPagosRepository _pagos;
    private readonly SecretProtector _secretos;

    public PagosController(IPagosRepository pagos, SecretProtector secretos)
    {
        _pagos = pagos;
        _secretos = secretos;
    }

    [HttpGet("configuracion")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<ConfiguracionPagosDto>> ObtenerConfiguracion(CancellationToken ct)
    {
        var c = await _pagos.ObtenerConfigAsync(NegocioId, ct);
        return Ok(new ConfiguracionPagosDto
        {
            Proveedor = "IZIPAY",
            CodigoComercio = c?.CodigoComercio,
            PublicKey = c?.PublicKey,
            Activo = false,
            TieneApiKey = !string.IsNullOrEmpty(c?.ApiKeyCifrada),
            TieneHashKey = !string.IsNullOrEmpty(c?.HashKeyCifrada),
            IntegracionDisponible = false
        });
    }

    [HttpPut("configuracion")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> GuardarConfiguracion([FromBody] ConfiguracionPagosDto dto, CancellationToken ct)
    {
        if (!string.Equals(dto.Proveedor, "IZIPAY", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { mensaje = "El proveedor definido para la nueva integracion es Izipay." });

        if (dto.Activo)
            return BadRequest(new
            {
                mensaje = "Izipay aun no puede activarse. Primero deben completarse la integracion, las credenciales y las pruebas en sandbox."
            });

        var codigoComercio = Normalizar(dto.CodigoComercio);
        var publicKey = Normalizar(dto.PublicKey);
        var apiKeyNueva = Normalizar(dto.ApiKeyNueva);
        var hashKeyNueva = Normalizar(dto.HashKeyNueva);

        if (codigoComercio is not null && !CodigoComercioValido.IsMatch(codigoComercio))
            return BadRequest(new { mensaje = "El codigo de comercio contiene caracteres no validos." });
        if (publicKey is not null && publicKey.Length < 50)
            return BadRequest(new { mensaje = "La llave publica RSA de Izipay parece incompleta." });
        if (apiKeyNueva is not null && apiKeyNueva.Length < 8)
            return BadRequest(new { mensaje = "La clave API de Izipay parece incompleta." });
        if (hashKeyNueva is not null && hashKeyNueva.Length < 8)
            return BadRequest(new { mensaje = "La clave Hash de Izipay parece incompleta." });

        var existente = await _pagos.ObtenerConfigAsync(NegocioId, ct)
            ?? new ConfiguracionPagos { NegocioId = NegocioId };

        existente.Proveedor = "IZIPAY";
        existente.CodigoComercio = codigoComercio;
        existente.PublicKey = publicKey;
        existente.Activo = false;
        if (apiKeyNueva is not null) existente.ApiKeyCifrada = _secretos.Proteger(apiKeyNueva);
        if (hashKeyNueva is not null) existente.HashKeyCifrada = _secretos.Proteger(hashKeyNueva);

        await _pagos.GuardarConfigAsync(existente, ct);
        return NoContent();
    }

    private static string? Normalizar(string? valor)
    {
        var limpio = valor?.Trim();
        return string.IsNullOrWhiteSpace(limpio) ? null : limpio;
    }
}
