SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Evidencia fotografica de los pedidos: fotos que el personal toma al revisar/clasificar las
-- prendas (RECEPCION) y al entregarlas (ENTREGA), visibles tambien para el cliente en su
-- pagina de seguimiento. Los archivos viven en disco (config Fotos:Directorio); aqui solo se
-- guarda el metadato + el nombre de archivo. NegocioId y SedeId quedan denormalizados para
-- respetar el aislamiento multi-tenant sin joins extra al servir/borrar.
IF OBJECT_ID('dbo.PedidoFoto', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidoFoto (
        Id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PedidoFoto PRIMARY KEY,
        PedidoId           INT            NOT NULL,
        SedeId             INT            NOT NULL,
        NegocioId          INT            NOT NULL,
        Momento            NVARCHAR(20)   NOT NULL CONSTRAINT DF_PedidoFoto_Momento DEFAULT 'OTRO',
        NombreArchivo      NVARCHAR(120)  NOT NULL,
        ContentType        NVARCHAR(60)   NOT NULL,
        TamanoBytes        INT            NOT NULL,
        SubidoPorUsuarioId INT            NULL,
        FechaSubida        DATETIME2(0)   NOT NULL CONSTRAINT DF_PedidoFoto_Fecha DEFAULT SYSDATETIME(),
        CONSTRAINT FK_PedidoFoto_Pedido FOREIGN KEY (PedidoId) REFERENCES dbo.Pedido(Id),
        CONSTRAINT CK_PedidoFoto_Momento CHECK (Momento IN ('RECEPCION','ENTREGA','OTRO'))
    );

    CREATE INDEX IX_PedidoFoto_Pedido ON dbo.PedidoFoto (PedidoId, FechaSubida);
END
GO
