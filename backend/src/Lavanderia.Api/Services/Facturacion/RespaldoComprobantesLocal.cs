using Lavanderia.Api.Repositories;

namespace Lavanderia.Api.Services.Facturacion;

/// <summary>
/// Respaldo LOCAL obligatorio (requisito SUNAT): por cada comprobante ACEPTADO guarda en disco
/// el XML firmado, el CDR (constancia de SUNAT) y el PDF, para conservarlos como archivo del
/// emisor. La carpeta se puede apuntar a una que sincronice Google Drive/OneDrive para tener
/// además respaldo en la nube. Es idempotente: no reescribe archivos ya guardados.
///
/// Estructura:  {raiz}\{RUC}\{yyyy-MM}\{RUC}-{tipo}-{serie}-{correlativo}.(xml|pdf) + R-....zip (CDR)
/// </summary>
public interface IRespaldoComprobantes
{
    /// <summary>Respalda un comprobante (por id) si ya está ACEPTADO y tiene XML. No lanza: solo loguea.</summary>
    Task RespaldarAsync(int comprobanteId, int sedeId, int negocioId, CancellationToken ct = default);

    /// <summary>Respalda todos los comprobantes aceptados de un negocio (backfill). Devuelve cuántos procesó.</summary>
    Task<int> RespaldarTodosAsync(int negocioId, CancellationToken ct = default);

    /// <summary>Carpeta raíz donde se guardan los respaldos (para mostrarla en la UI).</summary>
    string CarpetaRaiz { get; }
}

public sealed class RespaldoComprobantesLocal : IRespaldoComprobantes
{
    private readonly IFacturacionRepository _repo;
    private readonly IConfiguracionNegocioRepository _negocioRepo;
    private readonly ComprobantePdfGenerator _pdf;
    private readonly ILogger<RespaldoComprobantesLocal> _log;

    public string CarpetaRaiz { get; }

    public RespaldoComprobantesLocal(IFacturacionRepository repo, IConfiguracionNegocioRepository negocioRepo,
        ComprobantePdfGenerator pdf, IConfiguration config, IWebHostEnvironment env,
        ILogger<RespaldoComprobantesLocal> log)
    {
        _repo = repo; _negocioRepo = negocioRepo; _pdf = pdf; _log = log;
        var configurada = config.GetValue<string>("Comprobantes:RespaldoDirectorio");
        CarpetaRaiz = !string.IsNullOrWhiteSpace(configurada)
            ? configurada
            : Path.Combine(env.ContentRootPath, "App_Data", "comprobantes-sunat");
    }

    public async Task RespaldarAsync(int comprobanteId, int sedeId, int negocioId, CancellationToken ct = default)
    {
        try
        {
            var c = await _repo.ObtenerPorIdAsync(comprobanteId, sedeId, ct);
            if (c is null || c.Estado != "ACEPTADO" || c.XmlFirmado is not { Length: > 0 }) return;

            var ruc = string.IsNullOrWhiteSpace(c.RucEmisor) ? "SIN-RUC" : c.RucEmisor!.Trim();
            var carpeta = Path.Combine(CarpetaRaiz, ruc, c.FechaEmision.ToString("yyyy-MM"));
            Directory.CreateDirectory(carpeta);

            var baseNombre = $"{ruc}-{CodigoTipo(c.Tipo)}-{c.Serie}-{c.Correlativo:D8}";
            var xmlPath = Path.Combine(carpeta, baseNombre + ".xml");
            var cdrPath = Path.Combine(carpeta, "R-" + baseNombre + ".zip");   // SUNAT nombra el CDR con prefijo R-
            var pdfPath = Path.Combine(carpeta, baseNombre + ".pdf");

            if (!File.Exists(xmlPath))
                await File.WriteAllBytesAsync(xmlPath, c.XmlFirmado, ct);

            if (c.CdrZip is { Length: > 0 } && !File.Exists(cdrPath))
                await File.WriteAllBytesAsync(cdrPath, c.CdrZip, ct);

            if (!File.Exists(pdfPath))
            {
                c.Detalles = await _repo.ListarDetallesAsync(c.Id, ct);
                if (c is { Tipo: "GUIA_REMISION" }) c.Guia = await _repo.ObtenerGuiaDatosAsync(c.Id, ct);
                var negocio = await _negocioRepo.ObtenerAsync(negocioId, ct);
                if (negocio is not null)
                    await File.WriteAllBytesAsync(pdfPath, _pdf.Generar(c, negocio), ct);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo respaldar en disco el comprobante {Id}", comprobanteId);
        }
    }

    public async Task<int> RespaldarTodosAsync(int negocioId, CancellationToken ct = default)
    {
        var pendientes = await _repo.ListarIdsAceptadosAsync(negocioId, ct);
        var n = 0;
        foreach (var (id, sedeId) in pendientes)
        {
            await RespaldarAsync(id, sedeId, negocioId, ct);
            n++;
        }
        return n;
    }

    private static string CodigoTipo(string tipo) => tipo switch
    {
        "FACTURA" => "01",
        "NOTA_CREDITO" => "07",
        "NOTA_DEBITO" => "08",
        "GUIA_REMISION" => "09",
        _ => "03"   // BOLETA
    };
}
