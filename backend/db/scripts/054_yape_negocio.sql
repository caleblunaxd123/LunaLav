-- 054: Datos de cobro por Yape/Plin del NEGOCIO (para que el cliente le pague a la lavandería).
-- Distinto del Yape de la plataforma (ConfiguracionPlataforma), que es para la suscripción del dueño del SaaS.
-- Se usan en el mensaje de WhatsApp "en camino" (delivery) y en la ventana del repartidor (QR).

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ConfiguracionNegocio') AND name = 'YapeNumero')
    ALTER TABLE dbo.ConfiguracionNegocio ADD YapeNumero NVARCHAR(30) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ConfiguracionNegocio') AND name = 'YapeTitular')
    ALTER TABLE dbo.ConfiguracionNegocio ADD YapeTitular NVARCHAR(120) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ConfiguracionNegocio') AND name = 'YapeQrUrl')
    ALTER TABLE dbo.ConfiguracionNegocio ADD YapeQrUrl NVARCHAR(300) NULL;
GO

PRINT 'OK 054: YapeNumero, YapeTitular, YapeQrUrl agregados a ConfiguracionNegocio.';
