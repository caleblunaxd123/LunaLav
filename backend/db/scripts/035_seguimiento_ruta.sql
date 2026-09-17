SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Seguimiento en vivo del delivery (tipo Uber): momento en que el repartidor sale a ruta,
-- ultima posicion GPS reportada por su celular, token publico de su pantalla de reparto y
-- banderas para no repetir los avisos automaticos de "en ruta / cerca / llego".
IF COL_LENGTH('dbo.Pedido', 'RutaIniciadaEn') IS NULL
    ALTER TABLE dbo.Pedido ADD RutaIniciadaEn DATETIME NULL;
IF COL_LENGTH('dbo.Pedido', 'MotorizadoLat') IS NULL
    ALTER TABLE dbo.Pedido ADD MotorizadoLat DECIMAL(9,6) NULL;
IF COL_LENGTH('dbo.Pedido', 'MotorizadoLng') IS NULL
    ALTER TABLE dbo.Pedido ADD MotorizadoLng DECIMAL(9,6) NULL;
IF COL_LENGTH('dbo.Pedido', 'MotorizadoUbicadoEn') IS NULL
    ALTER TABLE dbo.Pedido ADD MotorizadoUbicadoEn DATETIME NULL;
IF COL_LENGTH('dbo.Pedido', 'TokenRuta') IS NULL
    ALTER TABLE dbo.Pedido ADD TokenRuta UNIQUEIDENTIFIER NULL;
IF COL_LENGTH('dbo.Pedido', 'NotifRutaEnviada') IS NULL
    ALTER TABLE dbo.Pedido ADD NotifRutaEnviada BIT NOT NULL CONSTRAINT DF_Pedido_NotifRuta DEFAULT 0;
IF COL_LENGTH('dbo.Pedido', 'NotifCercaEnviada') IS NULL
    ALTER TABLE dbo.Pedido ADD NotifCercaEnviada BIT NOT NULL CONSTRAINT DF_Pedido_NotifCerca DEFAULT 0;
IF COL_LENGTH('dbo.Pedido', 'NotifLlegadaEnviada') IS NULL
    ALTER TABLE dbo.Pedido ADD NotifLlegadaEnviada BIT NOT NULL CONSTRAINT DF_Pedido_NotifLlegada DEFAULT 0;
GO

-- Genera el token de reparto para los delivery vigentes que todavia no lo tienen.
-- (El backend tambien lo crea de forma perezosa al pedir el link, pero asi los pedidos
--  historicos ya quedan listos para probar el seguimiento sin tocar nada.)
UPDATE dbo.Pedido
   SET TokenRuta = NEWID()
 WHERE Modalidad = 'Delivery'
   AND TokenRuta IS NULL
   AND Anulado = 0;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Pedido') AND name = 'UX_Pedido_TokenRuta')
    CREATE UNIQUE INDEX UX_Pedido_TokenRuta ON dbo.Pedido (TokenRuta) WHERE TokenRuta IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Pedido_MotorizadoCoordenadas')
BEGIN
    ALTER TABLE dbo.Pedido WITH CHECK ADD CONSTRAINT CK_Pedido_MotorizadoCoordenadas CHECK (
        (MotorizadoLat IS NULL AND MotorizadoLng IS NULL)
        OR (
            MotorizadoLat BETWEEN -90 AND 90
            AND MotorizadoLng BETWEEN -180 AND 180
        )
    );
END
GO
