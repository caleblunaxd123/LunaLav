using System.Text;
using System.Xml;
using Lavanderia.Api.Domain;

namespace Lavanderia.Api.Services.Facturacion;

public record CredencialesEmisor(
    string Ambiente, string RucEmisor, string RazonSocial,
    string SolUsuario, string SolClave, byte[] CertificadoPfx, string CertificadoPassword,
    string ApiSunatPersonaId = "", string ApiSunatToken = "");

public record SolicitudEmision(ComprobanteElectronico Comprobante, List<PedidoItem> Items, CredencialesEmisor Credenciales, ConfiguracionFacturacion Config);

public record ResultadoEmision(
    bool Exitoso, string Estado, string? Codigo, string? Descripcion,
    byte[]? XmlFirmado, byte[]? CdrZip, string? ExternalId = null,
    string? HashCpe = null, DateTime? FechaRespuesta = null);

public record ResultadoConsulta(
    bool Exitoso, string Estado, string? Codigo, string? Descripcion,
    byte[]? XmlFirmado, byte[]? CdrZip, string? HashCpe, DateTime? FechaRespuesta);

public record ResultadoAnulacion(
    bool Exitoso, string Estado, string? Codigo, string? Descripcion, DateTime? FechaRespuesta);

public record ResultadoConexion(
    bool Exitoso, string Mensaje, bool? Produccion = null,
    string? UltimoNumero = null, string? NumeroSugerido = null);

/// <summary>
/// Punto de extensión para el envío del comprobante a SUNAT. Hoy solo existe
/// <see cref="SunatDirectoProvider"/> (SEE - Sistema del Contribuyente, sin OSE);
/// un proveedor tipo Nubefact se agregaría implementando esta misma interfaz.
/// </summary>
public interface IFacturacionElectronicaProvider
{
    string Codigo { get; }
    Task<ResultadoEmision> EmitirAsync(SolicitudEmision solicitud, CancellationToken ct = default);

    /// <summary>True si el emisor firma con su propio certificado (SUNAT directo). APISUNAT firma
    /// en la nube, así que NO exige certificado .pfx ni credenciales SOL en la configuración.</summary>
    bool RequiereCertificadoLocal => true;
    Task<ResultadoConsulta> ConsultarAsync(ComprobanteElectronico comprobante, CredencialesEmisor credenciales, CancellationToken ct = default)
        => Task.FromResult(new ResultadoConsulta(false, comprobante.Estado, "NO_SOPORTADO", "El proveedor no permite consulta asincrona.", null, null, null, null));
    Task<ResultadoAnulacion> AnularAsync(ComprobanteElectronico comprobante, CredencialesEmisor credenciales, string motivo, CancellationToken ct = default)
        => Task.FromResult(new ResultadoAnulacion(false, "ERROR", "NO_SOPORTADO", "El proveedor no permite anulacion remota.", null));
    Task<ResultadoConexion> ProbarConexionAsync(CredencialesEmisor credenciales, string tipo, string serie, CancellationToken ct = default)
        => Task.FromResult(new ResultadoConexion(false, "El proveedor no ofrece una prueba remota de credenciales."));
}

public class SunatDirectoProvider : IFacturacionElectronicaProvider
{
    private readonly SunatSoapClient _soap;
    public SunatDirectoProvider(SunatSoapClient soap) => _soap = soap;
    public string Codigo => "SUNAT_DIRECTO";

    public async Task<ResultadoEmision> EmitirAsync(SolicitudEmision solicitud, CancellationToken ct = default)
    {
        var (comprobante, items, credenciales, config) = solicitud;

        var xmlDoc = UblXmlBuilder.Construir(comprobante, config, items);
        var xmlBytes = SerializarUtf8(xmlDoc);
        var xmlFirmado = XmlSigner.Firmar(xmlBytes, credenciales.CertificadoPfx, credenciales.CertificadoPassword);

        var tipoDocCodigo = comprobante.Tipo == "FACTURA" ? "01" : "03";
        var nombreArchivo = $"{credenciales.RucEmisor}-{tipoDocCodigo}-{comprobante.Serie}-{comprobante.Correlativo:D8}";

        var resultado = await _soap.EnviarAsync(
            credenciales.Ambiente, credenciales.RucEmisor, credenciales.SolUsuario, credenciales.SolClave,
            nombreArchivo, xmlFirmado, ct);

        var estado = resultado.Exitoso ? "ACEPTADO" : "RECHAZADO";
        return new ResultadoEmision(resultado.Exitoso, estado, resultado.Codigo, resultado.Descripcion,
            xmlFirmado, resultado.CdrZip, HashCpe: XmlSigner.ObtenerDigest(xmlFirmado),
            FechaRespuesta: DateTime.Now);
    }

    private static byte[] SerializarUtf8(System.Xml.Linq.XDocument doc)
    {
        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, new XmlWriterSettings { Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false }))
            doc.Save(writer);
        return ms.ToArray();
    }
}
