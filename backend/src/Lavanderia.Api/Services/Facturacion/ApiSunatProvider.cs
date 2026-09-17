using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Lavanderia.Api.Domain;
using Microsoft.Extensions.Options;

namespace Lavanderia.Api.Services.Facturacion;

public class ApiSunatOptions
{
    public string BaseUrl { get; set; } = "https://back.apisunat.com";
    /// <summary>Id de la persona/empresa en APISUNAT (lo entrega su panel).</summary>
    public string PersonaId { get; set; } = "";
    /// <summary>Token de la API. Usar primero el de PRUEBAS (DEV) y luego el de producción.</summary>
    public string PersonaToken { get; set; } = "";
}

/// <summary>
/// Proveedor de emisión vía APISUNAT (PSE en la nube): en vez de firmar y presentar el XML a
/// SUNAT nosotros mismos, se envía el comprobante como JSON (formato xml-js "compact", el mismo
/// del botón "Generador JSON" de su panel) y APISUNAT lo firma y lo presenta.
///
/// Reutiliza <see cref="UblXmlBuilder"/> (ya validado contra SUNAT beta) y convierte ese XML a
/// JSON, así el contenido del comprobante es idéntico entre ambos proveedores.
///
/// Activación cuando Mekias entregue los datos:
///   1. appsettings → "FacturacionElectronica": { "Proveedor": "APISUNAT" }
///   2. appsettings → "ApiSunat": { "PersonaId": "...", "PersonaToken": "..." } (token DEV primero)
///   3. Contrastar el JSON generado con el ejemplo del "Generador JSON" y ajustar si difiere.
/// </summary>
public class ApiSunatProvider : IFacturacionElectronicaProvider
{
    private readonly HttpClient _http;
    private readonly ApiSunatOptions _opciones;

    public ApiSunatProvider(HttpClient http, IOptions<ApiSunatOptions> opciones)
    {
        _http = http;
        _opciones = opciones.Value;
    }

    /// <summary>APISUNAT firma en la nube: no se necesita certificado ni credenciales SOL locales.</summary>
    public bool RequiereCertificadoLocal => false;
    public string Codigo => "APISUNAT";

