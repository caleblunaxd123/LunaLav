-- ================================================================
-- 060_insumo_vencimiento.sql
-- Fecha de vencimiento/caducidad opcional para cada insumo.
-- Permite avisar en la tarjeta cuando un insumo está vencido o por vencer.
-- Idempotente.
-- ================================================================
IF COL_LENGTH('dbo.Insumo', 'FechaVencimiento') IS NULL
    ALTER TABLE dbo.Insumo ADD FechaVencimiento DATE NULL;
GO
