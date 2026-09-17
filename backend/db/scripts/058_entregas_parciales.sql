-- ================================================================
-- 058_entregas_parciales.sql
-- Entregas parciales del pedido + control de cantidad entregada por ítem.
-- Permite que el cliente se lleve una parte de las prendas (según lo que
-- pagó) y el resto en otra visita, y que se cobre con varios métodos a la
-- vez. El saldo pendiente queda registrado como "por cobrar".
-- Idempotente: se puede correr varias veces sin error.
-- ================================================================

-- 1) Cantidad ya entregada por cada ítem del pedido (acumulado).
IF COL_LENGTH('dbo.PedidoItem', 'CantidadEntregada') IS NULL
    ALTER TABLE dbo.PedidoItem ADD CantidadEntregada DECIMAL(10,2) NOT NULL DEFAULT 0;
GO

-- 2) Cabecera de cada entrega (parcial o final) que se le hace al cliente.
IF OBJECT_ID('dbo.PedidoEntrega', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidoEntrega (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        PedidoId      INT NOT NULL FOREIGN KEY REFERENCES dbo.Pedido(Id) ON DELETE CASCADE,
        SedeId        INT NOT NULL,
        Fecha         DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        UsuarioId     INT NULL FOREIGN KEY REFERENCES dbo.Usuario(Id),
        RecibidoPor   NVARCHAR(120) NULL,   -- quién recogió (si no es el titular)
        Nota          NVARCHAR(300) NULL,   -- descripción libre de lo entregado
        EsFinal       BIT NOT NULL DEFAULT 0, -- true = quedó todo entregado (pedido ENTREGADO)
        MontoCobrado  DECIMAL(10,2) NOT NULL DEFAULT 0 -- lo cobrado en ESTA entrega
    );
    CREATE INDEX IX_PedidoEntrega_PedidoId ON dbo.PedidoEntrega(PedidoId);
END
GO

-- 3) Detalle: qué ítems y en qué cantidad se entregaron en cada entrega.
IF OBJECT_ID('dbo.PedidoEntregaDetalle', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidoEntregaDetalle (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        EntregaId     INT NOT NULL FOREIGN KEY REFERENCES dbo.PedidoEntrega(Id) ON DELETE CASCADE,
        PedidoItemId  INT NOT NULL FOREIGN KEY REFERENCES dbo.PedidoItem(Id),
        Cantidad      DECIMAL(10,2) NOT NULL
    );
    CREATE INDEX IX_PedidoEntregaDetalle_EntregaId ON dbo.PedidoEntregaDetalle(EntregaId);
END
GO

-- 4) Permitir entregar con saldo pendiente ("fiado"): se elimina la regla (de la
--    migración 032) que exigía pagar el total para marcar un pedido como ENTREGADO.
--    El saldo pendiente sigue registrado y contando en "por cobrar".
--    (Las demás reglas de integridad y el trigger de estados terminales se conservan.)
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Pedido_EntregadoPagado')
    ALTER TABLE dbo.Pedido DROP CONSTRAINT CK_Pedido_EntregadoPagado;
GO