    public async Task<ResultadoEmision> EmitirAsync(SolicitudEmision solicitud, CancellationToken ct = default)
    {
        var (comprobante, items, credenciales, config) = solicitud;

        var personaId = string.IsNullOrWhiteSpace(credenciales.ApiSunatPersonaId) ? _opciones.PersonaId : credenciales.ApiSunatPersonaId;
        var personaToken = string.IsNullOrWhiteSpace(credenciales.ApiSunatToken) ? _opciones.PersonaToken : credenciales.ApiSunatToken;
        if (string.IsNullOrWhiteSpace(personaId) || string.IsNullOrWhiteSpace(personaToken))
            return new ResultadoEmision(false, "RECHAZADO", "CONFIG",
                "Faltan las credenciales de APISUNAT (ApiSunat:PersonaId / ApiSunat:PersonaToken).", null, null);

        // Mismo contenido UBL 2.1 que el envío directo; APISUNAT se encarga de la firma.
        // OJO: APISUNAT arma el sobre <Invoice> (namespaces + UBLExtensions + firma) por su cuenta,
        // así que el documentBody es SOLO el CONTENIDO del Invoice (sus hijos), plano, sin el nodo
        // raíz, sin la declaración XML, y sin ext:UBLExtensions ni cac:Signature.
        var esNotaCredito = comprobante.Tipo == "NOTA_CREDITO";
        var esNotaDebito = comprobante.Tipo == "NOTA_DEBITO";
        var xmlDoc = esNotaCredito
            ? UblXmlBuilder.ConstruirNotaCredito(comprobante, config, items)
            : esNotaDebito
                ? UblXmlBuilder.ConstruirNotaDebito(comprobante, config, items)
                : UblXmlBuilder.Construir(comprobante, config, items);
        var documentBody = XmlJsCompact.ConvertirDocumentBody(xmlDoc.Root!, "ext:UBLExtensions", "cac:Signature");

        // Nombre de archivo con el formato oficial SUNAT: RUC-TIPO-SERIE-CORRELATIVO(8 dígitos).
        var tipoDocCodigo = esNotaCredito ? "07" : esNotaDebito ? "08" : comprobante.Tipo == "FACTURA" ? "01" : "03";
        var fileName = $"{credenciales.RucEmisor}-{tipoDocCodigo}-{comprobante.Serie}-{comprobante.Correlativo:D8}";

        var payload = new JsonObject
        {
            ["personaId"] = personaId,
            ["personaToken"] = personaToken,
            ["fileName"] = fileName,
            ["documentBody"] = documentBody
        };

        using var contenido = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var respuesta = await _http.PostAsync($"{_opciones.BaseUrl.TrimEnd('/')}/personas/v1/sendBill", contenido, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

        if (!respuesta.IsSuccessStatusCode)
            return new ResultadoEmision(false, "RECHAZADO", ((int)respuesta.StatusCode).ToString(),
                $"APISUNAT respondió {(int)respuesta.StatusCode}: {Recortar(cuerpo)}", null, null);

        // APISUNAT procesa en cola: la respuesta inmediata trae documentId + estado inicial.
        string? documentId = null;
        string? status = null;
        try
        {
            var json = JsonNode.Parse(cuerpo);
            documentId = json?["documentId"]?.GetValue<string>();
            status = json?["status"]?.GetValue<string>();
        }
        catch { /* respuesta no-JSON: se conserva el cuerpo crudo en la descripción */ }

        var estado = MapearEstado(status);
        var exitoso = (estado is "PENDIENTE" or "ACEPTADO") && !string.IsNullOrWhiteSpace(documentId);
        if (!exitoso && estado == "PENDIENTE") estado = "ERROR";
        return new ResultadoEmision(exitoso, estado, status ?? "RESPUESTA_INVALIDA",
            string.IsNullOrWhiteSpace(documentId)
                ? "APISUNAT no devolvio el identificador del documento."
                : $"APISUNAT {status ?? "RESPUESTA_INVALIDA"}.",
            null, null, documentId,
            FechaRespuesta: estado is "ACEPTADO" or "RECHAZADO" ? DateTime.Now : null);
    }

    public async Task<ResultadoConsulta> ConsultarAsync(
        ComprobanteElectronico comprobante, CredencialesEmisor credenciales, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(comprobante.ExternalId))
            return new(false, comprobante.Estado, "SIN_ID", "El comprobante no tiene documentId de APISUNAT.", null, null, null, null);

        using var respuesta = await _http.GetAsync(
            $"{_opciones.BaseUrl.TrimEnd('/')}/documents/{Uri.EscapeDataString(comprobante.ExternalId)}/getById", ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        if (!respuesta.IsSuccessStatusCode)
            return new(false, comprobante.Estado, ((int)respuesta.StatusCode).ToString(),
                $"APISUNAT respondio {(int)respuesta.StatusCode}: {Recortar(cuerpo)}", null, null, null, null);

        JsonNode? json;
        try { json = JsonNode.Parse(cuerpo); }
        catch { return new(false, "ERROR", "RESPUESTA_INVALIDA", "APISUNAT devolvio una respuesta no interpretable.", null, null, null, null); }

        var estado = MapearEstado(json?["status"]?.GetValue<string>());
        var xml = await DescargarArchivoSeguroAsync(json?["xml"]?.GetValue<string>(), ct);
        var cdr = await DescargarArchivoSeguroAsync(json?["cdr"]?.GetValue<string>(), ct);
        var descripcion = DescribirRespuesta(json);
        var fechaRespuesta = LeerUnix(json?["responseTime"]);
        return new(true, estado, json?["status"]?.GetValue<string>(), descripcion,
            xml, cdr, XmlSigner.ObtenerDigest(xml), fechaRespuesta);
    }

    public async Task<ResultadoAnulacion> AnularAsync(
        ComprobanteElectronico comprobante, CredencialesEmisor credenciales, string motivo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(comprobante.ExternalId))
            return new(false, "ERROR", "SIN_ID", "El comprobante no tiene documentId de APISUNAT.", null);
        if (string.IsNullOrWhiteSpace(credenciales.ApiSunatPersonaId) || string.IsNullOrWhiteSpace(credenciales.ApiSunatToken))
            return new(false, "ERROR", "CONFIG", "Faltan credenciales APISUNAT del negocio.", null);

        var payload = new JsonObject
        {
            ["personaId"] = credenciales.ApiSunatPersonaId,
            ["personaToken"] = credenciales.ApiSunatToken,
            ["documentId"] = comprobante.ExternalId,
            ["reason"] = motivo
        };
        using var contenido = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var respuesta = await _http.PostAsync($"{_opciones.BaseUrl.TrimEnd('/')}/personas/v1/voidBill", contenido, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        if (!respuesta.IsSuccessStatusCode)
            return new(false, "ERROR", ((int)respuesta.StatusCode).ToString(), Recortar(cuerpo), null);
        var status = JsonNode.Parse(cuerpo)?["status"]?.GetValue<string>();
        return new(true, MapearEstadoAnulacion(status), status, $"APISUNAT {status ?? "PENDIENTE"}.",
            string.Equals(status, "ACEPTADO", StringComparison.OrdinalIgnoreCase) ? DateTime.Now : null);
    }

    public async Task<ResultadoConexion> ProbarConexionAsync(
        CredencialesEmisor credenciales, string tipo, string serie, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(credenciales.ApiSunatPersonaId) || string.IsNullOrWhiteSpace(credenciales.ApiSunatToken))
            return new(false, "Completa personaId y token de APISUNAT.");

        var payload = new JsonObject
        {
            ["personaId"] = credenciales.ApiSunatPersonaId,
            ["personaToken"] = credenciales.ApiSunatToken,
            ["type"] = tipo,
            ["serie"] = serie
        };
        try
        {
            using var contenido = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            using var respuesta = await _http.PostAsync($"{_opciones.BaseUrl.TrimEnd('/')}/personas/lastDocument", contenido, ct);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
            if (!respuesta.IsSuccessStatusCode)
                return new(false, $"APISUNAT respondio {(int)respuesta.StatusCode}: {Recortar(cuerpo)}");
            var json = JsonNode.Parse(cuerpo);
            return new(true, "Credenciales verificadas correctamente.",
                json?["production"]?.GetValue<bool>(), ValorTexto(json?["lastNumber"]), ValorTexto(json?["suggestedNumber"]));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, "APISUNAT no respondio dentro del tiempo esperado.");
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return new(false, "No se pudo validar la respuesta de APISUNAT.");
        }
    }

    private async Task<byte[]?> DescargarArchivoSeguroAsync(string? url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        using var respuesta = await _http.GetAsync(uri, ct);
        return respuesta.IsSuccessStatusCode ? await respuesta.Content.ReadAsByteArrayAsync(ct) : null;
    }

    private static string MapearEstado(string? status) => status?.ToUpperInvariant() switch
    {
        "ACEPTADO" => "ACEPTADO", "ANULADO" or "BAJA" => "ANULADO", "RECHAZADO" => "RECHAZADO",
        "EXCEPCION" or "ERROR" => "ERROR", "PENDIENTE" => "PENDIENTE", _ => "ERROR"
    };

    private static string MapearEstadoAnulacion(string? status) => status?.ToUpperInvariant() switch
    {
        "ACEPTADO" => "ANULADO", "RECHAZADO" => "RECHAZADO", "EXCEPCION" => "ERROR", _ => "PENDIENTE"
    };

    private static DateTime? LeerUnix(JsonNode? nodo)
    {
        try { return nodo is null ? null : DateTimeOffset.FromUnixTimeSeconds(nodo.GetValue<long>()).LocalDateTime; }
        catch { return null; }
    }

    private static string DescribirRespuesta(JsonNode? json)
    {
        var partes = new List<string>();
        foreach (var nombre in new[] { "faults", "notes" })
            if (json?[nombre] is JsonArray arr)
                partes.AddRange(arr.Select(x => x?.ToJsonString()).Where(x => !string.IsNullOrWhiteSpace(x))!);
        return partes.Count == 0 ? $"APISUNAT {json?["status"]?.GetValue<string>() ?? "PENDIENTE"}." : Recortar(string.Join(" | ", partes));
    }

    private static string? ValorTexto(JsonNode? nodo)
    {
        if (nodo is null) return null;
        try { return nodo.GetValue<string>(); }
        catch { return nodo.ToJsonString().Trim('"'); }
    }

    private static string Recortar(string s) => s.Length <= 300 ? s : s[..300] + "…";
}

