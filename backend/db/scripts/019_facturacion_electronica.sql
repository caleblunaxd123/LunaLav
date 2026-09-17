-- ============================================================
-- 019: Facturacion Electronica (SUNAT directo, por negocio)
--
-- Cada Negocio (tenant) es un contribuyente distinto ante SUNAT: trae su propio
-- RUC, certificado digital (.pfx) y credenciales SOL. No se comparte una sola
-- cuenta SUNAT entre negocios.
--
-- Es re-ejecutable: cada paso valida si ya se aplico antes de tocar nada.
-- ============================================================
USE LunaLav;
GO

IF OBJECT_ID('dbo.ConfiguracionFacturacion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ConfiguracionFacturacion (
        Id                          INT IDENTITY(1,1) PRIMARY KEY,
        NegocioId                   INT NOT NULL UNIQUE FOREIGN KEY REFERENCES dbo.Negocio(Id),
        RazonSocial                 NVARCHAR(150) NULL,
        RucEmisor                   NVARCHAR(11) NULL,
        Ambiente                    NVARCHAR(20) NOT NULL DEFAULT 'BETA', -- BETA | PRODUCCION
        SolUsuario                  NVARCHAR(50) NULL,
        SolClaveCifrada             NVARCHAR(500) NULL,
        CertificadoPfx              VARBINARY(MAX) NULL,
        CertificadoPasswordCifrada  NVARCHAR(500) NULL,
        SerieBoleta                 NVARCHAR(4) NOT NULL DEFAULT 'B001',
        SerieFactura                NVARCHAR(4) NOT NULL DEFAULT 'F001',
        CorrelativoBoleta           INT NOT NULL DEFAULT 0,
        CorrelativoFactura          INT NOT NULL DEFAULT 0,
        Activo                      BIT NOT NULL DEFAULT 0,
        FechaActualizacion          DATETIME2 NOT NULL DEFAULT SYSDATETIME()
    );
END
GO

IF OBJECT_ID('dbo.ComprobanteElectronico', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ComprobanteElectronico (
        Id                          INT IDENTITY(1,1) PRIMARY KEY,
        NegocioId                   INT NOT NULL FOREIGN KEY REFERENCES dbo.Negocio(Id),
        SedeId                      INT NOT NULL FOREIGN KEY REFERENCES dbo.Sede(Id),
        PedidoId                    INT NOT NULL FOREIGN KEY REFERENCES dbo.Pedido(Id),
        Tipo                        NVARCHAR(10) NOT NULL,             -- BOLETA | FACTURA
        Serie                       NVARCHAR(4) NOT NULL,
        Correlativo                 INT NOT NULL,
        ClienteNombre               NVARCHAR(150) NOT NULL,
        ClienteTipoDoc              NVARCHAR(10) NOT NULL,             -- DNI | RUC | SIN_DOC
        ClienteNumDoc               NVARCHAR(15) NULL,
        OpGravada                   DECIMAL(12,2) NOT NULL,
        Igv                         DECIMAL(12,2) NOT NULL,
        Total                       DECIMAL(12,2) NOT NULL,
        Estado                      NVARCHAR(20) NOT NULL DEFAULT 'PENDIENTE', -- PENDIENTE|ACEPTADO|RECHAZADO|ANULADO|ERROR
        CodigoRespuestaSunat        NVARCHAR(10) NULL,
        DescripcionRespuestaSunat   NVARCHAR(500) NULL,
        XmlFirmado                  VARBINARY(MAX) NULL,
        CdrZip                      VARBINARY(MAX) NULL,
        HashCpe                     NVARCHAR(100) NULL,
        FechaEmision                DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        FechaEnvio                  DATETIME2 NULL,
        UsuarioId                   INT NOT NULL FOREIGN KEY REFERENCES dbo.Usuario(Id)
    );
    CREATE UNIQUE INDEX UX_Comprobante_Serie_Correlativo ON dbo.ComprobanteElectronico(NegocioId, Tipo, Serie, Correlativo);
    CREATE INDEX IX_Comprobante_SedeId ON dbo.ComprobanteElectronico(SedeId);
    CREATE INDEX IX_Comprobante_PedidoId ON dbo.ComprobanteElectronico(PedidoId);
END
GO

