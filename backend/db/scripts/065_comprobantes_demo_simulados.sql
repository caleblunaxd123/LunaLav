-- ============================================================
-- 065: Comprobantes ficticios para presentar el plan LunaLav Factura.
-- No contienen información personal, no se envían a SUNAT y quedan marcados
-- de forma permanente como SIMULADO / SIN VALIDEZ TRIBUTARIA.
-- ============================================================
USE LunaLav;
GO
SET QUOTED_IDENTIFIER ON;
GO

DECLARE @NegocioId INT = (SELECT Id FROM dbo.Negocio WHERE Slug = 'demo');
DECLARE @SedeId INT = (SELECT TOP 1 Id FROM dbo.Sede WHERE NegocioId = @NegocioId AND Activo = 1 ORDER BY Id);
DECLARE @UsuarioId INT = (SELECT TOP 1 Id FROM dbo.Usuario WHERE NegocioId = @NegocioId AND Activo = 1 ORDER BY Id);
DECLARE @PedidoBoleta INT = (SELECT Id FROM dbo.Pedido WHERE SedeId = @SedeId AND CodigoAntiguo = 'DEMO-004');
DECLARE @PedidoFactura INT = (SELECT Id FROM dbo.Pedido WHERE SedeId = @SedeId AND CodigoAntiguo = 'DEMO-006');

IF @NegocioId IS NULL OR @SedeId IS NULL OR @UsuarioId IS NULL OR @PedidoBoleta IS NULL OR @PedidoFactura IS NULL
    THROW 51001, 'No se encontro el escenario de demo requerido para los comprobantes simulados.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.ComprobanteElectronico WHERE SedeId = @SedeId AND PedidoId = @PedidoBoleta)
BEGIN
    INSERT dbo.ComprobanteElectronico
      (NegocioId,SedeId,PedidoId,Tipo,Serie,Correlativo,ClienteNombre,ClienteTipoDoc,ClienteNumDoc,
       OpGravada,Igv,Total,Estado,CodigoRespuestaSunat,DescripcionRespuestaSunat,UsuarioId,Proveedor,Ambiente,
       RazonSocialEmisor,Moneda,IgvPorcentaje,Subtotal,Descuento,Recargo,Redondeo,EsSimulado,FechaEmision,FechaActualizacion)
    VALUES
      (@NegocioId,@SedeId,@PedidoBoleta,'BOLETA','B001',1,N'Jorge Salazar (Demo)','SIN_DOC',NULL,
       13.56,2.44,16.00,'SIMULADO','0',N'Documento de prueba — no enviado a SUNAT.',@UsuarioId,'SUNAT_DIRECTO','BETA',
       N'Lavandería Demo LunaLav','PEN',18,16.00,0,0,0,1,DATEADD(day,-1,SYSDATETIME()),SYSDATETIME());
END

IF NOT EXISTS (SELECT 1 FROM dbo.ComprobanteElectronico WHERE SedeId = @SedeId AND PedidoId = @PedidoFactura)
BEGIN
    INSERT dbo.ComprobanteElectronico
      (NegocioId,SedeId,PedidoId,Tipo,Serie,Correlativo,ClienteNombre,ClienteTipoDoc,ClienteNumDoc,
       OpGravada,Igv,Total,Estado,CodigoRespuestaSunat,DescripcionRespuestaSunat,UsuarioId,Proveedor,Ambiente,
       RazonSocialEmisor,Moneda,IgvPorcentaje,Subtotal,Descuento,Recargo,Redondeo,EsSimulado,FechaEmision,FechaActualizacion)
    VALUES
      (@NegocioId,@SedeId,@PedidoFactura,'FACTURA','F001',1,N'Empresa Ejemplo S.A.C. (Demo)','RUC',N'RUC DEMO 001',
       20.34,3.66,24.00,'SIMULADO','0',N'Documento de prueba — no enviado a SUNAT.',@UsuarioId,'SUNAT_DIRECTO','BETA',
       N'Lavandería Demo LunaLav','PEN',18,24.00,0,0,0,1,DATEADD(day,-3,SYSDATETIME()),SYSDATETIME());
END
GO