/// <summary>
/// Convierte un XDocument al formato JSON "compact" de xml-js (el que APISUNAT usa como
/// documentBody): elementos como objetos, atributos en "_attributes", texto en "_text" y
/// elementos repetidos como arreglos.
/// </summary>
public static class XmlJsCompact
{
    /// <summary>documentBody para APISUNAT: el contenido PLANO del Invoice (sus hijos), sin el nodo
    /// raíz ni la declaración, y quitando los nombres indicados (ext:UBLExtensions, cac:Signature),
    /// que APISUNAT agrega al firmar. También se descartan los namespaces del Invoice (_attributes).</summary>
    public static JsonObject ConvertirDocumentBody(XElement invoice, params string[] omitir)
    {
        var obj = (JsonObject)ConvertirElemento(invoice);
        obj.Remove("_attributes");
        foreach (var nombre in omitir) obj.Remove(nombre);
        return obj;
    }

    public static JsonObject Convertir(XDocument doc)
    {
        var raiz = new JsonObject
        {
            ["_declaration"] = new JsonObject
            {
                ["_attributes"] = new JsonObject
                {
                    ["version"] = "1.0",
                    ["encoding"] = "UTF-8",
                    ["standalone"] = "no"
                }
            }
        };
        if (doc.Root is not null)
            raiz[Nombre(doc.Root)] = ConvertirElemento(doc.Root);
        return raiz;
    }

