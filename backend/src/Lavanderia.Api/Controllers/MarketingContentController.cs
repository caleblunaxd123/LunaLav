using Lavanderia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[ApiController, Authorize(Policy = "Marketing"), Route("api/marketing/publicaciones")]
public class MarketingContentController(OllamaService ollama) : ControllerBase
{
    [HttpPost("generar")]
    public async Task<IActionResult> Generar([FromBody] PublicacionPrompt p, CancellationToken ct)
    {
        try { return Ok(new { texto = await ollama.GenerarPublicacionAsync(p, ct) }); }
        catch (InvalidOperationException e) { return StatusCode(503, new { mensaje = e.Message }); }
    }

    [HttpPost("respuesta")]
    public async Task<IActionResult> Respuesta([FromBody] PublicacionRespuestaRequest r, CancellationToken ct)
    {
        try { return Ok(new { texto = await ollama.GenerarRespuestaAsync(r.Contexto ?? "", r.Instruccion, r.Modelo, ct) }); }
        catch (InvalidOperationException e) { return StatusCode(503, new { mensaje = e.Message }); }
    }
}

public record PublicacionRespuestaRequest(string? Contexto, string? Instruccion, string? Modelo);
