-- 036: aislamiento de usuarios por tenant, auditoria publica, tokens de reparto e Izipay.
-- Re-ejecutable. Culqi queda desactivado y sus credenciales anteriores se eliminan.
USE LunaLav;
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
GO

BEGIN TRANSACTION;

IF OBJECT_ID('dbo.SchemaVersion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SchemaVersion (
        Version       NVARCHAR(80) NOT NULL PRIMARY KEY,
        AplicadoEn    DATETIME2 NOT NULL DEFAULT SYSDATETIME()
    );
END;

-- Usuario pasa de unicidad global a unicidad dentro de cada negocio.
DECLARE @constraintUsuario SYSNAME;
SELECT TOP (1) @constraintUsuario = kc.name
FROM sys.key_constraints kc
INNER JOIN sys.index_columns ic
    ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
WHERE kc.parent_object_id = OBJECT_ID('dbo.Usuario')
  AND kc.type = 'UQ'
GROUP BY kc.name
HAVING COUNT(*) = 1 AND MAX(COL_NAME(ic.object_id, ic.column_id)) = 'Usuario';

IF @constraintUsuario IS NOT NULL
BEGIN
    DECLARE @sqlConstraint NVARCHAR(500) = N'ALTER TABLE dbo.Usuario DROP CONSTRAINT ' + QUOTENAME(@constraintUsuario);
    EXEC sys.sp_executesql @sqlConstraint;
END;

DECLARE @indiceUsuario SYSNAME;
SELECT TOP (1) @indiceUsuario = i.name
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
WHERE i.object_id = OBJECT_ID('dbo.Usuario')
  AND i.is_unique = 1
  AND i.is_unique_constraint = 0
GROUP BY i.name
HAVING COUNT(*) = 1 AND MAX(COL_NAME(ic.object_id, ic.column_id)) = 'Usuario';

IF @indiceUsuario IS NOT NULL
BEGIN
    DECLARE @sqlIndice NVARCHAR(500) = N'DROP INDEX ' + QUOTENAME(@indiceUsuario) + N' ON dbo.Usuario';
    EXEC sys.sp_executesql @sqlIndice;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.Usuario') AND name = 'UX_Usuario_NegocioId_Usuario'
)
    CREATE UNIQUE INDEX UX_Usuario_NegocioId_Usuario ON dbo.Usuario(NegocioId, Usuario);

-- Auditoria: distingue personal, cliente publico y repartidor.
IF COL_LENGTH('dbo.PedidoHistorial', 'ActorTipo') IS NULL
    ALTER TABLE dbo.PedidoHistorial ADD ActorTipo NVARCHAR(30) NOT NULL
        CONSTRAINT DF_PedidoHistorial_ActorTipo DEFAULT 'USUARIO';
IF COL_LENGTH('dbo.PedidoHistorial', 'ActorDescripcion') IS NULL
    ALTER TABLE dbo.PedidoHistorial ADD ActorDescripcion NVARCHAR(120) NULL;

-- Los enlaces del repartidor expiran y pueden rotarse/revocarse.
IF COL_LENGTH('dbo.Pedido', 'TokenRutaExpiraEn') IS NULL
    ALTER TABLE dbo.Pedido ADD TokenRutaExpiraEn DATETIME2 NULL;

EXEC(N'UPDATE dbo.Pedido
       SET TokenRuta = NULL, TokenRutaExpiraEn = NULL
       WHERE TokenRuta IS NOT NULL
         AND (Anulado = 1 OR EstadoProceso IN (''ENTREGADO'', ''ANULADO'', ''DONADO''));

       UPDATE dbo.Pedido
       SET TokenRutaExpiraEn = DATEADD(DAY, 7, SYSDATETIME())
       WHERE TokenRuta IS NOT NULL AND TokenRutaExpiraEn IS NULL');

-- Estructura de credenciales requerida por Izipay. Las claves API/Hash son secretas.
IF COL_LENGTH('dbo.ConfiguracionPagos', 'CodigoComercio') IS NULL
    ALTER TABLE dbo.ConfiguracionPagos ADD CodigoComercio NVARCHAR(50) NULL;
IF COL_LENGTH('dbo.ConfiguracionPagos', 'ApiKeyCifrada') IS NULL
    ALTER TABLE dbo.ConfiguracionPagos ADD ApiKeyCifrada NVARCHAR(1000) NULL;
IF COL_LENGTH('dbo.ConfiguracionPagos', 'HashKeyCifrada') IS NULL
    ALTER TABLE dbo.ConfiguracionPagos ADD HashKeyCifrada NVARCHAR(1000) NULL;

IF COL_LENGTH('dbo.ConfiguracionPagos', 'PublicKey') IS NOT NULL
    ALTER TABLE dbo.ConfiguracionPagos ALTER COLUMN PublicKey NVARCHAR(MAX) NULL;

UPDATE dbo.ConfiguracionPagos
SET Proveedor = 'IZIPAY',
    Activo = 0,
    PublicKey = NULL,
    SecretKeyCifrada = NULL,
    FechaActualizacion = SYSDATETIME()
WHERE Proveedor <> 'IZIPAY' OR Proveedor IS NULL;

IF COL_LENGTH('dbo.SolicitudPago', 'ProveedorOperacionId') IS NULL
    ALTER TABLE dbo.SolicitudPago ADD ProveedorOperacionId NVARCHAR(120) NULL;

IF COL_LENGTH('dbo.SolicitudPago', 'CulqiChargeId') IS NOT NULL
    EXEC(N'UPDATE dbo.SolicitudPago
           SET ProveedorOperacionId = COALESCE(ProveedorOperacionId, CulqiChargeId)
           WHERE CulqiChargeId IS NOT NULL');

IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersion WHERE Version = '036_endurecimiento_saas_izipay')
    INSERT INTO dbo.SchemaVersion(Version) VALUES ('036_endurecimiento_saas_izipay');

COMMIT TRANSACTION;
GO

