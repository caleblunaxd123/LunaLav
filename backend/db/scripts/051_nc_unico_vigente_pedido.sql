SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- 051: El índice de "un comprobante vigente por pedido" debe aplicar solo a
-- comprobantes de venta (BOLETA/FACTURA). Una NOTA_CREDITO referencia
-- legítimamente el mismo pedido mientras la boleta/factura original sigue vigente.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ComprobanteElectronico') AND name = 'UX_Comprobante_UnicoVigentePedido')
    DROP INDEX UX_Comprobante_UnicoVigentePedido ON dbo.ComprobanteElectronico;
GO

CREATE UNIQUE INDEX UX_Comprobante_UnicoVigentePedido
    ON dbo.ComprobanteElectronico(PedidoId)
    WHERE Estado IN ('PENDIENTE', 'ACEPTADO', 'ERROR') AND Tipo IN ('BOLETA', 'FACTURA');
GO

-- El CHECK de Tipo debe admitir NOTA_CREDITO (catálogo 07 SUNAT).
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Comprobante_Tipo')
    ALTER TABLE dbo.ComprobanteElectronico DROP CONSTRAINT CK_Comprobante_Tipo;
GO
ALTER TABLE dbo.ComprobanteElectronico ADD CONSTRAINT CK_Comprobante_Tipo
    CHECK (Tipo IN ('BOLETA', 'FACTURA', 'NOTA_CREDITO'));
GO
