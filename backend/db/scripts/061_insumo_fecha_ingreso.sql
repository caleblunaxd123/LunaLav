-- ================================================================
-- 061_insumo_fecha_ingreso.sql
-- Fecha de ingreso/registro del insumo al inventario (opcional).
-- Editable desde el formulario de crear/editar insumo.
-- Idempotente.
-- ================================================================
IF COL_LENGTH('dbo.Insumo', 'FechaIngreso') IS NULL
    ALTER TABLE dbo.Insumo ADD FechaIngreso DATE NULL;
GO
