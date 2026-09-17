using Lavanderia.Api.Domain;

namespace Lavanderia.Api.Tests;

public class InventarioReglasTests
{
    [Theory]
    [InlineData("EQUIPO", "EQUIPO")]
    [InlineData("MATERIAL", "MATERIAL")]
    [InlineData("INSUMO", "INSUMO")]
    [InlineData("equipo", "EQUIPO")]          // sin importar mayúsculas
    [InlineData("  material  ", "MATERIAL")]  // recorta espacios
    public void NormalizarClase_valida_pasa_normalizada(string entrada, string esperado)
    {
        Assert.Equal(esperado, InventarioReglas.NormalizarClase(entrada));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("OTRA_COSA")]
    [InlineData("EQUIPOS")]   // parecido pero no exacto
    public void NormalizarClase_invalida_cae_a_INSUMO(string? entrada)
    {
        Assert.Equal("INSUMO", InventarioReglas.NormalizarClase(entrada));
    }
}
