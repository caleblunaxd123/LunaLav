-- Permitir un cuadre por (Fecha, Usuario) en vez de uno por Fecha.
-- Cada colaborador que trabaja ese dia hace su propio cuadre.
IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_CuadreCaja_Fecha_Usuario' AND object_id = OBJECT_ID('dbo.CuadreCaja')
)
    DROP INDEX UQ_CuadreCaja_Fecha_Usuario ON dbo.CuadreCaja;
GO

SET QUOTED_IDENTIFIER ON;
GO

-- Limpiar duplicados historicos (se queda el mas reciente por (Fecha, UsuarioId))
;WITH Dups AS (
    SELECT Id, ROW_NUMBER() OVER (PARTITION BY Fecha, UsuarioId ORDER BY FechaCreacion DESC, Id DESC) AS rn
    FROM dbo.CuadreCaja
)
DELETE FROM dbo.CuadreCaja WHERE Id IN (SELECT Id FROM Dups WHERE rn > 1);
GO

CREATE UNIQUE INDEX UQ_CuadreCaja_Fecha_Usuario ON dbo.CuadreCaja(Fecha, UsuarioId);
GO
