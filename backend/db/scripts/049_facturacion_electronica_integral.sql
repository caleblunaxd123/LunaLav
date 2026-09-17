-- 049: cierre integral de facturacion electronica.
-- Agrega trazabilidad del proveedor, snapshots inmutables, detalle, intentos y anulaciones.
-- El script es re-ejecutable y conserva los comprobantes historicos.
SET QUOTED_IDENTIFIER ON;
GO

-- Configuracion fiscal y credenciales por tenant.
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'Proveedor') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD Proveedor NVARCHAR(20) NOT NULL
        CONSTRAINT DF_ConfigFact_Proveedor DEFAULT 'SUNAT_DIRECTO';
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'ApiSunatPersonaId') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD ApiSunatPersonaId NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'ApiSunatTokenCifrado') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD ApiSunatTokenCifrado NVARCHAR(1000) NULL;
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'DireccionFiscal') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD DireccionFiscal NVARCHAR(250) NULL;
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'Ubigeo') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD Ubigeo NVARCHAR(6) NULL;
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'CodigoEstablecimiento') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD CodigoEstablecimiento NVARCHAR(4) NOT NULL
        CONSTRAINT DF_ConfigFact_CodigoEstablecimiento DEFAULT '0000';
IF COL_LENGTH('dbo.ConfiguracionFacturacion', 'EmailEmisor') IS NULL
    ALTER TABLE dbo.ConfiguracionFacturacion ADD EmailEmisor NVARCHAR(150) NULL;
GO

-- Snapshot tributario del encabezado. Nunca debe depender de configuracion o pedido actuales.
IF COL_LENGTH('dbo.ComprobanteElectronico', 'Proveedor') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD Proveedor NVARCHAR(20) NOT NULL
        CONSTRAINT DF_Comprobante_Proveedor DEFAULT 'SUNAT_DIRECTO';
IF COL_LENGTH('dbo.ComprobanteElectronico', 'Ambiente') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD Ambiente NVARCHAR(20) NOT NULL
        CONSTRAINT DF_Comprobante_Ambiente DEFAULT 'BETA';
IF COL_LENGTH('dbo.ComprobanteElectronico', 'ExternalId') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD ExternalId NVARCHAR(150) NULL;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'RucEmisor') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD RucEmisor NVARCHAR(11) NULL;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'RazonSocialEmisor') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD RazonSocialEmisor NVARCHAR(150) NULL;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'DireccionFiscalEmisor') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD DireccionFiscalEmisor NVARCHAR(250) NULL;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'UbigeoEmisor') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD UbigeoEmisor NVARCHAR(6) NULL;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'CodigoEstablecimientoEmisor') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD CodigoEstablecimientoEmisor NVARCHAR(4) NULL;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'Moneda') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD Moneda NVARCHAR(3) NOT NULL
        CONSTRAINT DF_Comprobante_Moneda DEFAULT 'PEN';
IF COL_LENGTH('dbo.ComprobanteElectronico', 'IgvPorcentaje') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD IgvPorcentaje DECIMAL(5,2) NOT NULL
        CONSTRAINT DF_Comprobante_IgvPorcentaje DEFAULT 18;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'Subtotal') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD Subtotal DECIMAL(12,2) NOT NULL
        CONSTRAINT DF_Comprobante_Subtotal DEFAULT 0;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'Descuento') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD Descuento DECIMAL(12,2) NOT NULL
        CONSTRAINT DF_Comprobante_Descuento DEFAULT 0;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'Recargo') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD Recargo DECIMAL(12,2) NOT NULL
        CONSTRAINT DF_Comprobante_Recargo DEFAULT 0;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'Redondeo') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD Redondeo DECIMAL(12,2) NOT NULL
        CONSTRAINT DF_Comprobante_Redondeo DEFAULT 0;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'EsSimulado') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD EsSimulado BIT NOT NULL
        CONSTRAINT DF_Comprobante_EsSimulado DEFAULT 0;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'FechaActualizacion') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD FechaActualizacion DATETIME2 NOT NULL
        CONSTRAINT DF_Comprobante_FechaActualizacion DEFAULT SYSDATETIME();
