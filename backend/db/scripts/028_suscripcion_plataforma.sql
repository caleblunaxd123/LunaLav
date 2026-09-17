-- Panel del propietario: suscripción/cobro por empresa + último acceso por usuario.
-- Convierte el panel de "administrar empresas" en un SaaS que las cobra y monitorea.

-- ---- Suscripción en Negocio ----
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Negocio') AND name = 'PlanSuscripcion')
    ALTER TABLE dbo.Negocio ADD PlanSuscripcion NVARCHAR(20) NOT NULL DEFAULT 'BASICO';
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Negocio') AND name = 'EstadoSuscripcion')
    -- PRUEBA / ACTIVA / VENCIDA / SUSPENDIDA
    ALTER TABLE dbo.Negocio ADD EstadoSuscripcion NVARCHAR(20) NOT NULL DEFAULT 'ACTIVA';
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Negocio') AND name = 'MontoMensual')
    ALTER TABLE dbo.Negocio ADD MontoMensual DECIMAL(10,2) NOT NULL DEFAULT 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Negocio') AND name = 'ProximoPago')
    ALTER TABLE dbo.Negocio ADD ProximoPago DATE NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Negocio') AND name = 'NotasInternas')
    ALTER TABLE dbo.Negocio ADD NotasInternas NVARCHAR(500) NULL;
GO

-- ---- Último acceso por usuario (para saber quién usa el sistema de verdad) ----
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Usuario') AND name = 'UltimoAcceso')
    ALTER TABLE dbo.Usuario ADD UltimoAcceso DATETIME2 NULL;
GO

-- ---- Datos de demo sensatos para las empresas reales (no el negocio reservado) ----
UPDATE dbo.Negocio
SET PlanSuscripcion = 'PRO',
    EstadoSuscripcion = 'ACTIVA',
    MontoMensual = 99.00,
    ProximoPago = DATEADD(DAY, 20, CAST(GETDATE() AS DATE))
WHERE Slug NOT IN ('plataforma-interna') AND MontoMensual = 0;
GO
