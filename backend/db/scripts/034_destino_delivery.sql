SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.Pedido', 'DireccionEntrega') IS NULL
    ALTER TABLE dbo.Pedido ADD DireccionEntrega NVARCHAR(250) NULL;
IF COL_LENGTH('dbo.Pedido', 'DistritoEntrega') IS NULL
    ALTER TABLE dbo.Pedido ADD DistritoEntrega NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.Pedido', 'ReferenciaEntrega') IS NULL
    ALTER TABLE dbo.Pedido ADD ReferenciaEntrega NVARCHAR(250) NULL;
IF COL_LENGTH('dbo.Pedido', 'LatitudEntrega') IS NULL
    ALTER TABLE dbo.Pedido ADD LatitudEntrega DECIMAL(9,6) NULL;
IF COL_LENGTH('dbo.Pedido', 'LongitudEntrega') IS NULL
    ALTER TABLE dbo.Pedido ADD LongitudEntrega DECIMAL(9,6) NULL;
GO

-- Conserva los pedidos Delivery anteriores. La direccion se toma del cliente y
-- queda explicitamente marcada para confirmacion cuando el dato no existia.
UPDATE p
   SET DireccionEntrega = COALESCE(NULLIF(LTRIM(RTRIM(p.DireccionEntrega)), ''),
                                  NULLIF(LTRIM(RTRIM(c.Direccion)), ''),
                                  'Direccion pendiente de confirmar'),
       DistritoEntrega = COALESCE(NULLIF(LTRIM(RTRIM(p.DistritoEntrega)), ''),
                                 'Por confirmar')
  FROM dbo.Pedido p
  INNER JOIN dbo.Cliente c ON c.Id = p.ClienteId
 WHERE p.Modalidad = 'Delivery';
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Pedido_DeliveryDestino')
BEGIN
    ALTER TABLE dbo.Pedido WITH CHECK ADD CONSTRAINT CK_Pedido_DeliveryDestino CHECK (
        Modalidad <> 'Delivery'
        OR (
            DireccionEntrega IS NOT NULL AND LEN(LTRIM(RTRIM(DireccionEntrega))) > 0
            AND DistritoEntrega IS NOT NULL AND LEN(LTRIM(RTRIM(DistritoEntrega))) > 0
        )
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Pedido_DeliveryCoordenadas')
BEGIN
    ALTER TABLE dbo.Pedido WITH CHECK ADD CONSTRAINT CK_Pedido_DeliveryCoordenadas CHECK (
        (LatitudEntrega IS NULL AND LongitudEntrega IS NULL)
        OR (
            LatitudEntrega BETWEEN -90 AND 90
            AND LongitudEntrega BETWEEN -180 AND 180
        )
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Pedido') AND name = 'IX_Pedido_DistritoEntrega')
    CREATE INDEX IX_Pedido_DistritoEntrega ON dbo.Pedido (DistritoEntrega) WHERE DistritoEntrega IS NOT NULL;
GO
