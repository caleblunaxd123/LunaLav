-- ----------------------------------------------------------------
-- Personal (empleados)
-- ----------------------------------------------------------------
IF OBJECT_ID('dbo.Empleado', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Empleado (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        Nombre        NVARCHAR(120) NOT NULL,
        Dni           NVARCHAR(15) NULL,
        Celular       NVARCHAR(20) NULL,
        Cargo         NVARCHAR(60) NULL,
        FechaIngreso  DATE NULL,
        Activo        BIT NOT NULL DEFAULT 1
    );
END
GO

-- ----------------------------------------------------------------
-- Semilla de plantillas de WhatsApp por evento (tabla ya existente
-- en 001_schema.sql, aqui solo se llena si esta vacia)
-- ----------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.PlantillaWhatsapp)
BEGIN
    INSERT INTO dbo.PlantillaWhatsapp (Evento, Mensaje, Activa) VALUES
    ('INGRESO',     'Hola {cliente}, registramos tu pedido #{numero} por un total de S/ {total}. ¡Gracias por tu preferencia!', 1),
    ('CAMBIO_AREA', 'Hola {cliente}, tu pedido #{numero} está en la etapa: {area}.', 1),
    ('LISTO',       'Hola {cliente}, tu pedido #{numero} está listo para recoger. ¡Te esperamos!', 1),
    ('DEMORA',      'Hola {cliente}, tu pedido #{numero} está listo para recoger hace {dias} día(s). ¡Te esperamos!', 1),
    ('ENTREGADO',   'Hola {cliente}, tu pedido #{numero} fue entregado. ¡Gracias por tu preferencia!', 1);
END
GO
