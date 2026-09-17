using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/configuracion")]
[Microsoft.AspNetCore.Authorization.Authorize(Policy = "Modulo:AJUSTES")]
public class ConfiguracionController : TenantAwareControllerBase
{
    private const int LogoMaxBytes = 2 * 1024 * 1024;   // 2 MB: un logo no necesita más

    private readonly IConfiguracionNegocioRepository _repo;
    private readonly INegocioRepository _negocios;
    private readonly IServicioRepository _servicios;
    private readonly Services.IAlmacenamientoFotos _almacen;

    public ConfiguracionController(IConfiguracionNegocioRepository repo, INegocioRepository negocios,
        IServicioRepository servicios, Services.IAlmacenamientoFotos almacen)
    {
        _repo = repo;
        _negocios = negocios;
        _servicios = servicios;
        _almacen = almacen;
    }

    /// <summary>
    /// Sube el logo del negocio desde el equipo del usuario. Devuelve la URL con la que
    /// queda guardado (se envía luego en LogoUrl al guardar la configuración).
    /// </summary>
    [HttpPost("logo")]
    [Authorize(Roles = "ADMIN")]
    [RequestSizeLimit(LogoMaxBytes + 512 * 1024)]
    public async Task<IActionResult> SubirLogo(IFormFile? archivo, CancellationToken ct)
    {
        if (archivo is null || archivo.Length == 0)
            return BadRequest(new { mensaje = "No se recibió ninguna imagen." });
        if (archivo.Length > LogoMaxBytes)
            return BadRequest(new { mensaje = "La imagen es demasiado grande (máx. 2 MB)." });

        using var ms = new MemoryStream();
        await archivo.CopyToAsync(ms, ct);
        var datos = ms.ToArray();

        // El tipo se decide por los bytes reales, no por el Content-Type que declara el cliente.
        var tipo = Services.ImagenValidador.Detectar(datos);
        if (tipo is null)
            return BadRequest(new { mensaje = "El archivo no es una imagen válida (JPG, PNG o WEBP)." });

        var nombre = await _almacen.GuardarLogoAsync(NegocioId, datos, tipo.Value.Extension, ct);
        return Ok(new { logoUrl = $"/api/configuracion/logo/{nombre}" });
    }

    /// <summary>
    /// Sirve el logo. Público: el login lo muestra antes de que el usuario se autentique.
    /// El nombre de archivo lleva el negocio, así que no se puede pedir el de otro tenant.
    /// </summary>
    [HttpGet("logo/{nombre}")]
    [AllowAnonymous]
    public IActionResult Logo(string nombre)
    {
        var guion = nombre.IndexOf('-', "negocio-".Length);
        if (!nombre.StartsWith("negocio-", StringComparison.Ordinal) || guion < 0
            || !int.TryParse(nombre["negocio-".Length..guion], out var negocioId))
            return NotFound();

        var stream = _almacen.AbrirLogo(negocioId, nombre);
        if (stream is null) return NotFound();

        var contentType = Path.GetExtension(nombre).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        return File(stream, contentType);
    }

    /// <summary>
    /// Si hay sesion valida, devuelve la configuracion del negocio del usuario autenticado.
    /// Sin sesion, se usa el primer negocio de esta instancia (fallback generico para accesos
    /// sin slug de empresa en la URL — ver GET /publico/{slug} para el caso normal de login).
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ConfiguracionNegocioDto>> Obtener(CancellationToken ct)
    {
        var negocioId = User.Identity?.IsAuthenticated == true ? NegocioId : (int?)null;
        var c = await _repo.ObtenerAsync(negocioId, ct);
        if (c is null) return NotFound();
        var dto = Map(c);
        if (negocioId.HasValue)
            dto.ServicioDeliveryId = (await _servicios.ObtenerCargoDeliveryAsync(negocioId.Value, ct))?.Id;
        return Ok(dto);
    }

