using Lavanderia.Api.Auth;
using Lavanderia.Api.Domain;

namespace Lavanderia.Api.Tests;

public class NegocioAccessRulesTests
{
    [Theory]
    [InlineData("ACTIVA")]
    [InlineData("PRUEBA")]
    public void PermiteNegocioActivoConSuscripcionOperativa(string estado)
    {
        var negocio = new Negocio { Activo = true, EstadoSuscripcion = estado };

        Assert.True(NegocioAccessRules.PuedeOperar(negocio));
    }

    [Theory]
    [InlineData("VENCIDA")]
    [InlineData("SUSPENDIDA")]
    public void BloqueaSuscripcionNoOperativa(string estado)
    {
        var negocio = new Negocio { Activo = true, EstadoSuscripcion = estado };

        Assert.False(NegocioAccessRules.PuedeOperar(negocio));
    }

    [Fact]
    public void BloqueaNegocioInactivoAunqueSuscripcionEsteActiva()
    {
        var negocio = new Negocio { Activo = false, EstadoSuscripcion = "ACTIVA" };

        Assert.False(NegocioAccessRules.PuedeOperar(negocio));
    }
}
