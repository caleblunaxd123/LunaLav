using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Lavanderia.Api.Domain;
using Lavanderia.Api.Dtos;
using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services.Facturacion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api")]
public class FacturacionElectronicaController : TenantAwareControllerBase
{
    private static readonly Regex SerieBoleta = new("^B[0-9]{3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SerieFactura = new("^F[0-9]{3}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly IFacturacionRepository _repo;
    private readonly IConfiguracionNegocioRepository _negocio;
    private readonly IPedidoRepository _pedidos;
    private readonly IClienteRepository _clientes;
    private readonly IReadOnlyDictionary<string, IFacturacionElectronicaProvider> _providers;
    private readonly ComprobantePdfGenerator _pdf;
    private readonly SecretProtector _secretos;
    private readonly IRespaldoComprobantes _respaldo;
    private readonly ILogger<FacturacionElectronicaController> _log;

    public FacturacionElectronicaController(IFacturacionRepository repo, IConfiguracionNegocioRepository negocio,
        IPedidoRepository pedidos, IClienteRepository clientes, IEnumerable<IFacturacionElectronicaProvider> providers,
        ComprobantePdfGenerator pdf, SecretProtector secretos, IRespaldoComprobantes respaldo,
        ILogger<FacturacionElectronicaController> log)
    {
        _repo = repo; _negocio = negocio; _pedidos = pedidos; _clientes = clientes;
        _providers = providers.ToDictionary(x => x.Codigo, StringComparer.OrdinalIgnoreCase);
        _pdf = pdf; _secretos = secretos; _respaldo = respaldo; _log = log;
    }

    [HttpGet("facturacion/configuracion")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<ConfiguracionFacturacionDto>> ObtenerConfiguracion(CancellationToken ct)
    {
        var c = await _repo.ObtenerConfigAsync(NegocioId, ct);
        var proveedor = Seleccionar(c?.Proveedor ?? "SUNAT_DIRECTO");
        return Ok(new ConfiguracionFacturacionDto
        {
            RazonSocial = c?.RazonSocial, RucEmisor = c?.RucEmisor, Ambiente = c?.Ambiente ?? "BETA",
            SolUsuario = c?.SolUsuario, SerieBoleta = c?.SerieBoleta ?? "B001", SerieFactura = c?.SerieFactura ?? "F001",
            Activo = c?.Activo ?? false, SoloBoletas = c?.SoloBoletas ?? false, TieneCertificado = c?.CertificadoPfx is { Length: > 0 },
            TieneCredencialesSol = !string.IsNullOrEmpty(c?.SolClaveCifrada), Proveedor = proveedor.Codigo,
            ApiSunatPersonaId = c?.ApiSunatPersonaId,
            TieneCredencialesApiSunat = !string.IsNullOrEmpty(c?.ApiSunatTokenCifrado),
            DireccionFiscal = c?.DireccionFiscal, Ubigeo = c?.Ubigeo,
            CodigoEstablecimiento = c?.CodigoEstablecimiento ?? "0000", EmailEmisor = c?.EmailEmisor,
            CorrelativoBoleta = c?.CorrelativoBoleta ?? 0, CorrelativoFactura = c?.CorrelativoFactura ?? 0,
            RequiereCertificadoLocal = proveedor.RequiereCertificadoLocal
        });
    }

    [HttpPost("facturacion/configuracion/probar")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<ResultadoConexionFacturacionDto>> ProbarConfiguracion(
        [FromBody] ConfiguracionFacturacionDto dto, CancellationToken ct)
    {
        IFacturacionElectronicaProvider proveedor;
        try { proveedor = Seleccionar(dto.Proveedor); }
        catch (InvalidOperationException ex) { return BadRequest(new { mensaje = ex.Message }); }
        if (proveedor.Codigo != "APISUNAT")
            return Bad("La prueba remota esta disponible para APISUNAT. SUNAT directo valida el certificado al guardar.");

        var c = await _repo.ObtenerConfigAsync(NegocioId, ct) ?? new ConfiguracionFacturacion { NegocioId = NegocioId };
        c.Ambiente = dto.Ambiente?.Trim().ToUpperInvariant() ?? "BETA";
        c.RucEmisor = Limpiar(dto.RucEmisor);
        c.RazonSocial = Limpiar(dto.RazonSocial);
        c.ApiSunatPersonaId = Limpiar(dto.ApiSunatPersonaId);
        if (!string.IsNullOrWhiteSpace(dto.ApiSunatTokenNuevo))
            c.ApiSunatTokenCifrado = _secretos.Proteger(dto.ApiSunatTokenNuevo);

        var resultado = await proveedor.ProbarConexionAsync(Credenciales(c), "01",
            (Limpiar(dto.SerieFactura) ?? "F001").ToUpperInvariant(), ct);
        resultado = ValidarAmbiente(resultado, c.Ambiente);
        return Ok(new ResultadoConexionFacturacionDto(resultado.Exitoso, resultado.Mensaje,
            resultado.Produccion, resultado.UltimoNumero, resultado.NumeroSugerido));
    }

    [HttpPut("facturacion/configuracion")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> GuardarConfiguracion([FromBody] ConfiguracionFacturacionDto dto, CancellationToken ct)
    {
        var c = await _repo.ObtenerConfigAsync(NegocioId, ct) ?? new ConfiguracionFacturacion { NegocioId = NegocioId };
        IFacturacionElectronicaProvider proveedor;
        try { proveedor = Seleccionar(dto.Proveedor); }
        catch (InvalidOperationException ex) { return BadRequest(new { mensaje = ex.Message }); }

        var ambiente = dto.Ambiente?.Trim().ToUpperInvariant();
        var ruc = Limpiar(dto.RucEmisor);
        var serieB = (Limpiar(dto.SerieBoleta) ?? "B001").ToUpperInvariant();
        var serieF = (Limpiar(dto.SerieFactura) ?? "F001").ToUpperInvariant();
        var ubigeo = Limpiar(dto.Ubigeo);
        var establecimiento = (Limpiar(dto.CodigoEstablecimiento) ?? "0000").ToUpperInvariant();
        if (ambiente is not ("BETA" or "PRODUCCION")) return Bad("El ambiente debe ser BETA o PRODUCCION.");
        if (ruc is not null && !DocumentoFiscalValidator.EsRucValido(ruc)) return Bad("El RUC emisor no es valido ante SUNAT.");
        if (!SerieBoleta.IsMatch(serieB) || !SerieFactura.IsMatch(serieF)) return Bad("Las series deben usar el formato B001 y F001.");
        if (ubigeo is not null && !Regex.IsMatch(ubigeo, "^[0-9]{6}$")) return Bad("El ubigeo debe tener 6 digitos.");
        if (!Regex.IsMatch(establecimiento, "^[A-Z0-9]{4}$")) return Bad("El codigo de establecimiento debe tener 4 caracteres.");

        c.RazonSocial = Limpiar(dto.RazonSocial); c.RucEmisor = ruc; c.Ambiente = ambiente;
        c.SolUsuario = Limpiar(dto.SolUsuario); c.SerieBoleta = serieB; c.SerieFactura = serieF;
        c.Proveedor = proveedor.Codigo; c.ApiSunatPersonaId = Limpiar(dto.ApiSunatPersonaId);
        c.DireccionFiscal = Limpiar(dto.DireccionFiscal); c.Ubigeo = ubigeo;
        c.CodigoEstablecimiento = establecimiento; c.EmailEmisor = Limpiar(dto.EmailEmisor); c.Activo = dto.Activo;
        c.SoloBoletas = dto.SoloBoletas;
        if (!string.IsNullOrWhiteSpace(dto.SolClaveNueva)) c.SolClaveCifrada = _secretos.Proteger(dto.SolClaveNueva);
        if (!string.IsNullOrWhiteSpace(dto.ApiSunatTokenNuevo)) c.ApiSunatTokenCifrado = _secretos.Proteger(dto.ApiSunatTokenNuevo);

        if (!string.IsNullOrWhiteSpace(dto.CertificadoPfxBase64))
        {
            byte[] pfx;
            try { pfx = Convert.FromBase64String(dto.CertificadoPfxBase64); }
            catch (FormatException) { return Bad("El certificado no es un base64 valido."); }
            var password = dto.CertificadoPasswordNueva;
            if (string.IsNullOrWhiteSpace(password) && !string.IsNullOrEmpty(c.CertificadoPasswordCifrada))
                password = _secretos.Desproteger(c.CertificadoPasswordCifrada);
            if (string.IsNullOrWhiteSpace(password)) return Bad("Indica la contrasena del nuevo certificado.");
            try
            {
                using var cert = X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.EphemeralKeySet);
                if (!cert.HasPrivateKey) return Bad("El certificado no contiene una clave privada.");
                if (DateTime.Now < cert.NotBefore || DateTime.Now > cert.NotAfter) return Bad("El certificado esta vencido o aun no es valido.");
            }
            catch (CryptographicException) { return Bad("El certificado no se pudo abrir con la contrasena indicada."); }
            c.CertificadoPfx = pfx;
        }
        if (!string.IsNullOrWhiteSpace(dto.CertificadoPasswordNueva))
            c.CertificadoPasswordCifrada = _secretos.Proteger(dto.CertificadoPasswordNueva);

        if (dto.Activo)
        {
            if (string.IsNullOrWhiteSpace(c.RazonSocial) || string.IsNullOrWhiteSpace(c.RucEmisor)
                || string.IsNullOrWhiteSpace(c.DireccionFiscal) || string.IsNullOrWhiteSpace(c.Ubigeo))
                return Bad("Completa razon social, RUC, direccion fiscal y ubigeo antes de activar.");
            if (proveedor.Codigo == "APISUNAT" && (string.IsNullOrWhiteSpace(c.ApiSunatPersonaId) || string.IsNullOrWhiteSpace(c.ApiSunatTokenCifrado)))
                return Bad("Completa personaId y token de APISUNAT para este negocio.");
            if (proveedor.Codigo == "APISUNAT")
            {
                var prueba = ValidarAmbiente(await proveedor.ProbarConexionAsync(Credenciales(c), "01", serieF, ct), ambiente);
                if (!prueba.Exitoso) return Bad(prueba.Mensaje);
            }
            if (proveedor.RequiereCertificadoLocal && (string.IsNullOrWhiteSpace(c.SolUsuario)
                || string.IsNullOrWhiteSpace(c.SolClaveCifrada) || c.CertificadoPfx is not { Length: > 0 }
                || string.IsNullOrWhiteSpace(c.CertificadoPasswordCifrada)))
                return Bad("Completa credenciales SOL y certificado digital antes de activar.");
        }
        await _repo.GuardarConfigAsync(c, ct);
        return NoContent();
    }

    /// <summary>Estado ligero para el punto de venta: si la facturación está activa y si el
    /// negocio es "solo boletas" (RUS/NRUS). Lo consume el detalle del pedido para mostrar u
    /// ocultar el botón de Factura. Accesible a quien puede emitir (módulo PEDIDOS).</summary>
    [HttpGet("facturacion/estado")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<object>> EstadoFacturacion(CancellationToken ct)
    {
        var c = await _repo.ObtenerConfigAsync(NegocioId, ct);
        return Ok(new { activa = c?.Activo ?? false, soloBoletas = c?.SoloBoletas ?? false });
    }

    [HttpPost("pedidos/{pedidoId:int}/comprobante")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<ComprobanteDto>> Emitir(int pedidoId, [FromBody] EmitirComprobanteRequest req, CancellationToken ct)
    {
        var tipo = req.Tipo?.Trim().ToUpperInvariant();
        if (tipo is not ("BOLETA" or "FACTURA")) return Bad("Tipo de comprobante invalido.");
        var pedido = await _pedidos.ObtenerPorIdAsync(pedidoId, SedeRequeridaId, ct);
        if (pedido is null) return NotFound();
        if (pedido.Anulado) return Bad("No se puede emitir para un pedido anulado.");
        if (pedido.EstadoPago != "PAGADO") return Bad("El pedido debe estar pagado por completo.");
        var config = await _repo.ObtenerConfigAsync(NegocioId, ct);
        if (config is null || !config.Activo) return Bad("Configura y activa la facturacion electronica primero.");
        if (tipo == "FACTURA" && config.SoloBoletas)
            return Bad("Este negocio está en régimen RUS/NRUS: solo puede emitir Boletas. La Factura no está disponible.");
        var provider = Seleccionar(config.Proveedor);
        var cliente = await _clientes.ObtenerPorIdAsync(pedido.ClienteId, NegocioId, ct);
        if (cliente is null) return Bad("El cliente asociado ya no existe.");

        string tipoDoc; string? documento; string nombre;
        if (tipo == "FACTURA")
        {
            documento = SoloDigitos(cliente.DocumentoFiscal);
            if (!DocumentoFiscalValidator.EsRucValido(documento)) return Bad("Para emitir factura el cliente necesita un RUC valido ante SUNAT.");
            tipoDoc = "RUC"; nombre = cliente.Nombre.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(cliente.Dni))
        {
            documento = SoloDigitos(cliente.Dni);
            if (documento?.Length != 8) return Bad("El DNI del cliente no es valido.");
            tipoDoc = "DNI"; nombre = pedido.ClienteNombre ?? cliente.Nombre;
        }
        else
        {
            if (pedido.Total >= 700m) return Bad("Las boletas desde S/ 700 requieren DNI del cliente.");
            tipoDoc = "SIN_DOC"; documento = null; nombre = pedido.ClienteNombre ?? cliente.Nombre;
        }

        var negocio = await _negocio.ObtenerAsync(NegocioId, ct);
        var igvPct = negocio?.Igv ?? 18m;
        var factor = 1 + igvPct / 100m;
        var opGravada = Math.Round((pedido.Total - pedido.Redondeo) / factor, 2, MidpointRounding.AwayFromZero);
        var igv = pedido.Total - pedido.Redondeo - opGravada;
        var detalles = pedido.Items.Select((item, i) =>
        {
            var valor = Math.Round(item.Total / factor, 2, MidpointRounding.AwayFromZero);
            return new ComprobanteElectronicoDetalle
            {
                NumeroLinea = i + 1, ServicioId = item.ServicioId, Descripcion = item.ServicioNombre ?? item.Descripcion ?? "Servicio",
                UnidadMedida = "ZZ", Cantidad = item.Cantidad, PrecioUnitarioIgv = item.PrecioUnit,
                ValorVenta = valor, Igv = item.Total - valor, Total = item.Total
            };
        }).ToList();
        if (detalles.Count == 0) return Bad("El pedido no tiene servicios para facturar.");

        var c = new ComprobanteElectronico
        {
            NegocioId = NegocioId, SedeId = SedeRequeridaId, PedidoId = pedido.Id, Tipo = tipo,
            Serie = tipo == "FACTURA" ? config.SerieFactura : config.SerieBoleta,
            ClienteNombre = nombre, ClienteTipoDoc = tipoDoc, ClienteNumDoc = documento,
            OpGravada = opGravada, Igv = igv, Total = pedido.Total, Subtotal = pedido.Subtotal,
            Descuento = pedido.Descuento, Recargo = pedido.RecargoUrgente, Redondeo = pedido.Redondeo,
            Estado = "PENDIENTE", FechaEmision = DateTime.Now, UsuarioId = UsuarioId,
            Proveedor = provider.Codigo, Ambiente = config.Ambiente, RucEmisor = config.RucEmisor,
            RazonSocialEmisor = config.RazonSocial, DireccionFiscalEmisor = config.DireccionFiscal,
            UbigeoEmisor = config.Ubigeo, CodigoEstablecimientoEmisor = config.CodigoEstablecimiento,
            IgvPorcentaje = igvPct, Detalles = detalles
        };
        var reservado = await _repo.CrearPendienteAtomicoAsync(c, detalles, ct);
        if (!reservado.Creado)
            return Conflict(new { mensaje = $"El pedido ya tiene {reservado.Comprobante.Serie}-{reservado.Comprobante.Correlativo:D8}." });

        var resultado = await EjecutarSeguro(() => provider.EmitirAsync(
            new SolicitudEmision(c, [], Credenciales(config), ConfigSnapshot(c)), ct), c.Id, "EMITIR", ct);
        await _repo.ActualizarResultadoCompletoAsync(c.Id, c.SedeId, resultado.Estado, resultado.Codigo,
            resultado.Descripcion, resultado.XmlFirmado, resultado.CdrZip, resultado.HashCpe,
            resultado.ExternalId, DateTime.Now, resultado.FechaRespuesta, ct);
        await _respaldo.RespaldarAsync(c.Id, c.SedeId, NegocioId, ct);
        var final = await CargarCompleto(c.Id, ct);
        return resultado.Estado == "PENDIENTE" ? Accepted(Map(final!)) : Ok(Map(final!));
    }

    [HttpGet("facturacion/comprobantes")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<PagedResultDto<ComprobanteDto>>> Listar([FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 15, [FromQuery] string? tipo = null, [FromQuery] string? estado = null,
        [FromQuery] string? busqueda = null, [FromQuery] DateTime? desde = null, [FromQuery] DateTime? hasta = null,
        CancellationToken ct = default)
    {
        pagina = Math.Max(1, pagina); tamanoPagina = Math.Clamp(tamanoPagina, 1, 200);
        tipo = Limpiar(tipo)?.ToUpperInvariant(); estado = Limpiar(estado)?.ToUpperInvariant();
        if (tipo is not null and not ("BOLETA" or "FACTURA" or "NOTA_CREDITO" or "NOTA_DEBITO" or "GUIA_REMISION")) return Bad("Filtro de tipo invalido.");
        var estados = new[] { "PENDIENTE", "ACEPTADO", "RECHAZADO", "ANULADO", "ERROR", "SIMULADO" };
        if (estado is not null && !estados.Contains(estado)) return Bad("Filtro de estado invalido.");
        var (items, total) = await _repo.ListarFiltradoAsync(SedeRequeridaId, tipo, estado, busqueda, desde, hasta, pagina, tamanoPagina, ct);
        return Ok(new PagedResultDto<ComprobanteDto> { Items = items.Select(Map).ToList(), Total = total, Pagina = pagina, TamanoPagina = tamanoPagina });
    }

    [HttpGet("facturacion/kpi")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<List<KpiComprobantesMesDto>>> Kpi([FromQuery] int meses = 6, CancellationToken ct = default)
        => Ok(await _repo.KpiMensualAsync(SedeRequeridaId, meses, ct));

    /// <summary>Carpeta local donde se respaldan los comprobantes (XML+CDR+PDF). Para mostrarla en Ajustes.</summary>
    [HttpGet("facturacion/respaldo")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public ActionResult<RespaldoInfoDto> RespaldoInfo() => Ok(new RespaldoInfoDto(_respaldo.CarpetaRaiz));

    /// <summary>Respalda a disco todos los comprobantes ya aceptados (por si se activó el respaldo después de emitir).</summary>
    [HttpPost("facturacion/respaldo/generar-todos")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<RespaldoResultadoDto>> RespaldarTodos(CancellationToken ct)
        => Ok(new RespaldoResultadoDto(await _respaldo.RespaldarTodosAsync(NegocioId, ct), _respaldo.CarpetaRaiz));

    [HttpGet("facturacion/comprobantes/{id:int}")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<ComprobanteDto>> Obtener(int id, CancellationToken ct)
    {
        var c = await CargarCompleto(id, ct); return c is null ? NotFound() : Ok(Map(c));
    }

    [HttpPost("facturacion/comprobantes/{id:int}/sincronizar")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<ActionResult<ComprobanteDto>> Sincronizar(int id, CancellationToken ct)
    {
        var c = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (c is null) return NotFound();
        if (c.EsSimulado) return Bad("Los datos simulados no se sincronizan.");
        if (c.Estado is not ("PENDIENTE" or "ERROR")) return Bad("Solo se sincronizan comprobantes pendientes o con error.");
        var config = await _repo.ObtenerConfigAsync(NegocioId, ct) ?? throw new InvalidOperationException("Configuracion no encontrada.");
        var provider = Seleccionar(c.Proveedor);
        var r = await provider.ConsultarAsync(c, Credenciales(config), ct);
        await _repo.RegistrarIntentoAsync(c.Id, "CONSULTAR", r.Estado, r.Codigo, r.Descripcion, UsuarioId, ct);
        if (r.Exitoso) await _repo.ActualizarResultadoCompletoAsync(c.Id, c.SedeId, r.Estado, r.Codigo, r.Descripcion,
            r.XmlFirmado, r.CdrZip, r.HashCpe, c.ExternalId, null, r.FechaRespuesta, ct);
        await _respaldo.RespaldarAsync(id, SedeRequeridaId, NegocioId, ct);
        return Ok(Map((await CargarCompleto(id, ct))!));
    }

    [HttpPost("facturacion/comprobantes/{id:int}/reenviar")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<ComprobanteDto>> Reenviar(int id, CancellationToken ct)
    {
        var c = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (c is null) return NotFound();
        if (c.EsSimulado || c.Estado != "ERROR") return Bad("Solo se reenvian comprobantes con error recuperable. Un comprobante rechazado requiere nueva numeracion.");
        var config = await _repo.ObtenerConfigAsync(NegocioId, ct) ?? throw new InvalidOperationException("Configuracion no encontrada.");
        var provider = Seleccionar(c.Proveedor);
        var r = await EjecutarSeguro(() => provider.EmitirAsync(new SolicitudEmision(c, [], Credenciales(config), ConfigSnapshot(c)), ct), c.Id, "REENVIAR", ct);
        await _repo.ActualizarResultadoCompletoAsync(c.Id, c.SedeId, r.Estado, r.Codigo, r.Descripcion,
            r.XmlFirmado, r.CdrZip, r.HashCpe, r.ExternalId, DateTime.Now, r.FechaRespuesta, ct);
        await _respaldo.RespaldarAsync(id, SedeRequeridaId, NegocioId, ct);
        return Ok(Map((await CargarCompleto(id, ct))!));
    }

    [HttpPost("facturacion/comprobantes/{id:int}/anular")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<ComprobanteDto>> Anular(int id, [FromBody] AnularComprobanteRequest req, CancellationToken ct)
    {
        var c = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (c is null) return NotFound();
        if (c.EsSimulado || c.Estado != "ACEPTADO") return Bad("Solo se puede anular un comprobante real aceptado.");
        if (c.EstadoAnulacion is "PENDIENTE" or "ANULADO") return Bad("El comprobante ya tiene una anulacion en curso o aceptada.");
        var motivo = req.Motivo.Trim();
        var config = await _repo.ObtenerConfigAsync(NegocioId, ct) ?? throw new InvalidOperationException("Configuracion no encontrada.");
        var r = await Seleccionar(c.Proveedor).AnularAsync(c, Credenciales(config), motivo, ct);
        await _repo.RegistrarIntentoAsync(c.Id, "ANULAR", r.Estado, r.Codigo, r.Descripcion, UsuarioId, ct);
        await _repo.ActualizarAnulacionAsync(c.Id, c.SedeId, r.Estado,
            r.Estado == "ANULADO" ? "ANULADO" : null, motivo, r.Codigo, r.Descripcion, r.FechaRespuesta, ct);
        return Ok(Map((await CargarCompleto(id, ct))!));
    }

    [HttpPost("facturacion/comprobantes/{id:int}/nota-credito")]
    [Authorize(Roles = "ADMIN")]
    public Task<ActionResult<ComprobanteDto>> EmitirNotaCredito(int id, [FromBody] NotaCreditoRequest req, CancellationToken ct)
        // Catálogo 09 de SUNAT (motivos de nota de crédito).
        => EmitirNota(id, "NOTA_CREDITO", new[] { "01", "02", "03", "04", "05", "06", "07", "09", "10", "11", "13" },
            req.MotivoCodigo, req.Motivo, "nota de crédito", "catálogo 09", ct);

    [HttpPost("facturacion/comprobantes/{id:int}/nota-debito")]
    [Authorize(Roles = "ADMIN")]
    public Task<ActionResult<ComprobanteDto>> EmitirNotaDebito(int id, [FromBody] NotaDebitoRequest req, CancellationToken ct)
        // Catálogo 10 de SUNAT (motivos de nota de débito).
        => EmitirNota(id, "NOTA_DEBITO", new[] { "01", "02", "03" },
            req.MotivoCodigo, req.Motivo, "nota de débito", "catálogo 10", ct);

    /// <summary>
    /// Emite una nota de crédito (07) o débito (08) sobre una boleta/factura aceptada. Ambas comparten
    /// estructura: referencian el documento original, llevan un motivo de catálogo y repiten sus líneas.
    /// </summary>
    private async Task<ActionResult<ComprobanteDto>> EmitirNota(int id, string tipoNota, string[] motivosValidos,
        string motivoCodigoRaw, string motivoRaw, string etiqueta, string catalogo, CancellationToken ct)
    {
        var esDebito = tipoNota == "NOTA_DEBITO";
        var original = await CargarCompleto(id, ct);
        if (original is null) return NotFound();
        if (original.Tipo is not ("BOLETA" or "FACTURA")) return Bad($"Solo se emite una {etiqueta} sobre una boleta o factura.");
        if (original.Estado != "ACEPTADO") return Bad($"Solo se emite {etiqueta} sobre un comprobante aceptado por SUNAT.");
        if (original.EstadoAnulacion is "PENDIENTE" or "ANULADO") return Bad("El comprobante ya está anulado o con una anulación en curso.");

        var motivoCodigo = (motivoCodigoRaw ?? "").Trim();
        if (!motivosValidos.Contains(motivoCodigo)) return Bad($"El motivo de la {etiqueta} no es válido ({catalogo} de SUNAT).");
        var motivo = (motivoRaw ?? "").Trim();
        if (motivo.Length < 3) return Bad($"Indica el motivo de la {etiqueta}.");

        var config = await _repo.ObtenerConfigAsync(NegocioId, ct);
        if (config is null || !config.Activo) return Bad("Configura y activa la facturación electrónica primero.");
        var provider = Seleccionar(config.Proveedor);

        var esFactura = original.Tipo == "FACTURA";
        var serie = esDebito
            ? (esFactura ? config.SerieNotaDebitoFactura : config.SerieNotaDebitoBoleta)
            : (esFactura ? config.SerieNotaCreditoFactura : config.SerieNotaCreditoBoleta);

        var nota = new ComprobanteElectronico
        {
            NegocioId = original.NegocioId, SedeId = original.SedeId, PedidoId = original.PedidoId, Tipo = tipoNota,
            Serie = serie,
            ClienteNombre = original.ClienteNombre, ClienteTipoDoc = original.ClienteTipoDoc, ClienteNumDoc = original.ClienteNumDoc,
            OpGravada = original.OpGravada, Igv = original.Igv, Total = original.Total, Subtotal = original.Subtotal,
            Descuento = original.Descuento, Recargo = original.Recargo, Redondeo = original.Redondeo,
            Estado = "PENDIENTE", FechaEmision = DateTime.Now, UsuarioId = UsuarioId,
            Proveedor = provider.Codigo, Ambiente = config.Ambiente, RucEmisor = original.RucEmisor,
            RazonSocialEmisor = original.RazonSocialEmisor, DireccionFiscalEmisor = original.DireccionFiscalEmisor,
            UbigeoEmisor = original.UbigeoEmisor, CodigoEstablecimientoEmisor = original.CodigoEstablecimientoEmisor,
            IgvPorcentaje = original.IgvPorcentaje, EsSimulado = original.EsSimulado,
            ComprobanteRefId = original.Id, DocRefTipo = esFactura ? "01" : "03",
            DocRefSerieNumero = $"{original.Serie}-{original.Correlativo:D8}",
            MotivoNotaCodigo = motivoCodigo, MotivoNotaDescripcion = motivo,
            Detalles = original.Detalles.Select((d, i) => new ComprobanteElectronicoDetalle
            {
                NumeroLinea = i + 1, ServicioId = d.ServicioId, Descripcion = d.Descripcion, UnidadMedida = d.UnidadMedida,
                Cantidad = d.Cantidad, PrecioUnitarioIgv = d.PrecioUnitarioIgv, ValorVenta = d.ValorVenta, Igv = d.Igv, Total = d.Total
            }).ToList()
        };
        if (nota.Detalles.Count == 0) return Bad("El comprobante no tiene líneas de referencia.");

        var creada = await _repo.CrearNotaAtomicaAsync(nota, nota.Detalles, ct);

        // Si el comprobante original era de prueba, la nota también lo es (no se envía a SUNAT).
        if (creada.EsSimulado)
        {
            await _repo.ActualizarResultadoCompletoAsync(creada.Id, creada.SedeId, "SIMULADO", "0",
                $"Nota simulada (documento de prueba).", null, null, null, null, DateTime.Now, DateTime.Now, ct);
            return Ok(Map((await CargarCompleto(creada.Id, ct))!));
        }

        var resultado = await EjecutarSeguro(() => provider.EmitirAsync(
            new SolicitudEmision(creada, [], Credenciales(config), ConfigSnapshot(creada)), ct), creada.Id, "EMITIR", ct);
        await _repo.ActualizarResultadoCompletoAsync(creada.Id, creada.SedeId, resultado.Estado, resultado.Codigo,
            resultado.Descripcion, resultado.XmlFirmado, resultado.CdrZip, resultado.HashCpe,
            resultado.ExternalId, DateTime.Now, resultado.FechaRespuesta, ct);
        await _respaldo.RespaldarAsync(creada.Id, creada.SedeId, NegocioId, ct);
        var final = await CargarCompleto(creada.Id, ct);
        return resultado.Estado == "PENDIENTE" ? Accepted(Map(final!)) : Ok(Map(final!));
    }

    [HttpPost("facturacion/comprobantes/{id:int}/guia-remision")]
    [Authorize(Roles = "ADMIN")]
    public async Task<ActionResult<ComprobanteDto>> EmitirGuiaRemision(int id, [FromBody] GuiaRemisionRequest req, CancellationToken ct)
    {
        var origen = await CargarCompleto(id, ct);
        if (origen is null) return NotFound();
        if (origen.Tipo is not ("BOLETA" or "FACTURA")) return Bad("La guía de remisión se emite desde una boleta o factura.");
        if (origen.Detalles.Count == 0) return Bad("El comprobante no tiene bienes para trasladar.");

        // Catálogo 20 de SUNAT (motivo de traslado) — subconjunto habitual para una lavandería.
        var motivosValidos = new[] { "01", "02", "04", "08", "09", "13", "14", "18", "19" };
        var motivo = (req.MotivoCodigo ?? "").Trim();
        if (!motivosValidos.Contains(motivo)) return Bad("El motivo de traslado no es válido (catálogo 20 de SUNAT).");
        if (motivo == "13" && string.IsNullOrWhiteSpace(req.MotivoDescripcion)) return Bad("Indica la descripción del motivo de traslado.");

        var modalidad = (req.ModalidadTransporte ?? "").Trim();
        if (modalidad is not ("01" or "02")) return Bad("La modalidad de transporte debe ser pública (01) o privada (02).");
        if (string.IsNullOrWhiteSpace(req.PartidaUbigeo) || string.IsNullOrWhiteSpace(req.PartidaDireccion)
            || string.IsNullOrWhiteSpace(req.LlegadaUbigeo) || string.IsNullOrWhiteSpace(req.LlegadaDireccion))
            return Bad("Indica el ubigeo y dirección de partida y de llegada.");
        if (modalidad == "01" && (string.IsNullOrWhiteSpace(req.TransportistaNumDoc) || string.IsNullOrWhiteSpace(req.TransportistaRazonSocial)))
            return Bad("El transporte público requiere el RUC y la razón social del transportista.");
        if (modalidad == "02" && (string.IsNullOrWhiteSpace(req.VehiculoPlaca) || string.IsNullOrWhiteSpace(req.ConductorNumDoc)
            || string.IsNullOrWhiteSpace(req.ConductorNombres) || string.IsNullOrWhiteSpace(req.ConductorLicencia)))
            return Bad("El transporte privado requiere placa del vehículo y documento, nombres y licencia del conductor.");

        var config = await _repo.ObtenerConfigAsync(NegocioId, ct);
        if (config is null || !config.Activo) return Bad("Configura y activa la facturación electrónica primero.");

        var guia = new GuiaRemisionDatos
        {
            MotivoTrasladoCodigo = motivo, MotivoTrasladoDescripcion = Limpiar(req.MotivoDescripcion),
            PesoBrutoTotal = req.PesoBrutoTotal, UnidadPeso = string.IsNullOrWhiteSpace(req.UnidadPeso) ? "KGM" : req.UnidadPeso!.Trim(),
            NumeroBultos = req.NumeroBultos, FechaInicioTraslado = req.FechaInicioTraslado.Date,
            ModalidadTransporte = modalidad,
            PartidaUbigeo = req.PartidaUbigeo.Trim(), PartidaDireccion = req.PartidaDireccion.Trim(),
            LlegadaUbigeo = req.LlegadaUbigeo.Trim(), LlegadaDireccion = req.LlegadaDireccion.Trim(),
            TransportistaNumDoc = Limpiar(req.TransportistaNumDoc), TransportistaRazonSocial = Limpiar(req.TransportistaRazonSocial),
            VehiculoPlaca = Limpiar(req.VehiculoPlaca), ConductorTipoDoc = string.IsNullOrWhiteSpace(req.ConductorTipoDoc) ? "1" : req.ConductorTipoDoc!.Trim(),
            ConductorNumDoc = Limpiar(req.ConductorNumDoc), ConductorNombres = Limpiar(req.ConductorNombres), ConductorLicencia = Limpiar(req.ConductorLicencia)
        };

        var g = new ComprobanteElectronico
        {
            NegocioId = origen.NegocioId, SedeId = origen.SedeId, PedidoId = origen.PedidoId, Tipo = "GUIA_REMISION",
            Serie = config.SerieGuiaRemision,
            ClienteNombre = origen.ClienteNombre, ClienteTipoDoc = origen.ClienteTipoDoc, ClienteNumDoc = origen.ClienteNumDoc,
            Estado = "PENDIENTE", FechaEmision = DateTime.Now, UsuarioId = UsuarioId,
            Proveedor = Seleccionar(config.Proveedor).Codigo, Ambiente = config.Ambiente, RucEmisor = origen.RucEmisor,
            RazonSocialEmisor = origen.RazonSocialEmisor, DireccionFiscalEmisor = origen.DireccionFiscalEmisor,
            UbigeoEmisor = origen.UbigeoEmisor, CodigoEstablecimientoEmisor = origen.CodigoEstablecimientoEmisor,
            IgvPorcentaje = origen.IgvPorcentaje, EsSimulado = origen.EsSimulado, Guia = guia,
            Detalles = origen.Detalles.Select((d, i) => new ComprobanteElectronicoDetalle
            {
                NumeroLinea = i + 1, ServicioId = d.ServicioId, Descripcion = d.Descripcion,
                UnidadMedida = string.IsNullOrWhiteSpace(d.UnidadMedida) || d.UnidadMedida == "ZZ" ? "NIU" : d.UnidadMedida,
                Cantidad = d.Cantidad
            }).ToList()
        };

        var creada = await _repo.CrearGuiaAtomicaAsync(g, g.Detalles, guia, ct);

        // El XML DespatchAdvice se genera y se guarda como referencia descargable. El envío real a SUNAT
        // usa el canal GRE (endpoint/credenciales aparte del sendBill), que se habilita en producción.
        byte[]? xml = null;
        try { xml = System.Text.Encoding.UTF8.GetBytes(UblXmlBuilder.ConstruirGuiaRemision(creada, config, creada.Detalles).ToString()); }
        catch { /* si el UBL falla, la guía queda registrada sin XML */ }

        await _repo.ActualizarResultadoCompletoAsync(creada.Id, creada.SedeId, "SIMULADO", "0",
            "Guía registrada. El envío a SUNAT (canal GRE) se habilita con las credenciales de producción.",
            xml, null, null, null, DateTime.Now, DateTime.Now, ct);
        return Ok(Map((await CargarCompleto(creada.Id, ct))!));
    }

    [HttpGet("facturacion/comprobantes/{id:int}/pdf")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> Pdf(int id, CancellationToken ct)
    {
        var c = await CargarCompleto(id, ct); if (c is null) return NotFound();
        var negocio = await _negocio.ObtenerAsync(NegocioId, ct); if (negocio is null) return NotFound();
        return File(_pdf.Generar(c, negocio), "application/pdf", Nombre(c, "pdf"));
    }

    [HttpGet("facturacion/comprobantes/{id:int}/xml")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> Xml(int id, CancellationToken ct) => await Archivo(id, "xml", "application/xml", c => c.XmlFirmado, ct);

    [HttpGet("facturacion/comprobantes/{id:int}/cdr")]
    [Authorize(Policy = "Modulo:PEDIDOS")]
    public async Task<IActionResult> Cdr(int id, CancellationToken ct) => await Archivo(id, "zip", "application/zip", c => c.CdrZip, ct);

    private async Task<IActionResult> Archivo(int id, string extension, string contentType,
        Func<ComprobanteElectronico, byte[]?> selector, CancellationToken ct)
    {
        var c = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct); if (c is null) return NotFound();
        var bytes = selector(c); return bytes is null ? NotFound(new { mensaje = "El archivo aun no esta disponible." }) : File(bytes, contentType, Nombre(c, extension));
    }

    private async Task<ResultadoEmision> EjecutarSeguro(Func<Task<ResultadoEmision>> accion, int id, string tipo, CancellationToken ct)
    {
        ResultadoEmision r;
        try { r = await accion(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _log.LogError(ex, "Fallo {Accion} del comprobante {ComprobanteId}", tipo, id);
            r = new(false, "ERROR", "EXCEPCION", "No se pudo comunicar con el proveedor. Sincroniza o reintenta.", null, null);
        }
        await _repo.RegistrarIntentoAsync(id, tipo, r.Estado, r.Codigo, r.Descripcion, UsuarioId, ct);
        return r;
    }

    private IFacturacionElectronicaProvider Seleccionar(string? codigo) =>
        _providers.TryGetValue((codigo ?? "").Trim(), out var p) ? p : throw new InvalidOperationException("Proveedor de facturacion no soportado.");

    private CredencialesEmisor Credenciales(ConfiguracionFacturacion c) => new(c.Ambiente, c.RucEmisor ?? "", c.RazonSocial ?? "",
        c.SolUsuario ?? "", Descifrar(c.SolClaveCifrada), c.CertificadoPfx ?? [], Descifrar(c.CertificadoPasswordCifrada),
        c.ApiSunatPersonaId ?? "", Descifrar(c.ApiSunatTokenCifrado));

    private string Descifrar(string? valor) => string.IsNullOrEmpty(valor) ? "" : _secretos.Desproteger(valor);

    private static ResultadoConexion ValidarAmbiente(ResultadoConexion resultado, string ambiente)
    {
        if (!resultado.Exitoso || resultado.Produccion is null) return resultado;
        var esperaProduccion = ambiente == "PRODUCCION";
        if (resultado.Produccion == esperaProduccion) return resultado;
        var ambienteToken = resultado.Produccion.Value ? "PRODUCCION" : "DESARROLLO";
        return resultado with { Exitoso = false, Mensaje = $"El token pertenece al ambiente {ambienteToken} y no coincide con {ambiente}." };
    }

    private static ConfiguracionFacturacion ConfigSnapshot(ComprobanteElectronico c) => new()
    {
        RucEmisor = c.RucEmisor, RazonSocial = c.RazonSocialEmisor, DireccionFiscal = c.DireccionFiscalEmisor,
        Ubigeo = c.UbigeoEmisor, CodigoEstablecimiento = c.CodigoEstablecimientoEmisor ?? "0000", Ambiente = c.Ambiente
    };

    private async Task<ComprobanteElectronico?> CargarCompleto(int id, CancellationToken ct)
    {
        var c = await _repo.ObtenerPorIdAsync(id, SedeRequeridaId, ct);
        if (c is not null) c.Detalles = await _repo.ListarDetallesAsync(c.Id, ct);
        if (c is not null) c.Intentos = await _repo.ListarIntentosAsync(c.Id, ct);
        if (c is { Tipo: "GUIA_REMISION" }) c.Guia = await _repo.ObtenerGuiaDatosAsync(c.Id, ct);
        return c;
    }

    private static ComprobanteDto Map(ComprobanteElectronico c) => new()
    {
        Id = c.Id, PedidoId = c.PedidoId, PedidoNumero = c.PedidoNumero, Tipo = c.Tipo, Serie = c.Serie,
        Correlativo = c.Correlativo, ClienteNombre = c.ClienteNombre, ClienteTipoDoc = c.ClienteTipoDoc,
        ClienteNumDoc = c.ClienteNumDoc, OpGravada = c.OpGravada, Igv = c.Igv, Total = c.Total,
        Estado = c.Estado, CodigoRespuestaSunat = c.CodigoRespuestaSunat,
        DescripcionRespuestaSunat = c.DescripcionRespuestaSunat, FechaEmision = c.FechaEmision,
        Proveedor = c.Proveedor, Ambiente = c.Ambiente, ExternalId = c.ExternalId, FechaEnvio = c.FechaEnvio,
        FechaRespuesta = c.FechaRespuesta, EsSimulado = c.EsSimulado,
        TieneXml = c.XmlFirmado is { Length: > 0 }, TieneCdr = c.CdrZip is { Length: > 0 },
        EstadoAnulacion = c.EstadoAnulacion,
        MotivoAnulacion = c.MotivoAnulacion, FechaAnulacion = c.FechaAnulacion,
        DocRefSerieNumero = c.DocRefSerieNumero, MotivoNotaCodigo = c.MotivoNotaCodigo,
        MotivoNotaDescripcion = c.MotivoNotaDescripcion,
        Guia = c.Guia is null ? null : new GuiaRemisionDto(
            c.Guia.MotivoTrasladoCodigo, c.Guia.MotivoTrasladoDescripcion, c.Guia.PesoBrutoTotal, c.Guia.UnidadPeso,
            c.Guia.NumeroBultos, c.Guia.FechaInicioTraslado, c.Guia.ModalidadTransporte,
            c.Guia.PartidaUbigeo, c.Guia.PartidaDireccion, c.Guia.LlegadaUbigeo, c.Guia.LlegadaDireccion,
            c.Guia.TransportistaNumDoc, c.Guia.TransportistaRazonSocial, c.Guia.VehiculoPlaca,
            c.Guia.ConductorTipoDoc, c.Guia.ConductorNumDoc, c.Guia.ConductorNombres, c.Guia.ConductorLicencia),
        Detalles = c.Detalles.Select(x => new ComprobanteDetalleDto(x.NumeroLinea, x.Descripcion, x.UnidadMedida,
            x.Cantidad, x.PrecioUnitarioIgv, x.ValorVenta, x.Igv, x.Total)).ToList(),
        Intentos = c.Intentos.Select(x => new ComprobanteIntentoDto(x.Id, x.Accion, x.Estado, x.Codigo,
            x.Descripcion, x.Fecha, x.UsuarioId)).ToList()
    };

    private static string CodigoTipoSunat(string tipo) => tipo switch { "FACTURA" => "01", "NOTA_CREDITO" => "07", "NOTA_DEBITO" => "08", "GUIA_REMISION" => "09", _ => "03" };
    private static string Nombre(ComprobanteElectronico c, string ext) => $"{c.RucEmisor}-{CodigoTipoSunat(c.Tipo)}-{c.Serie}-{c.Correlativo:D8}.{ext}";
    private static string? Limpiar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string? SoloDigitos(string? s) => s is null ? null : new string(s.Where(char.IsDigit).ToArray());
    private BadRequestObjectResult Bad(string mensaje) => BadRequest(new { mensaje });
}
