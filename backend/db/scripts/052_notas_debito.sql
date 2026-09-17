-- 052: Notas de Débito (SUNAT 08). Series/correlativos propios y ampliación del CHECK de Tipo.
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'SerieNotaDebitoFactura') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD SerieNotaDebitoFactura NVARCHAR(4) NOT NULL CONSTRAINT DF_Config_SerieNDF DEFAULT 'FD01';
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'SerieNotaDebitoBoleta') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD SerieNotaDebitoBoleta NVARCHAR(4) NOT NULL CONSTRAINT DF_Config_SerieNDB DEFAULT 'BD01';
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'CorrelativoNotaDebitoFactura') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD CorrelativoNotaDebitoFactura INT NOT NULL CONSTRAINT DF_Config_CorrNDF DEFAULT 0;
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'CorrelativoNotaDebitoBoleta') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD CorrelativoNotaDebitoBoleta INT NOT NULL CONSTRAINT DF_Config_CorrNDB DEFAULT 0;
GO

-- El CHECK de Tipo debe admitir NOTA_DEBITO (catálogo 08 SUNAT).
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Comprobante_Tipo')
    ALTER TABLE dbo.ComprobanteElectronico DROP CONSTRAINT CK_Comprobante_Tipo;
GO
ALTER TABLE dbo.ComprobanteElectronico ADD CONSTRAINT CK_Comprobante_Tipo
    CHECK (Tipo IN ('BOLETA', 'FACTURA', 'NOTA_CREDITO', 'NOTA_DEBITO'));
GO
