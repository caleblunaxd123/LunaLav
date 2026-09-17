using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// Administración del catálogo de servicios (solo ADMIN).
/// El endpoint público /api/servicios (en CatalogosController) devuelve solo los activos para el wizard.
/// </summary>
[Route("api/servicios-admin")]
[Authorize(Roles = "ADMIN")]
[Authorize(Policy = "Modulo:AJUSTES")]
public class ServiciosAdminController : TenantAwareControllerBase
{
    private readonly IServicioRepository _repo;
    private readonly ICategoriaRepository _categorias;

    public ServiciosAdminController(IServicioRepository repo, ICategoriaRepository categorias)
    {
        _repo = repo;
        _categorias = categorias;
    }

    [HttpGet]
    public async Task<ActionResult<List<ServicioEditableDto>>> Listar(CancellationToken ct)
        => Ok((await _repo.ListarTodosAsync(NegocioId, ct)).Select(Map).ToList());

    [HttpPost]
    public async Task<ActionResult<ServicioEditableDto>> Crear([FromBody] ServicioEditableDto dto, CancellationToken ct)
    {
        var validacion = await ValidarCatalogoAsync(dto, null, ct);
        if (validacion is not null) return validacion;

        var id = await _repo.CrearAsync(new Servicio
        {
            NegocioId = NegocioId,
            Nombre = dto.Nombre.Trim(),
            Precio = dto.Precio,
            Costo = dto.Costo,
            Unidad = dto.Unidad.Trim(),
            CategoriaId = dto.CategoriaId,
            Activo = dto.Activo
        }, ct);
        var creado = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        return CreatedAtAction(nameof(Listar), Map(creado!));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] ServicioEditableDto dto, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();
        if (existente.EsCargoDelivery)
            return BadRequest(new { mensaje = "El cargo interno de delivery se configura desde Negocio y marca." });

        var validacion = await ValidarCatalogoAsync(dto, id, ct);
        if (validacion is not null) return validacion;

