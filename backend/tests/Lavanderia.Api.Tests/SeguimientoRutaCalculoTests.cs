using Lavanderia.Api.Repositories;
using Lavanderia.Api.Services;

namespace Lavanderia.Api.Tests;

public class SeguimientoRutaCalculoTests
{
    [Theory]
    [InlineData(null, "EN_RUTA")]
    [InlineData(600, "EN_RUTA")]
    [InlineData(500, "CERCA")]
    [InlineData(90, "LLEGO")]
    public void DeterminaEstadoSegunDistancia(int? distancia, string esperado)
    {
        var ruta = new RutaReparto { EstadoProceso = "LISTO", RutaIniciadaEn = DateTime.Now };

        Assert.Equal(esperado, SeguimientoRutaCalculo.DeterminarEstado(ruta, distancia));
    }

    [Fact]
    public void EntregadoTienePrioridadSobreDistancia()
    {
        var ruta = new RutaReparto { EstadoProceso = "ENTREGADO", RutaIniciadaEn = DateTime.Now };

        Assert.Equal("ENTREGADO", SeguimientoRutaCalculo.DeterminarEstado(ruta, 10_000));
    }
}