IF COL_LENGTH('dbo.ComprobanteElectronico', 'FechaRespuesta') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD FechaRespuesta DATETIME2 NULL;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'EstadoAnulacion') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD EstadoAnulacion NVARCHAR(20) NULL;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'MotivoAnulacion') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD MotivoAnulacion NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'FechaSolicitudAnulacion') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD FechaSolicitudAnulacion DATETIME2 NULL;
IF COL_LENGTH('dbo.ComprobanteElectronico', 'FechaAnulacion') IS NULL
    ALTER TABLE dbo.ComprobanteElectronico ADD FechaAnulacion DATETIME2 NULL;
GO

UPDATE c
   SET Proveedor = ISNULL(cf.Proveedor, 'SUNAT_DIRECTO'),
       Ambiente = ISNULL(cf.Ambiente, 'BETA'),
       RucEmisor = ISNULL(c.RucEmisor, cf.RucEmisor),
       RazonSocialEmisor = ISNULL(c.RazonSocialEmisor, cf.RazonSocial),
       DireccionFiscalEmisor = ISNULL(c.DireccionFiscalEmisor, cf.DireccionFiscal),
       UbigeoEmisor = ISNULL(c.UbigeoEmisor, cf.Ubigeo),
       CodigoEstablecimientoEmisor = ISNULL(c.CodigoEstablecimientoEmisor, cf.CodigoEstablecimiento),
       Subtotal = CASE WHEN c.Subtotal = 0 THEN p.Subtotal ELSE c.Subtotal END,
       Descuento = CASE WHEN c.Descuento = 0 THEN p.Descuento ELSE c.Descuento END,
       Recargo = CASE WHEN c.Recargo = 0 THEN p.RecargoUrgente ELSE c.Recargo END,
       Redondeo = CASE WHEN c.Redondeo = 0 THEN p.Redondeo ELSE c.Redondeo END
  FROM dbo.ComprobanteElectronico c
  INNER JOIN dbo.Pedido p ON p.Id = c.PedidoId
  LEFT JOIN dbo.ConfiguracionFacturacion cf ON cf.NegocioId = c.NegocioId;
GO

