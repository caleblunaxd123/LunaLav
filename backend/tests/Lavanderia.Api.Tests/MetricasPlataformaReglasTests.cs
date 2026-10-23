using Lavanderia.Api.Services;

namespace Lavanderia.Api.Tests;

public class MetricasPlataformaReglasTests
{
    private static DatosNegocioMetrica Negocio(string slug = "lavanderia", bool activo = true,
        string? email = "cliente@correo.pe", string estado = "PRUEBA", int pedidos = 0,
        DateTime? login = null, DateTime? ultimoPedido = null, bool pago = false,
        string? culqiEstado = null, string? culqiModo = null, decimal monto = 100m,
        bool? pagoReal = null,
        DateOnly? fin = null) => new(1, slug, activo, email, new DateTime(2026, 1, 1),
            "PRO", estado, monto, fin, false, login, false, false, pedidos,
            ultimoPedido, pago, pagoReal ?? pago, culqiEstado, culqiModo);

    [Theory]
    [InlineData("lavanderia", true, "cliente@correo.pe", null)]
    [InlineData("lavanderia", false, "cliente@correo.pe", "inactivo")]
    [InlineData("demo", true, "cliente@correo.pe", "demo")]
    [InlineData("plataforma-interna", true, "cliente@correo.pe", "plataforma_interna")]
    [InlineData("qa123", true, "cliente@correo.pe", "qa")]
    [InlineData("mi-test-1", true, "cliente@correo.pe", "test")]
    [InlineData("lavanderia", true, "cliente@example.com", "email_ejemplo")]
    [InlineData("interno", true, "cliente@correo.pe", "configurado")]
    public void ClasificaNegocios(string slug, bool activo, string email, string? motivo)
        => Assert.Equal(motivo, MetricasPlataformaReglas.MotivoExclusion(
            Negocio(slug, activo, email), ["interno"]));

    [Theory]
    [InlineData("no_login")]
    [InlineData("sin_pedidos")]
    [InlineData("inactivo_7d")]
    [InlineData("prueba_vencida_sin_pago")]
    [InlineData("convertido")]
    [InlineData("activo")]
    public void ClasificaMotivo(string esperado)
    {
        var ahora = new DateTime(2026, 10, 23);
        var reciente = ahora.AddDays(-1);
        var antiguo = ahora.AddDays(-10);
        var n = esperado switch
        {
            "no_login" => Negocio(),
            "sin_pedidos" => Negocio(login: reciente),
            "inactivo_7d" => Negocio(login: antiguo, pedidos: 1, ultimoPedido: antiguo),
            "prueba_vencida_sin_pago" => Negocio(fin: DateOnly.FromDateTime(antiguo)),
            "convertido" => Negocio(estado: "ACTIVA", pago: true),
            _ => Negocio(login: reciente, pedidos: 1, ultimoPedido: reciente)
        };
        Assert.Equal(esperado, MetricasPlataformaReglas.MotivoAbandono(n, ahora));
    }

    [Fact]
    public void ConversionYMrrSoloIncluyenPagadoresReales()
    {
        var pagador = Negocio(estado: "ACTIVA", pago: true, monto: 99m);
        var live = Negocio(estado: "ACTIVA", culqiEstado: "ACTIVA", culqiModo: "LIVE", monto: 149m);
        var test = Negocio(estado: "ACTIVA", culqiEstado: "ACTIVA", culqiModo: "TEST", monto: 199m);
        var pagoTest = Negocio(estado: "ACTIVA", pago: true, pagoReal: false,
            culqiEstado: "ACTIVA", culqiModo: "TEST", monto: 59m);
        Assert.True(MetricasPlataformaReglas.Pagando(pagador));
        Assert.True(MetricasPlataformaReglas.Pagando(live));
        Assert.False(MetricasPlataformaReglas.Pagando(test));
        Assert.False(MetricasPlataformaReglas.Pagando(pagoTest));
        Assert.Equal(248m, MetricasPlataformaReglas.Mrr([pagador, live, test, pagoTest]));
        Assert.Equal(66.67m, MetricasPlataformaReglas.Conversion(2, 3));
        Assert.Equal(0m, MetricasPlataformaReglas.Conversion(0, 0));
    }
}
