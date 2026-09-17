-- Agrega codigo de cupon opcional a Promocion, para que el cliente lo indique al registrar el pedido
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Promocion') AND name = 'Codigo'
)
    ALTER TABLE dbo.Promocion ADD Codigo NVARCHAR(30) NULL;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE name = 'UX_Promocion_Codigo'
)
    CREATE UNIQUE INDEX UX_Promocion_Codigo ON dbo.Promocion(Codigo) WHERE Codigo IS NOT NULL;
GO
