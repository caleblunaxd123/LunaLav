-- ============================================================
-- 077: Código de verificación por correo para el alta autónoma de lavanderías.
-- Solo se guarda el hash del código. Idempotente.
-- ============================================================
USE LunaLav;
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.RegistroVerificacion') IS NULL
BEGIN
    CREATE TABLE dbo.RegistroVerificacion (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        Email         NVARCHAR(150) NOT NULL,
        CodigoHash    NVARCHAR(64) NOT NULL,
        Expira        DATETIME2 NOT NULL,
        Intentos      INT NOT NULL CONSTRAINT DF_RegistroVerificacion_Intentos DEFAULT 0,
        Usado         BIT NOT NULL CONSTRAINT DF_RegistroVerificacion_Usado DEFAULT 0,
        IpOrigen      NVARCHAR(64) NULL,
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_RegistroVerificacion_Fecha DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_RegistroVerificacion_Email ON dbo.RegistroVerificacion(Email, FechaCreacion DESC);
END
GO
