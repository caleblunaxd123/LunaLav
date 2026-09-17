-- 050: Notas de Crédito electrónicas (SUNAT tipo 07).
-- Amplía el comprobante para poder emitir una NC que referencia a una boleta/factura ya
-- aceptada, guardando el motivo (catálogo 09) y el documento de referencia. Agrega en la
-- configuración las series y correlativos propios de las NC (FC01 para facturas, BC01 para
-- boletas). Re-ejecutable: conserva los datos existentes.
SET QUOTED_IDENTIFIER ON;
GO

-- 'NOTA_CREDITO' (12 chars) no cabe en NVARCHAR(10). Se amplía la columna Tipo.
IF COL_LENGTH('dbo.ComprobanteElectronico', 'Tipo') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ComprobanteElectronico')
                 AND name = 'Tipo' AND max_length < 40)   -- NVARCHAR(10) => max_length 20 bytes
    ALTER TABLE dbo.ComprobanteElectronico ALTER COLUMN Tipo NVARCHAR(20) NOT NULL;
GO

-- Referencia de la NC al comprobante que corrige/anula, y motivo SUNAT (catálogo 09).
IF COL_LENGTH('dbo.ComprobanteElectronico', 'ComprobanteRefId') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD ComprobanteRefId INT NULL;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'DocRefTipo') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD DocRefTipo NVARCHAR(4) NULL;        -- 01 factura / 03 boleta
IF COL_LENGTH('dbo.ComprobanteElectronico', 'DocRefSerieNumero') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD DocRefSerieNumero NVARCHAR(20) NULL; -- p.ej. F001-00000123
IF COL_LENGTH('dbo.ComprobanteElectronico', 'MotivoNotaCodigo') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD MotivoNotaCodigo NVARCHAR(2) NULL;   -- catálogo 09
IF COL_LENGTH('dbo.ComprobanteElectronico', 'MotivoNotaDescripcion') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD MotivoNotaDescripcion NVARCHAR(250) NULL;
GO

-- Series y correlativos independientes de las Notas de Crédito, por tenant.
-- Convención SUNAT: la serie de la NC empieza con F (facturas) o B (boletas).
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'SerieNotaCreditoFactura') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD SerieNotaCreditoFactura NVARCHAR(4) NOT NULL
        CONSTRAINT DF_ConfigFact_SerieNCFactura DEFAULT 'FC01';
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'SerieNotaCreditoBoleta') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD SerieNotaCreditoBoleta NVARCHAR(4) NOT NULL
        CONSTRAINT DF_ConfigFact_SerieNCBoleta DEFAULT 'BC01';
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'CorrelativoNotaCreditoFactura') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD CorrelativoNotaCreditoFactura INT NOT NULL
        CONSTRAINT DF_ConfigFact_CorrNCFactura DEFAULT 0;
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'CorrelativoNotaCreditoBoleta') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD CorrelativoNotaCreditoBoleta INT NOT NULL
        CONSTRAINT DF_ConfigFact_CorrNCBoleta DEFAULT 0;
GO
