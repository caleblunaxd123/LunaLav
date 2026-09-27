using Lavanderia.Api.Domain;
using Lavanderia.Api.Services.Pagos;

namespace Lavanderia.Api.Tests;

public class EnlacePagoTests
{
    [Theory]
    [InlineData(6, null)]
    [InlineData(5, "D5")]
    [InlineData(2, "D5")]
    [InlineData(1, "D1")]
    [InlineData(0, "D0")]
    [InlineData(-1, "VENCIDA")]
    [InlineData(-7, "VENCIDA")]
    [InlineData(-8, null)]
    public void Etapa_segunDiasParaVencer(int dias, string? etapa)
        => Assert.Equal(etapa, RecordatorioPagoWorker.Etapa(dias));

    private static Negocio Negocio(string estado = "ACTIVA") => new()
    {
        Nombre = "Lavandería Sol", TitularNombre = "Rosa Pérez", MontoMensual = 20m,
        EstadoSuscripcion = estado, ProximoPago = new DateOnly(2026, 10, 10)
    };

    [Fact]
    public void Mensaje_incluyeEnlaceMontoYSaludo()
    {
        var (asunto, cuerpo) = EnlacePagoService.Mensaje(Negocio(), "https://app.lunalav.pe/pagar/abc", 1);
        Assert.Equal("Tu suscripción a LunaLav vence mañana", asunto);
        Assert.StartsWith("Hola Rosa,", cuerpo);
        Assert.Contains("https://app.lunalav.pe/pagar/abc", cuerpo);
        Assert.Contains("S/ 20.00", cuerpo);
        Assert.Contains("sin iniciar sesión", cuerpo);
    }

    [Fact]
    public void Mensaje_distingueVencidaYPrueba()
    {
        var (asunto, cuerpo) = EnlacePagoService.Mensaje(Negocio("PRUEBA"), "u", -2);
        Assert.Equal("Tu suscripción a LunaLav venció", asunto);
        Assert.Contains("Tu prueba gratis de LunaLav venció", cuerpo);
    }
}