    private static string Nombre(XElement el)
    {
        var prefijo = el.GetPrefixOfNamespace(el.Name.Namespace);
        return string.IsNullOrEmpty(prefijo) ? el.Name.LocalName : $"{prefijo}:{el.Name.LocalName}";
    }

    private static JsonNode ConvertirElemento(XElement el)
    {
        var obj = new JsonObject();

        var atributos = new JsonObject();
        foreach (var atr in el.Attributes())
        {
            string nombre;
            if (atr.IsNamespaceDeclaration)
                nombre = atr.Name.LocalName == "xmlns" ? "xmlns" : $"xmlns:{atr.Name.LocalName}";
            else if (atr.Name.Namespace == XNamespace.None)
                nombre = atr.Name.LocalName;
            else
                nombre = $"{el.GetPrefixOfNamespace(atr.Name.Namespace)}:{atr.Name.LocalName}";
            atributos[nombre] = atr.Value;
        }
        if (atributos.Count > 0) obj["_attributes"] = atributos;

        var hijos = el.Elements().ToList();
        if (hijos.Count == 0)
        {
            if (el.Value.Length > 0) obj["_text"] = el.Value;
            return obj;
        }

        foreach (var grupo in hijos.GroupBy(Nombre))
        {
            var convertidos = grupo.Select(ConvertirElemento).ToList();
            if (convertidos.Count == 1)
            {
                obj[grupo.Key] = convertidos[0];
            }
            else
            {
                var arr = new JsonArray();
                foreach (var c in convertidos) arr.Add(c);
                obj[grupo.Key] = arr;
            }
        }
        return obj;
    }
}
