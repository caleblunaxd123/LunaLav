-- 057: Marca de régimen "solo boletas" (NRUS/RUS). Cuando está activa, el negocio NO puede
-- emitir Factura ni Liquidación (SUNAT no lo permite en el Nuevo RUS). El sistema oculta y
-- bloquea la Factura con un mensaje claro en vez de dejar que APISUNAT/SUNAT la rechace.
-- Re-ejecutable.
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'SoloBoletas') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD SoloBoletas BIT NOT NULL CONSTRAINT DF_ConfigFact_SoloBoletas DEFAULT 0;
GO

PRINT 'OK 057: ConfiguracionFacturacion.SoloBoletas agregado.';
