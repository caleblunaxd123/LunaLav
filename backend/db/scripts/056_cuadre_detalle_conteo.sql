-- 056: Guarda el desglose del conteo de efectivo (billete por billete) del cuadre de caja.
-- Antes solo se guardaba el TOTAL contado, así que al reabrir un cierre en modo "billete por
-- billete" la tabla salía en 0 y parecía que el conteo se había borrado. Con esta columna el
-- cierre queda grabado con su detalle y se muestra igual cualquier día. Re-ejecutable.
IF COL_LENGTH('dbo.CuadreCaja', 'DetalleConteo') IS NULL
    ALTER TABLE dbo.CuadreCaja ADD DetalleConteo NVARCHAR(500) NULL;
GO

PRINT 'OK 056: CuadreCaja.DetalleConteo agregado.';
