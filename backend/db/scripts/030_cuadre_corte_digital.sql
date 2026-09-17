-- Cuadre de caja: separar flujo físico (efectivo) del digital + corte parcial.
-- Nuevas columnas:
--   Corte           = monto de efectivo que se entrega/retira al cierre.
--   IngresosDigital = pagos por transferencia móvil (Yape + Plin + Transferencia).
--   IngresosTarjeta = pagos por POS / tarjeta.
--   Nota            = observación libre del cuadre (se muestra en el reporte).
-- CajaFinal pasa a significar el REMANENTE físico = TotalContado - Corte
--   (lo que queda en caja y sugiere la caja inicial del día siguiente).
-- Gastos pasa a registrar solo los gastos EN EFECTIVO (los que sí salen del cajón);
--   los gastos digitales ya no descuentan del efectivo físico.
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.CuadreCaja', 'Corte') IS NULL
    ALTER TABLE dbo.CuadreCaja ADD Corte DECIMAL(12,2) NOT NULL CONSTRAINT DF_CuadreCaja_Corte DEFAULT 0;
GO

IF COL_LENGTH('dbo.CuadreCaja', 'IngresosDigital') IS NULL
    ALTER TABLE dbo.CuadreCaja ADD IngresosDigital DECIMAL(12,2) NOT NULL CONSTRAINT DF_CuadreCaja_IngDig DEFAULT 0;
GO

IF COL_LENGTH('dbo.CuadreCaja', 'IngresosTarjeta') IS NULL
    ALTER TABLE dbo.CuadreCaja ADD IngresosTarjeta DECIMAL(12,2) NOT NULL CONSTRAINT DF_CuadreCaja_IngTar DEFAULT 0;
GO

IF COL_LENGTH('dbo.CuadreCaja', 'Nota') IS NULL
    ALTER TABLE dbo.CuadreCaja ADD Nota NVARCHAR(300) NULL;
GO
