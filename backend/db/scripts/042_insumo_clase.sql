-- 042: Clase de inventario en Insumo.
-- Tres clases: EQUIPO (equipos de trabajo), MATERIAL (materiales y herramientas),
-- INSUMO (insumos consumibles). Lo existente se asume consumible (INSUMO).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Insumo') AND name = 'Clase')
    ALTER TABLE dbo.Insumo
        ADD Clase NVARCHAR(20) NOT NULL CONSTRAINT DF_Insumo_Clase DEFAULT 'INSUMO';
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Insumo_Clase')
    ALTER TABLE dbo.Insumo
        ADD CONSTRAINT CK_Insumo_Clase CHECK (Clase IN ('EQUIPO', 'MATERIAL', 'INSUMO'));
GO

PRINT 'OK 042: columna Clase agregada a Insumo (EQUIPO/MATERIAL/INSUMO).';
GO
