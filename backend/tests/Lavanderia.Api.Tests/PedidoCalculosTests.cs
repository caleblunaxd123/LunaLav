using Lavanderia.Api.Domain;

namespace Lavanderia.Api.Tests;

/// <summary>
/// Cubre la lógica pura de negocio del pedido: redondeo del dinero, estado de pago, puntos,
/// la máquina de estados del flujo y las validaciones de contacto/entrega.
/// Es donde vive el "dinero" y las transiciones, así que es lo primero que debe estar blindado.
/// </summary>
public class PedidoCalculosTests
{
    // ---------------- Redondeo a 10 céntimos ----------------
    [Theory]
    [InlineData(10.00, 10.00)]
    [InlineData(10.04, 10.00)]   // baja al múltiplo más cercano
    [InlineData(10.05, 10.10)]   // punto medio: redondea hacia arriba (AwayFromZero)
    [InlineData(10.06, 10.10)]
    [InlineData(0.00, 0.00)]
    [InlineData(19.94, 19.90)]
    [InlineData(19.95, 20.00)]
    public void RedondearA10Centimos_redondeaComercialmente(decimal entrada, decimal esperado)
        => Assert.Equal(esperado, PedidoCalculos.RedondearA10Centimos(entrada));

    // ---------------- Estado de pago ----------------
    [Theory]
    [InlineData(0, 50, "PENDIENTE")]
    [InlineData(20, 50, "PARCIAL")]
    [InlineData(50, 50, "PAGADO")]
    [InlineData(60, 50, "PAGADO")]   // pagó de más (no debería pasar, pero no queda PARCIAL)
    public void DeterminarEstadoPago_segunMontoVsTotal(decimal pagado, decimal total, string esperado)
        => Assert.Equal(esperado, PedidoCalculos.DeterminarEstadoPago(pagado, total));

    // ---------------- Puntos ganados ----------------
    [Theory]
    [InlineData(100, 1, 100)]   // 1 punto por sol
    [InlineData(105, 10, 10)]   // floor(105/10)
    [InlineData(9, 10, 0)]      // no alcanza a 1 punto
    [InlineData(100, 0, 0)]     // fidelización deshabilitada (solesPorPunto = 0)
    public void PuntosGanados_pisoDeLaDivision(decimal total, decimal solesPorPunto, int esperado)
        => Assert.Equal(esperado, PedidoCalculos.PuntosGanados(total, solesPorPunto));

    // ---------------- Máquina de estados ----------------
    private static List<AreaLavado> Areas() => new()
    {
        new AreaLavado { Id = 1, Nombre = "Recepcion", Orden = 1, Activa = true },
        new AreaLavado { Id = 2, Nombre = "Lavado", Orden = 2, Activa = true },
        new AreaLavado { Id = 3, Nombre = "Empacado", Orden = 3, Activa = true },
    };

    [Fact]
    public void Flujo_pendienteSinArea_entraAlaPrimeraArea()
    {
        var p = new Pedido { EstadoProceso = "PENDIENTE", AreaActualId = null, Modalidad = "Tienda" };
        var paso = PedidoCalculos.CalcularSiguientePaso(p, Areas());
        Assert.Equal("EN_PROCESO", paso.Estado);
        Assert.Equal(1, paso.AreaId);
    }

    [Fact]
    public void Flujo_enProcesoAreaIntermedia_avanzaAlaSiguiente()
    {
        var p = new Pedido { EstadoProceso = "EN_PROCESO", AreaActualId = 1, Modalidad = "Tienda" };
        var paso = PedidoCalculos.CalcularSiguientePaso(p, Areas());
        Assert.Equal("EN_PROCESO", paso.Estado);
        Assert.Equal(2, paso.AreaId);
    }

    [Fact]
    public void Flujo_enUltimaArea_pasaAListo()
    {
        var p = new Pedido { EstadoProceso = "EN_PROCESO", AreaActualId = 3, Modalidad = "Tienda" };
        var paso = PedidoCalculos.CalcularSiguientePaso(p, Areas());
        Assert.Equal("LISTO", paso.Estado);
        Assert.Contains("recojo", paso.Nota);
    }

    [Fact]
    public void Flujo_ultimaAreaDelivery_notaDeRuta()
    {
        var p = new Pedido { EstadoProceso = "EN_PROCESO", AreaActualId = 3, Modalidad = "Delivery" };
        var paso = PedidoCalculos.CalcularSiguientePaso(p, Areas());
        Assert.Equal("LISTO", paso.Estado);
        Assert.Contains("ruta", paso.Nota);
    }

