-- ============================================================
-- 027: Logistica de delivery — motorizado asignado a un pedido
--
-- Catalogo simple de repartidores (por sede, igual que Empleado/AreaLavado) mas la asignacion
-- en el propio Pedido. La trazabilidad que ve el cliente (paso "En camino") ya existia en el
-- portal de seguimiento; esto solo le agrega el nombre/celular de quien va en camino.
-- ============================================================
USE LunaLav;
GO

IF OBJECT_ID('dbo.Motorizado', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Motorizado (
        Id       INT IDENTITY(1,1) PRIMARY KEY,
        SedeId   INT NOT NULL FOREIGN KEY REFERENCES dbo.Sede(Id),
        Nombre   NVARCHAR(120) NOT NULL,
        Celular  NVARCHAR(20) NULL,
        Activo   BIT NOT NULL DEFAULT 1
    );
    CREATE INDEX IX_Motorizado_SedeId ON dbo.Motorizado(SedeId);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Pedido') AND name = 'MotorizadoId'
)
    ALTER TABLE dbo.Pedido ADD MotorizadoId INT NULL FOREIGN KEY REFERENCES dbo.Motorizado(Id);
GO

