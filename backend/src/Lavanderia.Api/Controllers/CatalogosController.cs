using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api")]
public class CatalogosController : TenantAwareControllerBase
{
    private readonly IServicioRepository _servicios;
    private readonly IAreaLavadoRepository _areas;
    private readonly IPlantillaWhatsappRepository _plantillas;

    public CatalogosController(IServicioRepository servicios, IAreaLavadoRepository areas, IPlantillaWhatsappRepository plantillas)
    {
        _servicios = servicios;
        _areas = areas;
        _plantillas = plantillas;
    }

    [HttpGet("servicios")]
    public async Task<ActionResult<List<ServicioDto>>> Servicios(CancellationToken ct)
        => Ok((await _servicios.ListarActivosAsync(NegocioId, ct))
                .Select(s => new ServicioDto(s.Id, s.Nombre, s.Precio, s.Unidad, s.CategoriaId))
                .ToList());

    /// <summary>
    /// Alta rápida de un servicio durante el registro de un pedido. A diferencia de la gestión
    /// completa del catálogo (servicios-admin, solo ADMIN), esto lo puede hacer quien registra
    /// pedidos (módulo REGISTRAR): crea un servicio activo básico y devuelve el servicio creado.
    /// Si ya existe uno con el mismo nombre, lo reutiliza en lugar de duplicarlo.
    /// </summary>
    [HttpPost("servicios")]
    [Authorize(Policy = "Modulo:REGISTRAR")]
    public async Task<ActionResult<ServicioDto>> CrearServicioRapido([FromBody] ServicioRapidoRequest req, CancellationToken ct)
    {
        var nombre = (req.Nombre ?? "").Trim();
        var unidad = (req.Unidad ?? "").Trim();
        if (nombre.Length < 2) return BadRequest(new { mensaje = "El nombre del producto es muy corto." });
        if (string.IsNullOrWhiteSpace(unidad)) return BadRequest(new { mensaje = "Indica la unidad de cobro (ej. Pz, kg, m2)." });
        if (req.Precio <= 0 || req.Precio > 10000) return BadRequest(new { mensaje = "Ingresa un precio mayor a S/ 0.00 y hasta S/ 10,000.00." });

        // No duplicar el catálogo: si ya existe uno con ese nombre, se reutiliza.
        if (await _servicios.ExisteNombreAsync(nombre, NegocioId, null, ct))
        {
            var existente = (await _servicios.ListarTodosAsync(NegocioId, ct))
                .FirstOrDefault(s => string.Equals(s.Nombre.Trim(), nombre, StringComparison.OrdinalIgnoreCase));
            if (existente is not null)
                return Ok(new ServicioDto(existente.Id, existente.Nombre, existente.Precio, existente.Unidad, existente.CategoriaId));
        }

        var id = await _servicios.CrearAsync(new Servicio
        {
            NegocioId = NegocioId, Nombre = nombre, Precio = req.Precio, Unidad = unidad, CategoriaId = null, Activo = true
        }, ct);
        var creado = await _servicios.ObtenerPorIdAsync(id, NegocioId, ct);
        return Ok(new ServicioDto(creado!.Id, creado.Nombre, creado.Precio, creado.Unidad, creado.CategoriaId));
    }

    [HttpGet("areas-lavado")]
    public async Task<ActionResult<List<AreaLavadoDto>>> Areas(CancellationToken ct)
        => Ok((await _areas.ListarActivasAsync(SedeRequeridaId, ct))
                .Select(a => new AreaLavadoDto(a.Id, a.Nombre, a.Orden, a.TiempoEstMinutos))
                .ToList());

    [HttpGet("plantillas-whatsapp")]
    public async Task<ActionResult<List<PlantillaWhatsappActivaDto>>> PlantillasWhatsapp(CancellationToken ct)
        => Ok((await _plantillas.ListarActivasAsync(NegocioId, ct))
                .Select(p => new PlantillaWhatsappActivaDto(p.Evento, p.Mensaje))
                .ToList());

}