        existente.Nombre = dto.Nombre.Trim();
        existente.Precio = dto.Precio;
        existente.Costo = dto.Costo;
        existente.Unidad = dto.Unidad.Trim();
        existente.CategoriaId = dto.CategoriaId;
        existente.Activo = dto.Activo;
        await _repo.ActualizarAsync(existente, NegocioId, ct);
        return NoContent();
    }

    /// <summary>
    /// Carga masiva de servicios. Recibe filas ya parseadas (el frontend lee el CSV / pegado de Excel).
    /// Valida fila por fila: omite duplicados y filas inválidas sin abortar el resto, y opcionalmente
    /// crea las categorías que no existan. Devuelve un resumen con lo creado y lo omitido.
    /// </summary>
    [HttpPost("importar")]
    public async Task<ActionResult<ImportarServiciosResultado>> Importar([FromBody] ImportarServiciosRequest req, CancellationToken ct)
    {
        if (req.Filas is null || req.Filas.Count == 0)
            return BadRequest(new { mensaje = "No se recibió ninguna fila para importar." });
        if (req.Filas.Count > 500)
            return BadRequest(new { mensaje = "Máximo 500 servicios por importación. Divide el archivo en partes." });

        var resultado = new ImportarServiciosResultado();

        // Índice de categorías existentes por nombre normalizado (se va ampliando con las que creemos).
        var mapaCategorias = (await _categorias.ListarTodasAsync(NegocioId, ct))
            .GroupBy(c => Normalizar(c.Nombre))
            .ToDictionary(g => g.Key, g => g.First().Id);

        // Nombres ya procesados en este mismo lote, para no insertar duplicados internos.
        var nombresLote = new HashSet<string>();

        var fila = 0;
        foreach (var f in req.Filas)
        {
            fila++;
            var nombre = (f.Nombre ?? "").Trim();
            var unidad = (f.Unidad ?? "").Trim();

            // Fila totalmente vacía: se ignora en silencio (no cuenta como error).
            if (nombre.Length == 0 && unidad.Length == 0 && f.Precio == 0m) continue;

            if (nombre.Length < 2 || nombre.Length > 120)
            { resultado.Errores.Add(new() { Fila = fila, Nombre = nombre, Motivo = "Nombre inválido (debe tener entre 2 y 120 caracteres)." }); continue; }
            if (f.Precio < 0.01m || f.Precio > 10000m)
            { resultado.Errores.Add(new() { Fila = fila, Nombre = nombre, Motivo = "Precio fuera de rango (mayor a 0 y hasta 10 000)." }); continue; }

            if (unidad.Length == 0) unidad = "und";
            if (unidad.Length > 30) unidad = unidad[..30];

            var claveNombre = Normalizar(nombre);
            if (!nombresLote.Add(claveNombre))
            { resultado.Omitidos++; resultado.Errores.Add(new() { Fila = fila, Nombre = nombre, Motivo = "Repetido dentro del archivo." }); continue; }
            if (await _repo.ExisteNombreAsync(nombre, NegocioId, null, ct))
            { resultado.Omitidos++; resultado.Errores.Add(new() { Fila = fila, Nombre = nombre, Motivo = "Ya existe un servicio con ese nombre." }); continue; }

            int? categoriaId = null;
            var categoria = (f.Categoria ?? "").Trim();
            if (categoria.Length > 0)
            {
                var claveCat = Normalizar(categoria);
                if (mapaCategorias.TryGetValue(claveCat, out var cid))
                {
                    categoriaId = cid;
                }
                else if (req.CrearCategorias && categoria.Length is >= 2 and <= 80)
                {
                    var nuevoId = await _categorias.CrearAsync(
                        new Categoria { NegocioId = NegocioId, Nombre = categoria, Activa = true }, ct);
                    mapaCategorias[claveCat] = nuevoId;
                    categoriaId = nuevoId;
                    resultado.CategoriasCreadas.Add(categoria);
                }
                // Si la categoría no existe y no se pide crearla, el servicio queda sin categoría (no es error).
            }

            await _repo.CrearAsync(new Servicio
            {
                NegocioId = NegocioId,
                Nombre = nombre,
                Precio = f.Precio,
                Unidad = unidad,
                CategoriaId = categoriaId,
                Activo = true
            }, ct);
            resultado.Creados++;
        }

        return Ok(resultado);
    }

    private static string Normalizar(string valor) => valor.Trim().ToUpperInvariant();

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
    {
        var existente = await _repo.ObtenerPorIdAsync(id, NegocioId, ct);
        if (existente is null) return NotFound();
        if (existente.EsCargoDelivery)
            return BadRequest(new { mensaje = "El cargo interno de delivery no puede desactivarse desde Servicios." });

        var usos = await _repo.ContarUsoAsync(id, NegocioId, ct);
        if (usos == 0)
        {
            await _repo.EliminarAsync(id, NegocioId, ct);
            return Ok(new { mensaje = "Servicio eliminado.", eliminado = true });
        }
        await _repo.CambiarEstadoAsync(id, false, NegocioId, ct);
        return Ok(new
        {
            mensaje = $"No se puede eliminar: está usado en {usos} pedido(s). Se desactivó.",
            eliminado = false
        });
    }

    private async Task<ActionResult?> ValidarCatalogoAsync(ServicioEditableDto dto, int? excluirId, CancellationToken ct)
    {
        var nombre = dto.Nombre.Trim();
        if (await _repo.ExisteNombreAsync(nombre, NegocioId, excluirId, ct))
            return Conflict(new { mensaje = $"Ya existe un servicio llamado '{nombre}' en esta empresa." });

        if (dto.CategoriaId.HasValue &&
            await _categorias.ObtenerPorIdAsync(dto.CategoriaId.Value, NegocioId, ct) is null)
            return BadRequest(new { mensaje = "La categoría seleccionada no pertenece a esta empresa." });

        return null;
    }

    private static ServicioEditableDto Map(Servicio s) => new()
    {
        Id = s.Id,
        Nombre = s.Nombre,
        Precio = s.Precio,
        Costo = s.Costo,
        Unidad = s.Unidad,
        CategoriaId = s.CategoriaId,
        CategoriaNombre = s.CategoriaNombre,
        Activo = s.Activo,
        EnUso = s.EnUso
    };
}
