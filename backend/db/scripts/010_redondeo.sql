-- ----------------------------------------------------------------
-- Redondeo del total a los 10 centimos mas cercanos (convencion de
-- efectivo en Peru, ya que no circulan monedas de 1, 2 y 5 centimos)
-- ----------------------------------------------------------------
IF COL_LENGTH('dbo.Pedido', 'Redondeo') IS NULL
BEGIN
    ALTER TABLE dbo.Pedido ADD Redondeo DECIMAL(10,2) NOT NULL DEFAULT 0;
END
GO
