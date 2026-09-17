-- ----------------------------------------------------------------
-- Inventario de consumibles (detergente, bolsas, suavizante, etc.)
-- Valor agregado propio, no presente en el SaaS de referencia.
-- ----------------------------------------------------------------
IF OBJECT_ID('dbo.Insumo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Insumo (
        Id           INT IDENTITY(1,1) PRIMARY KEY,
        Nombre       NVARCHAR(80) NOT NULL,
        UnidadMedida NVARCHAR(20) NOT NULL,
        StockActual  DECIMAL(10,2) NOT NULL DEFAULT 0,
        StockMinimo  DECIMAL(10,2) NOT NULL DEFAULT 0,
        Activo       BIT NOT NULL DEFAULT 1
    );
END
GO

IF OBJECT_ID('dbo.MovimientoInsumo', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MovimientoInsumo (
        Id           INT IDENTITY(1,1) PRIMARY KEY,
        InsumoId     INT NOT NULL FOREIGN KEY REFERENCES dbo.Insumo(Id),
        Tipo         NVARCHAR(20) NOT NULL,  -- COMPRA | CONSUMO | AJUSTE
        Cantidad     DECIMAL(10,2) NOT NULL,
        CostoTotal   DECIMAL(10,2) NULL,
        Fecha        DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        UsuarioId    INT NOT NULL,
        Descripcion  NVARCHAR(300) NULL,
        MovimientoCajaId INT NULL FOREIGN KEY REFERENCES dbo.MovimientoCaja(Id)
    );
END
GO
