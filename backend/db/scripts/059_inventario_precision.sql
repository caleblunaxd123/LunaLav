-- ================================================================
-- 059_inventario_precision.sql
-- Aumenta la precisión del inventario de 2 a 3 decimales.
-- Necesario para el modo "Medición por peso": al convertir el peso
-- (Kg/Lt) a la unidad base (ej. Bidón) aparecen 3 decimales
-- (ej. 10.9 Kg / 20 = 0.545 Bidón). Con solo 2 decimales el stock
-- se redondeaba y no reflejaba el peso exacto.
-- Es un ensanchamiento de columna: no pierde datos existentes.
-- Idempotente en la práctica (re-ejecutar deja el mismo tipo).
-- ================================================================

ALTER TABLE dbo.Insumo           ALTER COLUMN StockActual DECIMAL(12,3) NOT NULL;
GO
ALTER TABLE dbo.Insumo           ALTER COLUMN StockMinimo DECIMAL(12,3) NOT NULL;
GO
ALTER TABLE dbo.MovimientoInsumo ALTER COLUMN Cantidad    DECIMAL(12,3) NOT NULL;
GO
