-- ============================================================
-- 078: Enlaces de pago de la suscripción (correo y WhatsApp) y registro de
-- recordatorios automáticos. Solo se guarda el hash del token. Idempotente.
-- ============================================================
USE LunaLav;
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.EnlacePago') IS NULL
BEGIN
    CREATE TABLE dbo.EnlacePago (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        NegocioId     INT NOT NULL REFERENCES dbo.Negocio(Id),
        TokenHash     NVARCHAR(64) NOT NULL,
        Expira        DATETIME2 NOT NULL,
        Revocado      BIT NOT NULL CONSTRAINT DF_EnlacePago_Revocado DEFAULT 0,
        Canal         NVARCHAR(20) NOT NULL,           -- CORREO / WHATSAPP / MANUAL
        UltimoUso     DATETIME2 NULL,
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_EnlacePago_Fecha DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_EnlacePago_TokenHash ON dbo.EnlacePago(TokenHash);
    CREATE INDEX IX_EnlacePago_Negocio ON dbo.EnlacePago(NegocioId, FechaCreacion DESC);
END
GO

-- Un recordatorio por empresa, vencimiento y etapa (5 días antes, 1 día antes, el día, vencida).
IF OBJECT_ID('dbo.RecordatorioPago') IS NULL
BEGIN
    CREATE TABLE dbo.RecordatorioPago (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        NegocioId     INT NOT NULL REFERENCES dbo.Negocio(Id),
        Vencimiento   DATE NOT NULL,
        Etapa         NVARCHAR(10) NOT NULL,           -- D5 / D1 / D0 / VENCIDA
        Enviado       BIT NOT NULL,
        Detalle       NVARCHAR(300) NULL,
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_RecordatorioPago_Fecha DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_RecordatorioPago UNIQUE (NegocioId, Vencimiento, Etapa)
    );
END
GO
