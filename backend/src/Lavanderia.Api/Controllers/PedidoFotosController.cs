using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

/// <summary>
/// Fotos de evidencia de un pedido (recepcion/clasificacion de prendas y entrega). El personal
/// las sube desde el celular; el cliente las ve luego en su pagina de seguimiento.
/// </summary>
[Route("api/pedidos")]
public class PedidoFotosController : TenantAwareControllerBase
{
    private const int MaxBytes = 8 * 1024 * 1024;      // 8 MB (el front ya comprime; esto es tope duro)
    private const int MaxPorPedido = 15;
    private static readonly Dictionary<string, string> TiposPermitidos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp"
    };
    private static readonly string[] Momentos = { "RECEPCION", "ENTREGA", "OTRO" };

    private readonly IPedidoFotoRepository _fotos;
    private readonly IAlmacenamientoFotos _almacen;
    private readonly IPedidoService _pedidos;

    public PedidoFotosController(IPedidoFotoRepository fotos, IAlmacenamientoFotos almacen, IPedidoService pedidos)
    {
        _fotos = fotos;
        _almacen = almacen;
        _pedidos = pedidos;
    }

    [HttpGet("{pedidoId:int}/fotos")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<List<FotoPedidoDto>>> Listar(int pedidoId, CancellationToken ct)
    {
        if (await _pedidos.ObtenerAsync(pedidoId, SedeRequeridaId, ct) is null) return NotFound();
        var fotos = await _fotos.ListarPorPedidoAsync(pedidoId, SedeRequeridaId, ct);
        return Ok(fotos.Select(f => new FotoPedidoDto(f.Id, f.Momento, f.FechaSubida, f.TamanoBytes)).ToList());
    }

    [HttpPost("{pedidoId:int}/fotos")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    [RequestSizeLimit(MaxBytes + 1024 * 1024)]
    public async Task<ActionResult<FotoPedidoDto>> Subir(int pedidoId, [FromForm] IFormFile? archivo, [FromForm] string? momento, CancellationToken ct)
    {
        var pedido = await _pedidos.ObtenerAsync(pedidoId, SedeRequeridaId, ct);
        if (pedido is null)
            return NotFound(new { mensaje = "El pedido no existe o no pertenece a tu sede." });

        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "No se recibió ninguna imagen." });
        if (archivo.Length > MaxBytes)
            return BadRequest(new { mensaje = "La imagen es demasiado grande (máx. 8 MB)." });
        if (!TiposPermitidos.ContainsKey(archivo.ContentType))
            return BadRequest(new { mensaje = "Formato no permitido. Sube una imagen JPG, PNG o WEBP." });

        if (await _fotos.ContarPorPedidoAsync(pedidoId, SedeRequeridaId, ct) >= MaxPorPedido)
            return BadRequest(new { mensaje = $"Este pedido ya tiene el máximo de {MaxPorPedido} fotos." });

        var momentoNorm = Momentos.Contains(momento, StringComparer.OrdinalIgnoreCase)
            ? momento!.ToUpperInvariant() : "OTRO";

        using var ms = new MemoryStream();
        await archivo.CopyToAsync(ms, ct);
        var datos = ms.ToArray();

        // El Content-Type lo declara el cliente: el tipo que vale es el de los bytes reales.
        var tipoReal = ImagenValidador.Detectar(datos);
        if (tipoReal is null)
            return BadRequest(new { mensaje = "El archivo no es una imagen válida (JPG, PNG o WEBP)." });
        var (contentTypeReal, extension) = tipoReal.Value;

        var nombreArchivo = await _almacen.GuardarAsync(NegocioId, pedidoId, pedido.Numero, datos, extension, ct);
        var foto = new PedidoFoto
        {
            PedidoId = pedidoId,
            SedeId = SedeRequeridaId,
            NegocioId = NegocioId,
            Momento = momentoNorm,
            NombreArchivo = nombreArchivo,
            ContentType = contentTypeReal,
            TamanoBytes = (int)archivo.Length,
            SubidoPorUsuarioId = UsuarioId
        };
        foto.Id = await _fotos.CrearAsync(foto, ct);

        return Ok(new FotoPedidoDto(foto.Id, foto.Momento, foto.FechaSubida == default ? DateTime.Now : foto.FechaSubida, foto.TamanoBytes));
    }

    [HttpGet("{pedidoId:int}/fotos/{fotoId:int}/archivo")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> Archivo(int pedidoId, int fotoId, CancellationToken ct)
    {
        var foto = await _fotos.ObtenerAsync(fotoId, SedeRequeridaId, ct);
        if (foto is null || foto.PedidoId != pedidoId) return NotFound();

        var stream = _almacen.Abrir(foto.NegocioId, foto.PedidoId, foto.NombreArchivo);
        if (stream is null) return NotFound();
        return File(stream, foto.ContentType);
    }

    [HttpDelete("{pedidoId:int}/fotos/{fotoId:int}")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> Eliminar(int pedidoId, int fotoId, CancellationToken ct)
    {
        var foto = await _fotos.EliminarAsync(fotoId, SedeRequeridaId, ct);
        if (foto is null || foto.PedidoId != pedidoId) return NotFound();
        _almacen.Eliminar(foto.NegocioId, foto.PedidoId, foto.NombreArchivo);
        return NoContent();
    }
}
