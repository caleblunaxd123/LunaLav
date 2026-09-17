-- 045: Generador de códigos de descuento automáticos.
-- Extiende dbo.Promocion para soportar códigos PERSONALIZADOS (a diferencia de los
-- códigos de marketing compartidos como VERANO10): atados a un cliente, de uso limitado
-- y con un origen que explica por qué se generó (bienvenida, cumpleaños, referido, puntos).
--   ClienteId : cliente dueño del código (NULL = código abierto/compartido).
--   Origen    : NUEVO | CUMPLE | REFERIDO | PUNTOS | MANUAL (NULL para promos antiguas).
--   MaxUsos   : tope de canjes (NULL = ilimitado, como las promos de marketing).
--   Usos      : contador de canjes ya realizados.
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.Promocion', 'ClienteId') IS NULL
    ALTER TABLE dbo.Promocion ADD ClienteId INT NULL;
GO

IF COL_LENGTH('dbo.Promocion', 'Origen') IS NULL
    ALTER TABLE dbo.Promocion ADD Origen NVARCHAR(20) NULL;
GO

IF COL_LENGTH('dbo.Promocion', 'MaxUsos') IS NULL
    ALTER TABLE dbo.Promocion ADD MaxUsos INT NULL;
GO

IF COL_LENGTH('dbo.Promocion', 'Usos') IS NULL
    ALTER TABLE dbo.Promocion ADD Usos INT NOT NULL CONSTRAINT DF_Promocion_Usos DEFAULT 0;
GO

-- FK a Cliente (misma BD, borrado protegido por el soft-delete de clientes)
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Promocion_Cliente')
    ALTER TABLE dbo.Promocion
        ADD CONSTRAINT FK_Promocion_Cliente FOREIGN KEY (ClienteId) REFERENCES dbo.Cliente(Id);
GO

PRINT 'OK 045: Promocion extendida para codigos generados (ClienteId, Origen, MaxUsos, Usos).';
GO
