-- ============================================================
-- 076: Cobro recurrente de la mensualidad con Culqi (pago en la web).
-- La tarjeta la tokeniza el checkout de Culqi en el navegador: aquí solo se guardan
-- los identificadores de Culqi y los últimos 4 dígitos, nunca el número de la tarjeta.
-- Idempotente: se puede ejecutar varias veces.
-- ============================================================
USE LunaLav;
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Un plan de Culqi por monto mensual (se crea una vez y se reutiliza).
IF OBJECT_ID('dbo.CulqiPlan') IS NULL
BEGIN
    CREATE TABLE dbo.CulqiPlan (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        MontoCentimos INT NOT NULL,
        Moneda        NVARCHAR(3) NOT NULL CONSTRAINT DF_CulqiPlan_Moneda DEFAULT N'PEN',
        CulqiPlanId   NVARCHAR(40) NOT NULL,
        Modo          NVARCHAR(10) NOT NULL, -- TEST / LIVE (los ids de prueba no sirven en producción)
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_CulqiPlan_Fecha DEFAULT SYSDATETIME(),
        CONSTRAINT UQ_CulqiPlan UNIQUE (MontoCentimos, Moneda, Modo)
    );
END
GO

-- Pago automático de cada empresa.
IF OBJECT_ID('dbo.SuscripcionCulqi') IS NULL
BEGIN
    CREATE TABLE dbo.SuscripcionCulqi (
        NegocioId          INT NOT NULL PRIMARY KEY REFERENCES dbo.Negocio(Id),
        CustomerId         NVARCHAR(40) NULL,
        CardId             NVARCHAR(40) NULL,
        SubscriptionId     NVARCHAR(40) NULL,
        CulqiPlanId        NVARCHAR(40) NULL,
        Estado             NVARCHAR(20) NOT NULL CONSTRAINT DF_SuscripcionCulqi_Estado DEFAULT N'SIN_TARJETA', -- SIN_TARJETA / ACTIVA / CANCELADA / FALLIDA
        TarjetaMarca       NVARCHAR(30) NULL,
        TarjetaUltimos4    NVARCHAR(4) NULL,
        UltimoError        NVARCHAR(400) NULL,
        Modo               NVARCHAR(10) NULL,
        FechaCreacion      DATETIME2 NOT NULL CONSTRAINT DF_SuscripcionCulqi_Creacion DEFAULT SYSDATETIME(),
        FechaActualizacion DATETIME2 NOT NULL CONSTRAINT DF_SuscripcionCulqi_Actualizacion DEFAULT SYSDATETIME()
    );
END
GO

-- Identificador del cobro en la pasarela (charge_id de Culqi): evita registrar dos veces
-- el mismo cobro si el webhook llega repetido o se sincroniza manualmente.
IF COL_LENGTH('dbo.PagoSuscripcion', 'ReferenciaExterna') IS NULL
    ALTER TABLE dbo.PagoSuscripcion ADD ReferenciaExterna NVARCHAR(60) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PagoSuscripcion_ReferenciaExterna')
    CREATE UNIQUE INDEX UX_PagoSuscripcion_ReferenciaExterna
        ON dbo.PagoSuscripcion(ReferenciaExterna) WHERE ReferenciaExterna IS NOT NULL;
GO
