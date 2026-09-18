-- Favorito por insumo y sede: permite priorizar los productos de uso frecuente.
-- Idempotente para instalaciones nuevas y ya existentes.
IF COL_LENGTH('dbo.Insumo', 'Favorito') IS NULL
    ALTER TABLE dbo.Insumo ADD Favorito BIT NOT NULL CONSTRAINT DF_Insumo_Favorito DEFAULT 0;
GO
