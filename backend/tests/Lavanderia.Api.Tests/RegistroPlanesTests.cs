using Lavanderia.Api.Controllers;

namespace Lavanderia.Api.Tests;

public class RegistroPlanesTests
{
    // El alta autónoma recibe el plan comercial y debe guardar el código que usa el panel
    // de la plataforma (BASICO / PRO / PREMIUM); si no, el dueño no puede editar la suscripción.
    [Theory]
    [InlineData("BASICO", "BASICO")]
    [InlineData("FACTURA", "PRO")]
    [InlineData("factura", "PRO")]
    [InlineData("MULTISEDE", "PREMIUM")]
    public void PlanComercial_seGuardaConElCodigoDelPanel(string comercial, string interno)
        => Assert.Equal(interno, RegistroPublicoController.PlanInterno(comercial));
}