    /// <summary>
    /// Marca del negocio identificado por el slug de su URL (ej. /mi-lavanderia/login), para pintarla
    /// antes de que el usuario inicie sesion. Publico: no expone nada que no exponga ya el
    /// endpoint anonimo de arriba (RUC/IGV son datos fiscales de por si publicos en Peru).
    /// </summary>
    [HttpGet("publico/{slug}")]
    [AllowAnonymous]
    public async Task<ActionResult<ConfiguracionNegocioDto>> ObtenerPorSlug(string slug, CancellationToken ct)
    {
        var negocio = await _negocios.ObtenerPorSlugAsync(slug, ct);
        if (negocio is null) return NotFound();
        var c = await _repo.ObtenerAsync(negocio.Id, ct);
        if (c is null) return NotFound();
        var dto = Map(c);
        dto.ServicioDeliveryId = (await _servicios.ObtenerCargoDeliveryAsync(negocio.Id, ct))?.Id;
        return Ok(dto);
    }

    [HttpPut]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> Actualizar([FromBody] ConfiguracionNegocioDto dto, CancellationToken ct)
    {
        // Upsert: un negocio recien creado aun no tiene fila propia en ConfiguracionNegocio.
        var existente = await _repo.ObtenerAsync(NegocioId, ct) ?? new ConfiguracionNegocio();

        existente.NombreNegocio = dto.NombreNegocio;
        existente.LogoUrl = dto.LogoUrl;
        existente.ColorPrimario = dto.ColorPrimario;
        existente.ColorSecundario = dto.ColorSecundario;
        existente.ColorAcento = dto.ColorAcento;
        existente.Direccion = dto.Direccion;
        existente.Telefono = dto.Telefono;
        existente.Ruc = dto.Ruc;
        existente.HorarioAtencion = dto.HorarioAtencion;
        existente.Igv = dto.Igv;
        existente.MetaMensual = dto.MetaMensual;
        existente.SolesPorPunto = dto.SolesPorPunto;
        existente.AnchoTicketMm = dto.AnchoTicketMm;
        existente.MensajePieTicket = dto.MensajePieTicket;
        existente.CondicionesServicio = dto.CondicionesServicio;
        existente.NotasProduccion = dto.NotasProduccion;
        existente.CostoDelivery = dto.CostoDelivery;
        existente.ValorPuntoCanje = dto.ValorPuntoCanje;
        existente.MaxDescuentoPct = dto.MaxDescuentoPct;
        existente.YapeNumero = dto.YapeNumero;
        existente.YapeTitular = dto.YapeTitular;
        existente.YapeQrUrl = dto.YapeQrUrl;

        await _repo.ActualizarAsync(existente, NegocioId, ct);

        // El servidor jamas confia en el precio que manda el cliente al crear un pedido: siempre
        // recalcula Total = Servicio.Precio * Cantidad (ver PedidoService.CrearAsync). Por eso el
        // Precio del servicio de sistema debe reflejar el CostoDelivery configurado aqui, o el
        // cargo de delivery se aplicaria como S/ 0 en el pedido real sin importar lo que Registrar
        // muestre en pantalla.
        var servicioDelivery = await _servicios.ObtenerCargoDeliveryAsync(NegocioId, ct);
        if (servicioDelivery is not null && servicioDelivery.Precio != dto.CostoDelivery)
        {
            servicioDelivery.Precio = dto.CostoDelivery;
            await _servicios.ActualizarAsync(servicioDelivery, NegocioId, ct);
        }

        return NoContent();
    }

    private static ConfiguracionNegocioDto Map(ConfiguracionNegocio c) => new()
    {
        Id = c.Id,
        NombreNegocio = c.NombreNegocio,
        LogoUrl = c.LogoUrl,
        ColorPrimario = c.ColorPrimario,
        ColorSecundario = c.ColorSecundario,
        ColorAcento = c.ColorAcento,
        Direccion = c.Direccion,
        Telefono = c.Telefono,
        Ruc = c.Ruc,
        HorarioAtencion = c.HorarioAtencion,
        Igv = c.Igv,
        MetaMensual = c.MetaMensual,
        SolesPorPunto = c.SolesPorPunto,
        AnchoTicketMm = c.AnchoTicketMm,
        MensajePieTicket = c.MensajePieTicket,
        CondicionesServicio = c.CondicionesServicio,
        NotasProduccion = c.NotasProduccion,
        CostoDelivery = c.CostoDelivery,
        ValorPuntoCanje = c.ValorPuntoCanje,
        MaxDescuentoPct = c.MaxDescuentoPct,
        YapeNumero = c.YapeNumero,
        YapeTitular = c.YapeTitular,
        YapeQrUrl = c.YapeQrUrl
    };
}