IF OBJECT_ID('dbo.ComprobanteElectronicoDetalle', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ComprobanteElectronicoDetalle (
        Id                  INT IDENTITY(1,1) PRIMARY KEY,
        ComprobanteId       INT NOT NULL FOREIGN KEY REFERENCES dbo.ComprobanteElectronico(Id),
        NumeroLinea         INT NOT NULL,
        ServicioId          INT NULL,
        Descripcion         NVARCHAR(500) NOT NULL,
        UnidadMedida        NVARCHAR(3) NOT NULL CONSTRAINT DF_ComprobanteDetalle_Unidad DEFAULT 'ZZ',
        Cantidad            DECIMAL(12,3) NOT NULL,
        PrecioUnitarioIgv   DECIMAL(12,4) NOT NULL,
        ValorVenta          DECIMAL(12,2) NOT NULL,
        Igv                 DECIMAL(12,2) NOT NULL,
        Total               DECIMAL(12,2) NOT NULL,
        CONSTRAINT UX_ComprobanteDetalle_Linea UNIQUE (ComprobanteId, NumeroLinea)
    );
    CREATE INDEX IX_ComprobanteDetalle_Comprobante ON dbo.ComprobanteElectronicoDetalle(ComprobanteId);
END
GO

-- Snapshot inicial para los datos existentes. Los nuevos se insertan atomically con el encabezado.
INSERT INTO dbo.ComprobanteElectronicoDetalle
    (ComprobanteId, NumeroLinea, ServicioId, Descripcion, UnidadMedida, Cantidad,
     PrecioUnitarioIgv, ValorVenta, Igv, Total)
SELECT c.Id,
       ROW_NUMBER() OVER (PARTITION BY c.Id ORDER BY pi.Id),
       pi.ServicioId,
       COALESCE(s.Nombre, pi.Descripcion, 'Servicio de lavanderia'),
       'ZZ', pi.Cantidad, pi.PrecioUnit,
       ROUND(pi.Total / (1 + c.IgvPorcentaje / 100), 2),
       pi.Total - ROUND(pi.Total / (1 + c.IgvPorcentaje / 100), 2),
       pi.Total
  FROM dbo.ComprobanteElectronico c
  INNER JOIN dbo.PedidoItem pi ON pi.PedidoId = c.PedidoId
  LEFT JOIN dbo.Servicio s ON s.Id = pi.ServicioId
 WHERE NOT EXISTS (SELECT 1 FROM dbo.ComprobanteElectronicoDetalle d WHERE d.ComprobanteId = c.Id);
GO

IF OBJECT_ID('dbo.ComprobanteElectronicoIntento', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ComprobanteElectronicoIntento (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        ComprobanteId       INT NOT NULL FOREIGN KEY REFERENCES dbo.ComprobanteElectronico(Id),
        Accion              NVARCHAR(20) NOT NULL,
        Estado              NVARCHAR(20) NOT NULL,
        Codigo              NVARCHAR(50) NULL,
        Descripcion         NVARCHAR(1000) NULL,
        Fecha               DATETIME2 NOT NULL CONSTRAINT DF_ComprobanteIntento_Fecha DEFAULT SYSDATETIME(),
        UsuarioId           INT NULL FOREIGN KEY REFERENCES dbo.Usuario(Id)
    );
    CREATE INDEX IX_ComprobanteIntento_Comprobante ON dbo.ComprobanteElectronicoIntento(ComprobanteId, Fecha DESC);
END
GO

-- Los registros poblados como ACEPTADO sin evidencia no deben mezclarse con emisiones reales.
UPDATE dbo.ComprobanteElectronico
   SET EsSimulado = 1, Estado = 'SIMULADO',
       DescripcionRespuestaSunat = COALESCE(DescripcionRespuestaSunat, 'Dato de demostracion sin XML/CDR.')
 WHERE Estado = 'ACEPTADO' AND ExternalId IS NULL AND XmlFirmado IS NULL AND CdrZip IS NULL AND FechaEnvio IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ComprobanteElectronico') AND name = 'UX_Comprobante_ExternalId')
    CREATE UNIQUE INDEX UX_Comprobante_ExternalId
        ON dbo.ComprobanteElectronico(Proveedor, ExternalId)
        WHERE ExternalId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ComprobanteElectronico') AND name = 'IX_Comprobante_Pendientes')
    CREATE INDEX IX_Comprobante_Pendientes
        ON dbo.ComprobanteElectronico(Estado, FechaActualizacion)
        INCLUDE (NegocioId, SedeId, Proveedor, ExternalId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ComprobanteElectronico') AND name = 'UX_Comprobante_UnicoVigentePedido')
    CREATE UNIQUE INDEX UX_Comprobante_UnicoVigentePedido
        ON dbo.ComprobanteElectronico(PedidoId)
        WHERE Estado IN ('PENDIENTE', 'ACEPTADO', 'ERROR');
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ConfigFacturacion_Proveedor')
    ALTER TABLE dbo.ConfiguracionFacturacion ADD CONSTRAINT CK_ConfigFacturacion_Proveedor
        CHECK (Proveedor IN ('APISUNAT', 'SUNAT_DIRECTO'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ConfigFacturacion_Ambiente')
    ALTER TABLE dbo.ConfiguracionFacturacion ADD CONSTRAINT CK_ConfigFacturacion_Ambiente
        CHECK (Ambiente IN ('BETA', 'PRODUCCION'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Comprobante_Tipo')
    ALTER TABLE dbo.ComprobanteElectronico ADD CONSTRAINT CK_Comprobante_Tipo
        CHECK (Tipo IN ('BOLETA', 'FACTURA'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Comprobante_Estado')
    ALTER TABLE dbo.ComprobanteElectronico ADD CONSTRAINT CK_Comprobante_Estado
        CHECK (Estado IN ('PENDIENTE', 'ACEPTADO', 'RECHAZADO', 'ANULADO', 'ERROR', 'SIMULADO'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Comprobante_Importes')
    ALTER TABLE dbo.ComprobanteElectronico ADD CONSTRAINT CK_Comprobante_Importes
        CHECK (Subtotal >= 0 AND Descuento >= 0 AND Recargo >= 0 AND Total >= 0 AND Igv >= 0);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ComprobanteDetalle_Importes')
    ALTER TABLE dbo.ComprobanteElectronicoDetalle ADD CONSTRAINT CK_ComprobanteDetalle_Importes
        CHECK (Cantidad > 0 AND PrecioUnitarioIgv >= 0 AND ValorVenta >= 0 AND Igv >= 0 AND Total >= 0);
GO

PRINT 'OK 049: facturacion electronica integral preparada.';
GO
