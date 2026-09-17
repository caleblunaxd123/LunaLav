SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM dbo.ComprobanteElectronico WHERE EsSimulado=0 AND ABS(OpGravada + Igv + Redondeo - Total) > 0.02)
    THROW 51000, 'Facturacion: existen encabezados cuyos totales no cuadran.', 1;

IF EXISTS (SELECT 1 FROM dbo.ComprobanteElectronico WHERE Tipo='FACTURA' AND EsSimulado=0
           AND (ClienteTipoDoc<>'RUC' OR LEN(ISNULL(ClienteNumDoc,''))<>11))
    THROW 51001, 'Facturacion: existe una factura real sin RUC valido.', 1;

IF EXISTS (SELECT 1 FROM dbo.ComprobanteElectronico WHERE Estado='ACEPTADO' AND EsSimulado=0
           AND Proveedor='APISUNAT' AND (ExternalId IS NULL OR FechaRespuesta IS NULL))
    THROW 51002, 'Facturacion: existe un APISUNAT aceptado sin trazabilidad externa.', 1;

IF EXISTS (SELECT PedidoId FROM dbo.ComprobanteElectronico WHERE Estado IN ('PENDIENTE','ACEPTADO','ERROR')
           GROUP BY PedidoId HAVING COUNT(*) > 1)
    THROW 51003, 'Facturacion: un pedido tiene mas de un comprobante vigente.', 1;

IF EXISTS (SELECT 1 FROM dbo.ComprobanteElectronico c WHERE NOT EXISTS
           (SELECT 1 FROM dbo.ComprobanteElectronicoDetalle d WHERE d.ComprobanteId=c.Id))
    THROW 51004, 'Facturacion: existe un comprobante sin snapshot de detalle.', 1;

PRINT 'OK: integridad de facturacion electronica.';
