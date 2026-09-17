-- 053: Guía de Remisión Remitente electrónica (GRE, SUNAT 09).
-- Documento sin IGV/totales: lleva datos de traslado (origen/destino, motivo, peso, transporte).
-- Reusa dbo.ComprobanteElectronico (Tipo='GUIA_REMISION') + tabla 1:1 con los datos de traslado.

-- Serie/correlativo propios de guía en la configuración.
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'SerieGuiaRemision') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD SerieGuiaRemision NVARCHAR(4) NOT NULL CONSTRAINT DF_Config_SerieGRE DEFAULT 'T001';
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'CorrelativoGuiaRemision') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD CorrelativoGuiaRemision INT NOT NULL CONSTRAINT DF_Config_CorrGRE DEFAULT 0;
GO

-- El CHECK de Tipo debe admitir GUIA_REMISION.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Comprobante_Tipo')
    ALTER TABLE dbo.ComprobanteElectronico DROP CONSTRAINT CK_Comprobante_Tipo;
GO
ALTER TABLE dbo.ComprobanteElectronico ADD CONSTRAINT CK_Comprobante_Tipo
    CHECK (Tipo IN ('BOLETA', 'FACTURA', 'NOTA_CREDITO', 'NOTA_DEBITO', 'GUIA_REMISION'));
GO

-- Datos de traslado (1:1 con el comprobante tipo guía).
IF OBJECT_ID('dbo.GuiaRemisionDatos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GuiaRemisionDatos (
        ComprobanteId INT NOT NULL PRIMARY KEY
            CONSTRAINT FK_GuiaDatos_Comprobante REFERENCES dbo.ComprobanteElectronico(Id),
        MotivoTrasladoCodigo NVARCHAR(2) NOT NULL,          -- catálogo 20
        MotivoTrasladoDescripcion NVARCHAR(250) NULL,
        PesoBrutoTotal DECIMAL(12,3) NOT NULL,
        UnidadPeso NVARCHAR(3) NOT NULL DEFAULT 'KGM',
        NumeroBultos INT NULL,
        FechaInicioTraslado DATE NOT NULL,
        ModalidadTransporte NVARCHAR(2) NOT NULL,           -- 01 público / 02 privado
        PartidaUbigeo NVARCHAR(6) NOT NULL,
        PartidaDireccion NVARCHAR(250) NOT NULL,
        LlegadaUbigeo NVARCHAR(6) NOT NULL,
        LlegadaDireccion NVARCHAR(250) NOT NULL,
        -- Transporte público (01): transportista
        TransportistaNumDoc NVARCHAR(15) NULL,
        TransportistaRazonSocial NVARCHAR(250) NULL,
        -- Transporte privado (02): vehículo + conductor
        VehiculoPlaca NVARCHAR(10) NULL,
        ConductorTipoDoc NVARCHAR(1) NULL,
        ConductorNumDoc NVARCHAR(15) NULL,
        ConductorNombres NVARCHAR(250) NULL,
        ConductorLicencia NVARCHAR(20) NULL
    );
END
GO
