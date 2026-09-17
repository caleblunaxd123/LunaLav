using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Lavanderia.Api.Services.Facturacion;

public record SunatEnvioResultado(bool Exitoso, string? Codigo, string Descripcion, byte[]? CdrZip);

/// <summary>Envía el XML firmado al billService de SUNAT (sendBill) y descifra la respuesta (CDR).</summary>
public class SunatSoapClient
{
    private readonly HttpClient _http;
    public SunatSoapClient(HttpClient http) => _http = http;

    private const string UrlBeta = "https://e-beta.sunat.gob.pe/ol-ti-itcpfegem-beta/billService";
    private const string UrlProduccion = "https://e-factura.sunat.gob.pe/ol-ti-itcpfegem/billService";

    public async Task<SunatEnvioResultado> EnviarAsync(
        string ambiente, string rucEmisor, string solUsuario, string solClave,
        string nombreArchivoSinExtension, byte[] xmlFirmado, CancellationToken ct = default)
    {
        var zip = ComprimirXml($"{nombreArchivoSinExtension}.xml", xmlFirmado);
        var zipBase64 = Convert.ToBase64String(zip);
        var url = ambiente == "PRODUCCION" ? UrlProduccion : UrlBeta;

        var sobre = ConstruirSobreSoap(rucEmisor, solUsuario, solClave, $"{nombreArchivoSinExtension}.zip", zipBase64);

        using var contenido = new StringContent(sobre, Encoding.UTF8, "text/xml");
        contenido.Headers.Add("SOAPAction", "\"\"");

        using var respuesta = await _http.PostAsync(url, contenido, ct);
        var textoRespuesta = await respuesta.Content.ReadAsStringAsync(ct);

        return InterpretarRespuesta(textoRespuesta, respuesta.IsSuccessStatusCode);
    }

    private static byte[] ComprimirXml(string nombreArchivo, byte[] xml)
    {
        using var ms = new MemoryStream();
        using (var archivo = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entrada = archivo.CreateEntry(nombreArchivo, CompressionLevel.Optimal);
            using var entradaStream = entrada.Open();
            entradaStream.Write(xml, 0, xml.Length);
        }
        return ms.ToArray();
    }

    private static string ConstruirSobreSoap(string ruc, string usuario, string clave, string nombreZip, string zipBase64) => $"""
        <soapenv:Envelope xmlns:soapenv="http://schemas.xmlsoap.org/soap/envelope/" xmlns:ser="http://service.sunat.gob.pe" xmlns:wsse="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd">
          <soapenv:Header>
            <wsse:Security>
              <wsse:UsernameToken>
                <wsse:Username>{ruc}{usuario}</wsse:Username>
                <wsse:Password>{clave}</wsse:Password>
              </wsse:UsernameToken>
            </wsse:Security>
          </soapenv:Header>
          <soapenv:Body>
            <ser:sendBill>
              <fileName>{nombreZip}</fileName>
              <contentFile>{zipBase64}</contentFile>
            </ser:sendBill>
          </soapenv:Body>
        </soapenv:Envelope>
        """;

    private static SunatEnvioResultado InterpretarRespuesta(string xmlRespuesta, bool httpOk)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xmlRespuesta); }
        catch { return new SunatEnvioResultado(false, null, $"Respuesta SUNAT no interpretable: {xmlRespuesta}", null); }

        var fault = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault");
        if (fault is not null)
        {
            var faultString = fault.Descendants().FirstOrDefault(e => e.Name.LocalName == "faultstring")?.Value;
            var detalle = fault.Descendants().FirstOrDefault(e => e.Name.LocalName is "message" or "detail")?.Value;
            return new SunatEnvioResultado(false, null, detalle ?? faultString ?? "SUNAT rechazó la solicitud (fault sin detalle).", null);
        }

        var appResponseB64 = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "applicationResponse")?.Value;
        if (string.IsNullOrWhiteSpace(appResponseB64))
            return new SunatEnvioResultado(false, null, "SUNAT no devolvió applicationResponse ni fault.", null);

        var cdrZip = Convert.FromBase64String(appResponseB64);
        var (codigo, descripcion) = LeerCdr(cdrZip);
        var exitoso = codigo == "0";
        return new SunatEnvioResultado(exitoso, codigo, descripcion, cdrZip);
    }

    private static (string? Codigo, string Descripcion) LeerCdr(byte[] cdrZip)
    {
        using var ms = new MemoryStream(cdrZip);
        using var archivo = new ZipArchive(ms, ZipArchiveMode.Read);
        var entrada = archivo.Entries.FirstOrDefault(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        if (entrada is null) return (null, "El CDR no contiene un XML de respuesta.");

        using var entradaStream = entrada.Open();
        var cdrDoc = XDocument.Load(entradaStream);
        var codigo = cdrDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ResponseCode")?.Value;
        var descripcion = cdrDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Description")?.Value ?? "";
        return (codigo, descripcion);
    }
}
