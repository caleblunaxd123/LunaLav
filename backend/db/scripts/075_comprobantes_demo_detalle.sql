-- ============================================================
-- 075: Detalle (líneas de ítems) para los comprobantes demo del plan
-- LunaLav Factura. Sin esto, la boleta/factura simulada se imprime con la
-- tabla de ítems vacía. Se toman los ítems reales de los pedidos demo para
-- que la representación impresa se vea completa al presentar el sistema.
-- Idempotente: no duplica líneas si ya existen.
-- ============================================================
USE LunaLav;
GO
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @NegocioId INT = (SELECT Id FROM dbo.Negocio WHERE Slug = 'demo');
DECLARE @SedeId INT = (SELECT TOP 1 Id FROM dbo.Sede WHERE NegocioId = @NegocioId AND Activo = 1 ORDER BY Id);
DECLARE @PedidoBoleta INT = (SELECT Id FROM dbo.Pedido WHERE SedeId = @SedeId AND CodigoAntiguo = 'DEMO-004');
DECLARE @PedidoFactura INT = (SELECT Id FROM dbo.Pedido WHERE SedeId = @SedeId AND CodigoAntiguo = 'DEMO-006');

DECLARE @CompBoleta INT = (SELECT TOP 1 Id FROM dbo.ComprobanteElectronico
    WHERE SedeId = @SedeId AND PedidoId = @PedidoBoleta AND Tipo = 'BOLETA' ORDER BY Id);
DECLARE @CompFactura INT = (SELECT TOP 1 Id FROM dbo.ComprobanteElectronico
    WHERE SedeId = @SedeId AND PedidoId = @PedidoFactura AND Tipo = 'FACTURA' ORDER BY Id);

-- Boleta B001: 2 x Sábanas 2 plazas @ S/ 8.00 (IGV incl.) = S/ 16.00
IF @CompBoleta IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ComprobanteElectronicoDetalle WHERE ComprobanteId = @CompBoleta)
BEGIN
    INSERT dbo.ComprobanteElectronicoDetalle
      (ComprobanteId, NumeroLinea, ServicioId, Descripcion, UnidadMedida, Cantidad, PrecioUnitarioIgv, ValorVenta, Igv, Total)
    VALUES
      (@CompBoleta, 1, NULL, N'Lavado de sábanas 2 plazas', 'NIU', 2, 8.0000, 13.56, 2.44, 16.00);
END

-- Factura F001: 2 x Lavado en seco @ S/ 12.00 (IGV incl.) = S/ 24.00
IF @CompFactura IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.ComprobanteElectronicoDetalle WHERE ComprobanteId = @CompFactura)
BEGIN
    INSERT dbo.ComprobanteElectronicoDetalle
      (ComprobanteId, NumeroLinea, ServicioId, Descripcion, UnidadMedida, Cantidad, PrecioUnitarioIgv, ValorVenta, Igv, Total)
    VALUES
      (@CompFactura, 1, NULL, N'Lavado en seco de prendas', 'ZZ', 2, 12.0000, 20.34, 3.66, 24.00);
END

-- Datos del emisor de ejemplo para que la representación impresa se vea completa
-- (RUC de muestra 20123456789, sin validez: el documento sigue marcado como SIMULADO).
UPDATE dbo.ComprobanteElectronico
SET RucEmisor = N'20123456789',
    DireccionFiscalEmisor = N'Av. Ejemplo 123, Lima - Lima'
WHERE NegocioId = @NegocioId AND EsSimulado = 1 AND (RucEmisor IS NULL OR RucEmisor = N'');
GO
