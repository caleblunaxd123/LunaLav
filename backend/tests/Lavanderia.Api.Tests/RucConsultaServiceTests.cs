using System.Net;
using System.Text;
using Lavanderia.Api.Services.Facturacion;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lavanderia.Api.Tests;

public class RucConsultaServiceTests
{
    private const string RucSunat = "20131312955"; // RUC público de SUNAT (dígito verificador correcto)

    /// <summary>Responde según el host: permite simular que un proveedor falla y el otro no.</summary>
    private sealed class FakeHandler(Func<Uri, (HttpStatusCode, string)> responder) : HttpMessageHandler
    {
        public List<Uri> Llamadas { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Llamadas.Add(request.RequestUri!);
            var (status, body) = responder(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static (RucConsultaService svc, FakeHandler handler) Crear(Func<Uri, (HttpStatusCode, string)> responder)
    {
        var handler = new FakeHandler(responder);
        var svc = new RucConsultaService(new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()), NullLogger<RucConsultaService>.Instance);
        return (svc, handler);
    }

    private const string OpenRucOk = """{"ruc":"20131312955","razon_social":"SUNAT","estado":"ACTIVO","condicion":"HABIDO","direccion":"AV. GARCILASO DE LA VEGA NRO 1472"}""";

    [Fact]
    public async Task DigitoVerificadorInvalido_noConsultaNiAcepta()
    {
        var (svc, handler) = Crear(_ => (HttpStatusCode.OK, OpenRucOk));
        var r = await svc.ConsultarAsync("20131312956", CancellationToken.None);
        Assert.False(r.FormatoValido);
        Assert.NotNull(r.Problema);
        Assert.Empty(handler.Llamadas);
    }

    [Fact]
    public async Task RucExistente_devuelveRazonSocialYEstado()
    {
        var (svc, _) = Crear(_ => (HttpStatusCode.OK, OpenRucOk));
        var r = await svc.ConsultarAsync(RucSunat, CancellationToken.None);
        Assert.True(r.Verificado);
        Assert.True(r.Existe);
        Assert.Equal("SUNAT", r.RazonSocial);
        Assert.True(r.ActivoHabido);
        Assert.Null(r.Problema);
        Assert.Null(r.Advertencia);
    }

    [Fact]
    public async Task RucInexistente_seRechaza()
    {
        var (svc, _) = Crear(_ => (HttpStatusCode.NotFound, """{"error":"not_found"}"""));
        var r = await svc.ConsultarAsync(RucSunat, CancellationToken.None);
        Assert.True(r.Verificado);
        Assert.False(r.Existe);
        Assert.Equal("Ese RUC no existe en el padrón de SUNAT.", r.Problema);
    }

    [Fact]
    public async Task SiOpenRucFalla_usaElRespaldo()
    {
        const string apisNet = """{"nombre":"SUNAT","estado":"ACTIVO","condicion":"NO HABIDO","direccion":"AV. X"}""";
        var (svc, handler) = Crear(u => u.Host == "openruc.com" ? (HttpStatusCode.ServiceUnavailable, "") : (HttpStatusCode.OK, apisNet));
        var r = await svc.ConsultarAsync(RucSunat, CancellationToken.None);
        Assert.True(r.Existe);
        Assert.False(r.ActivoHabido);
        Assert.NotNull(r.Advertencia); // no bloquea, pero avisa que no podrá facturarle
        Assert.Null(r.Problema);
        Assert.Equal(2, handler.Llamadas.Count);
    }

    [Fact]
    public async Task SiAmbosFallan_noBloqueaYNoCachea()
    {
        var (svc, handler) = Crear(_ => (HttpStatusCode.InternalServerError, ""));
        var r = await svc.ConsultarAsync(RucSunat, CancellationToken.None);
        Assert.True(r.FormatoValido);
        Assert.False(r.Verificado);
        Assert.Null(r.Problema);
        await svc.ConsultarAsync(RucSunat, CancellationToken.None);
        Assert.Equal(4, handler.Llamadas.Count); // reintenta en la siguiente consulta
    }

    [Fact]
    public async Task ResultadoSeCachea()
    {
        var (svc, handler) = Crear(_ => (HttpStatusCode.OK, OpenRucOk));
        await svc.ConsultarAsync(RucSunat, CancellationToken.None);
        await svc.ConsultarAsync(" 2013131 2955 ", CancellationToken.None); // mismo RUC con espacios
        Assert.Single(handler.Llamadas);
    }
}
