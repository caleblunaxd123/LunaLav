using System.Net;
using System.Text;
using Lavanderia.Api.Services.Pagos;
using Microsoft.Extensions.Configuration;

namespace Lavanderia.Api.Tests;

public class CulqiClientTests
{
    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Ultima { get; private set; }
        public string? CuerpoEnviado { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Ultima = request;
            CuerpoEnviado = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (CulqiClient client, FakeHandler handler) Crear(HttpStatusCode status, string body, string secret = "sk_test_abc")
    {
        var handler = new FakeHandler(status, body);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Culqi:PublicKey"] = "pk_test_abc", ["Culqi:SecretKey"] = secret
        }).Build();
        return (new CulqiClient(new HttpClient(handler), config), handler);
    }

    [Fact]
    public async Task Suscripcion_leeCargosExitososYFallidosDeLosPeriodos()
    {
        const string json = """
        { "id": "sxn_test_AAAAAAAAAAAAAAAA", "status": 3,
          "periods": [
            { "period": 0, "status": 1, "charges": [
              { "charge_id": "chr_test_OK1", "charger_status": 1, "amount": 5000, "charge_day": 1790000000, "error": "" } ] },
            { "period": 1, "status": 2, "charges": [
              { "charge_id": "chr_test_KO2", "charger_status": 2, "amount": 5000, "charge_day": 1792600000, "error": "Fondos insuficientes" } ] }
          ] }
        """;
        var (client, handler) = Crear(HttpStatusCode.OK, json);

        var s = await client.ObtenerSuscripcionAsync("sxn_test_AAAAAAAAAAAAAAAA", CancellationToken.None);

        Assert.Equal("https://api.culqi.com/v2/recurrent/subscriptions/sxn_test_AAAAAAAAAAAAAAAA/", handler.Ultima!.RequestUri!.ToString());
        Assert.Equal("Bearer sk_test_abc", handler.Ultima.Headers.Authorization!.ToString());
        Assert.False(s.Cancelada);
        Assert.Equal(2, s.Cargos.Count);
        Assert.True(s.Cargos[0].Exitoso);
        Assert.Equal(50m, s.Cargos[0].MontoSoles);
        Assert.False(s.Cargos[1].Exitoso);
        Assert.Equal("Fondos insuficientes", s.Cargos[1].Error);
    }

    [Fact]
    public async Task Suscripcion_aceptaPeriodoComoObjetoUnico()
    {
        // Así aparece en el ejemplo de la documentación de Culqi.
        const string json = """
        { "id": "sxn_test_B", "status": 4,
          "periods": { "period": 0, "status": 1, "charges": { "charge_id": "chr_test_X", "charger_status": 1, "amount": 2000 } } }
        """;
        var (client, _) = Crear(HttpStatusCode.OK, json);
        var s = await client.ObtenerSuscripcionAsync("sxn_test_B", CancellationToken.None);
        Assert.True(s.Cancelada);
        Assert.Single(s.Cargos);
        Assert.Equal(20m, s.Cargos[0].MontoSoles);
    }

    [Fact]
    public async Task Tarjeta_detectaQueElBancoPide3DS()
    {
        var (client, _) = Crear(HttpStatusCode.OK, """{ "action_code": "REVIEW", "user_message": "El usuario necesita autenticarse" }""");
        var r = await client.CrearTarjetaAsync("cus_test_X", "tkn_test_X", null, CancellationToken.None);
        Assert.True(r.Requiere3DS);
        Assert.Null(r.Tarjeta);
    }

    [Fact]
    public async Task Tarjeta_envia3DSYLeeMarcaYUltimosDigitos()
    {
        const string json = """{ "id": "crd_test_OK", "source": { "last_four": "1111", "iin": { "card_brand": "Visa" } } }""";
        var (client, handler) = Crear(HttpStatusCode.Created, json);
        var r = await client.CrearTarjetaAsync("cus_test_X", "tkn_test_X",
            new Parametros3DS("05", "xid", "cavv", "2.1.0", "ds-id"), CancellationToken.None);
        Assert.False(r.Requiere3DS);
        Assert.Equal("crd_test_OK", r.Tarjeta!.Id);
        Assert.Equal("Visa", r.Tarjeta.Marca);
        Assert.Equal("1111", r.Tarjeta.Ultimos4);
        Assert.Contains("\"authentication_3DS\"", handler.CuerpoEnviado);
        Assert.Contains("\"directoryServerTransactionId\":\"ds-id\"", handler.CuerpoEnviado);
    }

    [Fact]
    public async Task Plan_esMensualIndefinidoEnCentimos()
    {
        var (client, handler) = Crear(HttpStatusCode.Created, """{ "id": "pln_test_P" }""");
        var id = await client.CrearPlanMensualAsync(5000, CancellationToken.None);
        Assert.Equal("pln_test_P", id);
        Assert.EndsWith("/v2/recurrent/plans/create", handler.Ultima!.RequestUri!.ToString());
        Assert.Contains("\"interval_unit_time\":3", handler.CuerpoEnviado);
        Assert.Contains("\"interval_count\":0", handler.CuerpoEnviado);
        Assert.Contains("\"amount\":5000", handler.CuerpoEnviado);
    }

    [Fact]
    public async Task Error_muestraElMensajeParaElUsuario()
    {
        var (client, _) = Crear(HttpStatusCode.BadRequest, """{ "user_message": "Tu tarjeta fue rechazada.", "code": "card_declined" }""");
        var e = await Assert.ThrowsAsync<CulqiException>(() => client.CrearClienteAsync("Ana", "Pérez", "a@b.pe", "999999999", "Av. Lima 123", "Lima", 1, CancellationToken.None));
        Assert.Equal("Tu tarjeta fue rechazada.", e.Message);
        Assert.Equal("card_declined", e.Codigo);
    }

    [Fact]
    public void Modo_seDeduceDeLaLlaveSecreta()
    {
        Assert.Equal("TEST", Crear(HttpStatusCode.OK, "{}").client.Modo);
        Assert.Equal("LIVE", Crear(HttpStatusCode.OK, "{}", "sk_live_x").client.Modo);
    }
}
