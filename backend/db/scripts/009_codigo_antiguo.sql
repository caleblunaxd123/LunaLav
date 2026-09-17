-- ----------------------------------------------------------------
-- Codigo de talonario antiguo (para registrar pedidos historicos
-- migrados desde un sistema en papel, con fecha de ingreso manual)
-- ----------------------------------------------------------------
IF COL_LENGTH('dbo.Pedido', 'CodigoAntiguo') IS NULL
BEGIN
    ALTER TABLE dbo.Pedido ADD CodigoAntiguo NVARCHAR(30) NULL;
END
GO
