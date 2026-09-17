-- 044: Cobranza del propietario del SaaS.
--  * PagoSuscripcion: historial de pagos mensuales que cada empresa (tenant) le hace al dueño.
--  * ConfiguracionPlataforma: datos del dueño para cobrar (Yape) y para recordatorios/recibos.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.PagoSuscripcion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PagoSuscripcion (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        NegocioId     INT NOT NULL,
        Fecha         DATE NOT NULL CONSTRAINT DF_PagoSuscripcion_Fecha DEFAULT (CAST(GETDATE() AS DATE)),
        Monto         DECIMAL(12,2) NOT NULL,
        Metodo        NVARCHAR(20) NOT NULL CONSTRAINT DF_PagoSuscripcion_Metodo DEFAULT ('YAPE'),
        PeriodoDesde  DATE NULL,
        PeriodoHasta  DATE NULL,
        Nota          NVARCHAR(300) NULL,
        RegistradoPorUsuarioId INT NULL,
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_PagoSuscripcion_FechaCreacion DEFAULT (SYSDATETIME()),
        CONSTRAINT FK_PagoSuscripcion_Negocio FOREIGN KEY (NegocioId) REFERENCES dbo.Negocio(Id)
    );
    CREATE INDEX IX_PagoSuscripcion_Negocio ON dbo.PagoSuscripcion (NegocioId, Fecha DESC);
    PRINT 'OK 044: tabla PagoSuscripcion creada.';
END
GO

IF OBJECT_ID('dbo.ConfiguracionPlataforma', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ConfiguracionPlataforma (
        Id               INT NOT NULL PRIMARY KEY,   -- siempre 1 (fila única)
        NombrePlataforma NVARCHAR(100) NOT NULL CONSTRAINT DF_ConfigPlat_Nombre DEFAULT ('LunaLav'),
        YapeNombre       NVARCHAR(100) NULL,
        YapeNumero       NVARCHAR(20) NULL,
        ContactoSoporte  NVARCHAR(100) NULL,
        DiasAvisoCobro   INT NOT NULL CONSTRAINT DF_ConfigPlat_Dias DEFAULT (3)
    );
    INSERT INTO dbo.ConfiguracionPlataforma (Id, NombrePlataforma) VALUES (1, 'LunaLav');
    PRINT 'OK 044: tabla ConfiguracionPlataforma creada con fila única.';
END
GO
