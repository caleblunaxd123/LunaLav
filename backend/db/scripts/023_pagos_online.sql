-- ============================================================
-- 023: Pagos online (Culqi) para pedidos Delivery
--
-- Permite que el cliente pague su pedido desde su casa (tarjeta o Yape via
-- Culqi Checkout) en vez de pagarle en efectivo/Yape al repartidor. El link
-- publico usa un token opaco (no el Id/Numero correlativo del pedido, que es
-- adivinable) y expira a las 48 horas.
--
-- Es re-ejecutable: cada paso valida si ya se aplico antes de tocar nada.
-- ============================================================
USE LunaLav;
GO

IF OBJECT_ID('dbo.ConfiguracionPagos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ConfiguracionPagos (
        Id                  INT IDENTITY(1,1) PRIMARY KEY,
        NegocioId           INT NOT NULL UNIQUE FOREIGN KEY REFERENCES dbo.Negocio(Id),
        Proveedor           NVARCHAR(20) NOT NULL DEFAULT 'CULQI',
        PublicKey           NVARCHAR(200) NULL,      -- no es secreta, se usa en el navegador del cliente
        SecretKeyCifrada    NVARCHAR(500) NULL,       -- cifrada con SecretProtector, solo se usa server-side
        Activo              BIT NOT NULL DEFAULT 0,
        FechaActualizacion  DATETIME2 NOT NULL DEFAULT SYSDATETIME()
    );
END
GO

IF OBJECT_ID('dbo.SolicitudPago', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SolicitudPago (
        Id                  INT IDENTITY(1,1) PRIMARY KEY,
        NegocioId           INT NOT NULL FOREIGN KEY REFERENCES dbo.Negocio(Id),
        SedeId              INT NOT NULL FOREIGN KEY REFERENCES dbo.Sede(Id),
        PedidoId            INT NOT NULL FOREIGN KEY REFERENCES dbo.Pedido(Id),
        Token               UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        Monto               DECIMAL(12,2) NOT NULL,
        Estado              NVARCHAR(20) NOT NULL DEFAULT 'PENDIENTE', -- PENDIENTE | PAGADO | EXPIRADO | CANCELADO
        CulqiChargeId       NVARCHAR(50) NULL,
        FechaCreacion       DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        FechaExpiracion     DATETIME2 NOT NULL,
        FechaPago           DATETIME2 NULL
    );
    CREATE UNIQUE INDEX UX_SolicitudPago_Token ON dbo.SolicitudPago(Token);
    CREATE INDEX IX_SolicitudPago_PedidoId ON dbo.SolicitudPago(PedidoId);
END
GO

