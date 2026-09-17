-- 031: Cierra el ciclo de fidelización (canje de puntos) y agrega control de descuento.
--  - ConfiguracionNegocio.ValorPuntoCanje: cuánto vale 1 punto en soles al canjearlo (0 = canje deshabilitado).
--  - ConfiguracionNegocio.MaxDescuentoPct: tope de descuento manual que puede aplicar el personal (0 = sin tope).
--  - MovimientoPuntos.PedidoId: liga cada movimiento de puntos a su pedido, para poder revertirlos si se anula.

IF COL_LENGTH('dbo.ConfiguracionNegocio', 'ValorPuntoCanje') IS NULL
    ALTER TABLE dbo.ConfiguracionNegocio ADD ValorPuntoCanje DECIMAL(10,2) NOT NULL CONSTRAINT DF_ConfigNeg_ValorPuntoCanje DEFAULT 0;

IF COL_LENGTH('dbo.ConfiguracionNegocio', 'MaxDescuentoPct') IS NULL
    ALTER TABLE dbo.ConfiguracionNegocio ADD MaxDescuentoPct DECIMAL(5,2) NOT NULL CONSTRAINT DF_ConfigNeg_MaxDescuentoPct DEFAULT 0;

IF COL_LENGTH('dbo.MovimientoPuntos', 'PedidoId') IS NULL
    ALTER TABLE dbo.MovimientoPuntos ADD PedidoId INT NULL;
GO

SET QUOTED_IDENTIFIER ON;  -- requerido para crear índices filtrados
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_MovimientoPuntos_Pedido')
    CREATE INDEX IX_MovimientoPuntos_Pedido ON dbo.MovimientoPuntos(PedidoId) WHERE PedidoId IS NOT NULL;
GO
