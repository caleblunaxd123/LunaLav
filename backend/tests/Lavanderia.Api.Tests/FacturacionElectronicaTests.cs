using System.Net;
using System.Text;
using System.Xml.Linq;
using Lavanderia.Api.Domain;
using Lavanderia.Api.Services.Facturacion;
using Microsoft.Extensions.Options;

namespace Lavanderia.Api.Tests;

public class FacturacionElectronicaTests
{
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";

    [Fact]
    public void Ubl_IncluyePerfilFiscalDireccionYAjustes()
    {
        var (c, config) = Datos();
        var doc = UblXmlBuilder.Construir(c, config, []);
        Assert.Equal("0101", doc.Descendants(Cbc + "ProfileID").Single().Value);
        Assert.Null(doc.Descendants(Cbc + "InvoiceTypeCode").Single().Attribute("listID"));
        Assert.Equal("150110", doc.Descendants(Cac + "RegistrationAddress").Descendants(Cbc + "ID").Single().Value);
        Assert.Contains(doc.Descendants(Cac + "AllowanceCharge"), x => x.Element(Cbc + "ChargeIndicator")?.Value == "false");
        Assert.Contains(doc.Descendants(Cac + "AllowanceCharge"), x => x.Element(Cbc + "ChargeIndicator")?.Value == "true");
        Assert.All(doc.Descendants(Cac + "AllowanceCharge"), x => Assert.Equal("16.95", x.Element(Cbc + "BaseAmount")?.Value));
    }

    [Fact]
    public void Ubl_UsaPrecioUnitarioConIgv_NoTotalDeLinea()
    {
        var (c, config) = Datos();
        var doc = UblXmlBuilder.Construir(c, config, []);
        var alternativo = doc.Descendants(Cac + "AlternativeConditionPrice").Single()
            .Element(Cbc + "PriceAmount")!.Value;
        Assert.Equal("10.0000", alternativo);
        Assert.Equal("16.95", doc.Descendants(Cbc + "LineExtensionAmount").First().Value);
    }

    [Fact]
    public void ApiSunatDocumentBody_ExcluyeSobreYFirma()
    {
        var (c, config) = Datos();
        var body = XmlJsCompact.ConvertirDocumentBody(UblXmlBuilder.Construir(c, config, []).Root!,
            "ext:UBLExtensions", "cac:Signature");
        Assert.False(body.ContainsKey("_attributes"));
        Assert.False(body.ContainsKey("ext:UBLExtensions"));
        Assert.False(body.ContainsKey("cac:Signature"));
        Assert.Equal("F001-00000007", body["cbc:ID"]?["_text"]?.GetValue<string>());
    }

    [Fact]
    public void Ubl_TotalesCoincidenConSnapshot()
    {
        var (c, config) = Datos();
        var doc = UblXmlBuilder.Construir(c, config, []);
        Assert.Equal("24.00", doc.Descendants(Cbc + "PayableAmount").Single().Value);
        Assert.Equal("20.34", doc.Descendants(Cac + "TaxTotal").First().Descendants(Cbc + "TaxableAmount").Single().Value);
        Assert.Equal("3.66", doc.Descendants(Cac + "TaxTotal").First().Descendants(Cbc + "TaxAmount").Skip(1).Single().Value);
    }

    [Fact]
    public async Task ApiSunat_NoConvierteRechazoHttp200EnPendiente()
    {
        var handler = new RespuestaHttpHandler(HttpStatusCode.OK,
            "{\"documentId\":\"doc-1\",\"status\":\"RECHAZADO\"}");
        var provider = new ApiSunatProvider(new HttpClient(handler),
            Options.Create(new ApiSunatOptions { BaseUrl = "https://back.apisunat.test" }));
        var (c, config) = Datos();
        var credenciales = new CredencialesEmisor("BETA", config.RucEmisor!, config.RazonSocial!, "", "", [], "",
            "persona-1", "token-1");

        var resultado = await provider.EmitirAsync(new SolicitudEmision(c, [], credenciales, config));

        Assert.False(resultado.Exitoso);
        Assert.Equal("RECHAZADO", resultado.Estado);
        Assert.Equal("doc-1", resultado.ExternalId);
    }

    [Fact]
    public async Task ApiSunat_RespuestaSinDocumentIdEsError()
    {
        var handler = new RespuestaHttpHandler(HttpStatusCode.OK, "{\"status\":\"PENDIENTE\"}");
        var provider = new ApiSunatProvider(new HttpClient(handler),
            Options.Create(new ApiSunatOptions { BaseUrl = "https://back.apisunat.test" }));
        var (c, config) = Datos();
        var credenciales = new CredencialesEmisor("BETA", config.RucEmisor!, config.RazonSocial!, "", "", [], "",
            "persona-1", "token-1");

        var resultado = await provider.EmitirAsync(new SolicitudEmision(c, [], credenciales, config));

        Assert.False(resultado.Exitoso);
        Assert.Equal("ERROR", resultado.Estado);
    }

    [Theory]
    [InlineData("20612175722")]
    [InlineData("20100070970")]
    public void Ruc_Valido_PasaDigitoVerificador(string ruc)
        => Assert.True(DocumentoFiscalValidator.EsRucValido(ruc));

    [Theory]
    [InlineData("20612175721")]
    [InlineData("00123456789")]
    [InlineData("2012345678A")]
    public void Ruc_Invalido_SeRechaza(string ruc)
        => Assert.False(DocumentoFiscalValidator.EsRucValido(ruc));

    private static (ComprobanteElectronico, ConfiguracionFacturacion) Datos()
    {
        var c = new ComprobanteElectronico
        {
            Tipo = "FACTURA", Serie = "F001", Correlativo = 7, FechaEmision = new DateTime(2026, 8, 12, 10, 0, 0),
            ClienteTipoDoc = "RUC", ClienteNumDoc = "20123456789", ClienteNombre = "CLIENTE SAC",
            Subtotal = 20, Descuento = 2, Recargo = 6, Redondeo = 0, OpGravada = 20.34m, Igv = 3.66m,
            Total = 24, IgvPorcentaje = 18,
            Detalles = [new ComprobanteElectronicoDetalle { NumeroLinea = 1, Descripcion = "Lavado", UnidadMedida = "ZZ",
                Cantidad = 2, PrecioUnitarioIgv = 10, ValorVenta = 16.95m, Igv = 3.05m, Total = 20m }]
        };
        var config = new ConfiguracionFacturacion
        {
            RucEmisor = "20612175722", RazonSocial = "ADCON PERU SAC", DireccionFiscal = "Av. Principal 123",
            Ubigeo = "150110", CodigoEstablecimiento = "0000"
        };
        return (c, config);
    }

    private sealed class RespuestaHttpHandler(HttpStatusCode status, string contenido) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(contenido, Encoding.UTF8, "application/json")
            });
    }
}
