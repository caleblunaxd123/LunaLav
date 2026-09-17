using Lavanderia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Lavanderia.Api.Controllers;

[Route("api/geocodificacion")]
[Authorize(Policy = "Modulo:PEDIDOS")]
public class GeocodificacionController : TenantAwareControllerBase
{
    private readonly GeocodificacionService _geocodificacion;

    public GeocodificacionController(GeocodificacionService geocodificacion)
        => _geocodificacion = geocodificacion;

    [HttpGet("buscar")]
    public async Task<ActionResult<IReadOnlyList<ResultadoGeocodificacion>>> Buscar(
        [FromQuery] string direccion, [FromQuery] string distrito, CancellationToken ct)
    {
        direccion = (direccion ?? "").Trim();
        distrito = (distrito ?? "").Trim();
        if (direccion.Length is < 4 or > 250 || distrito.Length is < 2 or > 100)
            return BadRequest(new { mensaje = "Escribe una dirección y un distrito válidos." });

        try
        {
            return Ok(await _geocodificacion.BuscarAsync(direccion, distrito, ct));
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensaje = "El servicio de mapas no está disponible. Intenta nuevamente." });
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout,
                new { mensaje = "El servicio de mapas tardó demasiado en responder." });
        }
        catch (JsonException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensaje = "El servicio de mapas devolvió una respuesta no válida." });
        }
    }

    [HttpGet("reversa")]
    public async Task<ActionResult<ResultadoGeocodificacion>> Reversa(
        [FromQuery] decimal latitud, [FromQuery] decimal longitud, CancellationToken ct)
    {
        if (latitud is < -90 or > 90 || longitud is < -180 or > 180)
            return BadRequest(new { mensaje = "Las coordenadas no son válidas." });

        try
        {
            var resultado = await _geocodificacion.ReversaAsync(latitud, longitud, ct);
            return resultado is null ? NotFound(new { mensaje = "El mapa no reconoció ese punto." }) : Ok(resultado);
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensaje = "El servicio de mapas no está disponible. Intenta nuevamente." });
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout,
                new { mensaje = "El servicio de mapas tardó demasiado en responder." });
        }
        catch (JsonException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { mensaje = "El servicio de mapas devolvió una respuesta no válida." });
        }
    }
}