    [Fact]
    public void Flujo_listo_pasaAEntregado()
    {
        var p = new Pedido { EstadoProceso = "LISTO", AreaActualId = 3, Modalidad = "Tienda" };
        var paso = PedidoCalculos.CalcularSiguientePaso(p, Areas());
        Assert.Equal("ENTREGADO", paso.Estado);
    }

    [Theory]
    [InlineData("ENTREGADO")]
    [InlineData("DONADO")]
    [InlineData("ANULADO")]
    public void Flujo_estadoFinal_noPermiteAvanzar(string estadoFinal)
    {
        var p = new Pedido { EstadoProceso = estadoFinal, AreaActualId = 3, Modalidad = "Tienda" };
        Assert.Throws<InvalidOperationException>(() => PedidoCalculos.CalcularSiguientePaso(p, Areas()));
    }

    [Fact]
    public void Flujo_enProcesoSinArea_esInconsistente_lanza()
    {
        var p = new Pedido { EstadoProceso = "EN_PROCESO", AreaActualId = null, Modalidad = "Tienda" };
        Assert.Throws<InvalidOperationException>(() => PedidoCalculos.CalcularSiguientePaso(p, Areas()));
    }

    [Fact]
    public void Flujo_areaActualNoPerteneceALaSede_lanza()
    {
        var p = new Pedido { EstadoProceso = "EN_PROCESO", AreaActualId = 999, Modalidad = "Tienda" };
        Assert.Throws<InvalidOperationException>(() => PedidoCalculos.CalcularSiguientePaso(p, Areas()));
    }

    [Fact]
    public void Flujo_sinAreasConfiguradas_lanza()
    {
        var p = new Pedido { EstadoProceso = "PENDIENTE", AreaActualId = null, Modalidad = "Tienda" };
        Assert.Throws<InvalidOperationException>(() => PedidoCalculos.CalcularSiguientePaso(p, new List<AreaLavado>()));
    }

    // ---------------- Validación de contacto ----------------
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Contacto_celularVacio_lanza(string? celular)
        => Assert.Throws<InvalidOperationException>(() => PedidoCalculos.ValidarContacto(celular, null, "Tienda"));

    [Theory]
    [InlineData("12345678")]    // 8 dígitos
    [InlineData("812345678")]   // no empieza en 9
    [InlineData("9123")]        // muy corto
    [InlineData("9123456789")]  // muy largo
    public void Contacto_celularFormatoInvalido_lanza(string celular)
        => Assert.Throws<InvalidOperationException>(() => PedidoCalculos.ValidarContacto(celular, null, "Tienda"));

    [Fact]
    public void Contacto_celularValidoTienda_pasa()
        => PedidoCalculos.ValidarContacto("987654321", null, "Tienda"); // no lanza

    [Fact]
    public void Contacto_recojoSinDireccion_lanza()
        => Assert.Throws<InvalidOperationException>(() => PedidoCalculos.ValidarContacto("987654321", null, "Recojo"));

    [Fact]
    public void Contacto_recojoConDireccion_pasa()
        => PedidoCalculos.ValidarContacto("987654321", "Av. Siempre Viva 742", "Recojo"); // no lanza

    // ---------------- Validación de destino delivery ----------------
    [Fact]
    public void Destino_noDelivery_noValidaNada()
        => PedidoCalculos.ValidarDestinoDelivery("Tienda", null, null, null, null); // no lanza

    [Fact]
    public void Destino_deliverySinCoordenadas_lanza()
        => Assert.Throws<InvalidOperationException>(() =>
            PedidoCalculos.ValidarDestinoDelivery("Delivery", "Av. Larco 456", "Miraflores", null, null));

    [Fact]
    public void Destino_deliveryCoordenadasFueraDeRango_lanza()
        => Assert.Throws<InvalidOperationException>(() =>
            PedidoCalculos.ValidarDestinoDelivery("Delivery", "Av. Larco 456", "Miraflores", 200m, -77m));

    [Fact]
    public void Destino_deliveryCompleto_pasa()
        => PedidoCalculos.ValidarDestinoDelivery("Delivery", "Av. Larco 456", "Miraflores", -12.05m, -77.03m); // no lanza
}
