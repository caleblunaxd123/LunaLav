using Lavanderia.Api.Dtos;
using Lavanderia.Api.Infrastructure;
using Lavanderia.Api.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Lavanderia.Api.Controllers;

[Route("api/reportes")]
[Microsoft.AspNetCore.Authorization.Authorize(Policy = "Modulo:REPORTES")]
public class ReportesController : TenantAwareControllerBase
{
    private readonly IReporteRepository _repo;
    private readonly IGerencialRepository _gerencial;
    private readonly IConfiguracionNegocioRepository _config;
    public ReportesController(IReporteRepository repo, IGerencialRepository gerencial, IConfiguracionNegocioRepository config)
    {
        _repo = repo;
        _gerencial = gerencial;
        _config = config;
    }

    [HttpGet("sla")]
    public async Task<ActionResult<TableroSlaDto>> Sla([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _gerencial.ObtenerTableroSlaAsync(SedeRequeridaId, d, h, ct));
    }

    [HttpGet("vista-gerencial")]
    public async Task<ActionResult<VistaGerencialDto>> VistaGerencial(CancellationToken ct)
        => Ok(await _gerencial.ObtenerVistaGerencialAsync(NegocioId, SedeRequeridaId, ct));

    [HttpGet("consolidado")]
    public async Task<ActionResult<List<ConsolidadoSedeDto>>> Consolidado(CancellationToken ct)
        => Ok(await _gerencial.ObtenerConsolidadoAsync(NegocioId, ct));

    private static (DateTime desde, DateTime hasta) Rango(DateTime? desde, DateTime? hasta)
    {
        var h = hasta ?? DateTime.Today;
        var d = desde ?? h.AddDays(-30);
        return (d.Date, h.Date);
    }

    [HttpGet("ordenes-pendientes")]
    public async Task<ActionResult<ReporteResultDto>> OrdenesPendientes(CancellationToken ct)
        => Ok(await _repo.OrdenesPendientesAsync(SedeRequeridaId, ct));

    [HttpGet("gastos")]
    public async Task<ActionResult<ReporteResultDto>> Gastos([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.GastosAsync(d, h, SedeRequeridaId, ct));
    }

    [HttpGet("general")]
    public async Task<ActionResult<ReporteResultDto>> General([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.GeneralAsync(d, h, SedeRequeridaId, ct));
    }

    [HttpGet("servicios")]
    public async Task<ActionResult<ReporteResultDto>> Servicios([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.ServiciosAsync(d, h, SedeRequeridaId, ct));
    }

    [HttpGet("cuadres-caja")]
    public async Task<ActionResult<ReporteResultDto>> CuadresCaja([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.CuadresCajaAsync(d, h, SedeRequeridaId, ct));
    }

    [HttpGet("ordenes-mensual")]
    public async Task<ActionResult<ReporteResultDto>> OrdenesMensual([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.OrdenesMensualAsync(d, h, SedeRequeridaId, ct));
    }

    [HttpGet("almacen")]
    public async Task<ActionResult<ReporteResultDto>> Almacen(CancellationToken ct)
        => Ok(await _repo.AlmacenAsync(SedeRequeridaId, ct));

    [HttpGet("anulados")]
    public async Task<ActionResult<ReporteResultDto>> Anulados([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.AnuladosAsync(d, h, SedeRequeridaId, ct));
    }

    [HttpGet("registro-entregas")]
    public async Task<ActionResult<ReporteResultDto>> RegistroEntregas([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.RegistroEntregasAsync(d, h, SedeRequeridaId, ct));
    }

    [HttpGet("pagos")]
    public async Task<ActionResult<ReporteResultDto>> Pagos([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.PagosAsync(d, h, SedeRequeridaId, ct));
    }

    [HttpGet("descuento-directo")]
    public async Task<ActionResult<ReporteResultDto>> DescuentoDirecto([FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        return Ok(await _repo.DescuentoDirectoAsync(d, h, SedeRequeridaId, ct));
    }

    private async Task<ReporteResultDto?> ObtenerPorKeyAsync(string key, DateTime d, DateTime h, CancellationToken ct) => key switch
    {
        "ordenes-pendientes" => await _repo.OrdenesPendientesAsync(SedeRequeridaId, ct),
        "gastos" => await _repo.GastosAsync(d, h, SedeRequeridaId, ct),
        "general" => await _repo.GeneralAsync(d, h, SedeRequeridaId, ct),
        "servicios" => await _repo.ServiciosAsync(d, h, SedeRequeridaId, ct),
        "cuadres-caja" => await _repo.CuadresCajaAsync(d, h, SedeRequeridaId, ct),
        "ordenes-mensual" => await _repo.OrdenesMensualAsync(d, h, SedeRequeridaId, ct),
        "almacen" => await _repo.AlmacenAsync(SedeRequeridaId, ct),
        "anulados" => await _repo.AnuladosAsync(d, h, SedeRequeridaId, ct),
        "registro-entregas" => await _repo.RegistroEntregasAsync(d, h, SedeRequeridaId, ct),
        "pagos" => await _repo.PagosAsync(d, h, SedeRequeridaId, ct),
        "descuento-directo" => await _repo.DescuentoDirectoAsync(d, h, SedeRequeridaId, ct),
        _ => null
    };

    /// <summary>Exporta cualquier reporte a un archivo Excel (.xlsx) real con encabezados.</summary>
    [HttpGet("export/{key}")]
    public async Task<IActionResult> Exportar(string key, [FromQuery] DateTime? desde, [FromQuery] DateTime? hasta, CancellationToken ct)
    {
        var (d, h) = Rango(desde, hasta);
        var rep = await ObtenerPorKeyAsync(key, d, h, ct);
        if (rep is null) return NotFound(new { mensaje = "Reporte desconocido." });

        var cfg = await _config.ObtenerAsync(NegocioId, ct);
        var negocioNombre = string.IsNullOrWhiteSpace(cfg?.NombreNegocio) ? "Reporte" : cfg!.NombreNegocio;
        var titulo = TituloReporte(key);
        var subtitulo = $"Período: {d:dd/MM/yyyy} al {h:dd/MM/yyyy}   ·   Generado el {DateTime.Now:dd/MM/yyyy HH:mm}";

        var bytes = ExcelReporte.Construir(negocioNombre, titulo, subtitulo, rep.Columnas, rep.Filas);
        var nombre = $"{titulo.Replace(' ', '-').ToLowerInvariant()}-{d:yyyyMMdd}-{h:yyyyMMdd}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", nombre);
    }

    private static string TituloReporte(string key) => key.ToLowerInvariant() switch
    {
        "general" => "Reporte General",
        "servicios" => "Reporte de Servicios",
        "gastos" => "Reporte de Gastos",
        "ordenes-pendientes" => "Órdenes Pendientes",
        "clientes" => "Reporte de Clientes",
        "pagos" => "Reporte de Pagos",
        "cuadres-caja" => "Cuadres de Caja",
        "cuadres-diarios" => "Cuadres Diarios",
        _ => "Reporte " + (key.Length > 0 ? char.ToUpper(key[0]) + key[1..].Replace('-', ' ') : "")
    };
}
