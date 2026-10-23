using Lavanderia.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

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

    [Fact]
    public async Task Empezar_esSlugReservado_sinConsultarLaBaseDeDatos()
    {
        var controller = new RegistroPublicoController(null!, null!);

        var result = await controller.Disponible("Empezar", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = JsonSerializer.SerializeToElement(ok.Value);
        Assert.False(body.GetProperty("disponible").GetBoolean());
        Assert.Contains("reservado", body.GetProperty("mensaje").GetString());
    }
}
