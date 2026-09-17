-- 043: Presentación/contenido por unidad en Insumo.
-- Permite especificar el contenido de cada unidad de stock, ej. bidón (unidad) x 20 litros
-- (ContenidoValor = 20, ContenidoUnidad = 'litros'). Ambos opcionales: si no aplica, quedan NULL.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Insumo') AND name = 'ContenidoValor')
    ALTER TABLE dbo.Insumo ADD ContenidoValor DECIMAL(12, 2) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Insumo') AND name = 'ContenidoUnidad')
    ALTER TABLE dbo.Insumo ADD ContenidoUnidad NVARCHAR(20) NULL;
GO

PRINT 'OK 043: columnas ContenidoValor y ContenidoUnidad agregadas a Insumo.';
GO
