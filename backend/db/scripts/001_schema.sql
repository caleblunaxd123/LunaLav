-- ============================================================
-- Lavanderia - Schema inicial
-- Requiere: SQL Server 2019+
-- ============================================================

IF DB_ID('LunaLav') IS NULL
BEGIN
    CREATE DATABASE LunaLav;
END
GO

USE LunaLav;
GO

-- ----------------------------------------------------------------
-- Configuracion del negocio (marca configurable por instancia)
-- ----------------------------------------------------------------
IF OBJECT_ID('dbo.ConfiguracionNegocio', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ConfiguracionNegocio (
        Id                  INT IDENTITY(1,1) PRIMARY KEY,
        NombreNegocio       NVARCHAR(120)   NOT NULL,
        LogoUrl             NVARCHAR(400)   NULL,
        ColorPrimario       NVARCHAR(9)     NOT NULL DEFAULT '#0b57d0',
        ColorSecundario     NVARCHAR(9)     NOT NULL DEFAULT '#29b6f6',
        ColorAcento         NVARCHAR(9)     NOT NULL DEFAULT '#f5a623',
        Direccion           NVARCHAR(200)   NULL,
        Telefono            NVARCHAR(30)    NULL,
        Ruc                 NVARCHAR(20)    NULL,
        HorarioAtencion     NVARCHAR(120)   NULL,
        Igv                 DECIMAL(5,2)    NOT NULL DEFAULT 18.00,
        MetaMensual         DECIMAL(12,2)   NOT NULL DEFAULT 0,
        SolesPorPunto       DECIMAL(10,2)   NOT NULL DEFAULT 1,
        FechaActualizacion  DATETIME2       NOT NULL DEFAULT SYSDATETIME()
    );
END
GO

-- ----------------------------------------------------------------
-- Roles y Usuarios
-- ----------------------------------------------------------------
IF OBJECT_ID('dbo.Rol', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Rol (
        Id      INT IDENTITY(1,1) PRIMARY KEY,
        Codigo  NVARCHAR(30) NOT NULL UNIQUE,     -- ADMIN | TRABAJADOR | COORDINADOR
        Nombre  NVARCHAR(60) NOT NULL
    );
END
GO

IF OBJECT_ID('dbo.Usuario', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Usuario (
        Id              INT IDENTITY(1,1) PRIMARY KEY,
        Usuario         NVARCHAR(60)  NOT NULL UNIQUE,
        NombreCompleto  NVARCHAR(120) NOT NULL,
        Email           NVARCHAR(120) NULL,
        PasswordHash    NVARCHAR(200) NOT NULL,
        RolId           INT NOT NULL FOREIGN KEY REFERENCES dbo.Rol(Id),
        Activo          BIT NOT NULL DEFAULT 1,
        FechaCreacion   DATETIME2 NOT NULL DEFAULT SYSDATETIME()
    );
END
GO

-- ----------------------------------------------------------------
-- Cliente
-- ----------------------------------------------------------------
IF OBJECT_ID('dbo.Cliente', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cliente (
        Id              INT IDENTITY(1,1) PRIMARY KEY,
        Nombre          NVARCHAR(120) NOT NULL,
        Celular         NVARCHAR(20)  NULL,
        Dni             NVARCHAR(15)  NULL,
        DocumentoFiscal NVARCHAR(20)  NULL,     -- RUC opcional
        Direccion       NVARCHAR(200) NULL,
        Puntos          INT           NOT NULL DEFAULT 0,
        FechaCreacion   DATETIME2     NOT NULL DEFAULT SYSDATETIME()
    );
    CREATE INDEX IX_Cliente_Celular ON dbo.Cliente(Celular);
    CREATE INDEX IX_Cliente_Dni ON dbo.Cliente(Dni);
END
GO

-- ----------------------------------------------------------------
-- Catalogo de servicios
-- ----------------------------------------------------------------
IF OBJECT_ID('dbo.Categoria', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Categoria (
        Id      INT IDENTITY(1,1) PRIMARY KEY,
        Nombre  NVARCHAR(80) NOT NULL,
        Activa  BIT NOT NULL DEFAULT 1
    );
END
GO

IF OBJECT_ID('dbo.Servicio', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Servicio (
        Id           INT IDENTITY(1,1) PRIMARY KEY,
        Nombre       NVARCHAR(120) NOT NULL,
        Precio       DECIMAL(10,2) NOT NULL,
        Unidad       NVARCHAR(30)  NOT NULL,     -- kg | prenda | pieza | und
        CategoriaId  INT NULL FOREIGN KEY REFERENCES dbo.Categoria(Id),
        Activo       BIT NOT NULL DEFAULT 1
    );
END
GO

-- ----------------------------------------------------------------
-- Areas de lavado (etapas del proceso)
-- ----------------------------------------------------------------
IF OBJECT_ID('dbo.AreaLavado', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AreaLavado (
        Id                INT IDENTITY(1,1) PRIMARY KEY,
        Nombre            NVARCHAR(60) NOT NULL,
        Orden             INT NOT NULL,
        TiempoEstMinutos  INT NOT NULL DEFAULT 30,
        Activa            BIT NOT NULL DEFAULT 1
    );
END
GO

-- ----------------------------------------------------------------
-- Pedidos
-- ----------------------------------------------------------------
IF OBJECT_ID('dbo.Pedido', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Pedido (
        Id                INT IDENTITY(1,1) PRIMARY KEY,
        Numero            INT NOT NULL UNIQUE,         -- correlativo visible
        ClienteId         INT NOT NULL FOREIGN KEY REFERENCES dbo.Cliente(Id),
        UsuarioId         INT NOT NULL FOREIGN KEY REFERENCES dbo.Usuario(Id),
        FechaIngreso      DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        FechaEntregaEst   DATETIME2 NULL,
        Modalidad         NVARCHAR(20) NOT NULL,        -- Tienda | Delivery
        Subtotal          DECIMAL(10,2) NOT NULL,
        Descuento         DECIMAL(10,2) NOT NULL DEFAULT 0,
        Total             DECIMAL(10,2) NOT NULL,
        MontoPagado       DECIMAL(10,2) NOT NULL DEFAULT 0,
        EstadoPago        NVARCHAR(20) NOT NULL DEFAULT 'PENDIENTE',  -- PENDIENTE | PARCIAL | PAGADO
        EstadoProceso     NVARCHAR(20) NOT NULL DEFAULT 'PENDIENTE',  -- PENDIENTE | EN_PROCESO | LISTO | ENTREGADO | ANULADO
        AreaActualId      INT NULL FOREIGN KEY REFERENCES dbo.AreaLavado(Id),
        Observaciones     NVARCHAR(500) NULL,
        FechaEntregaReal  DATETIME2 NULL,
        Anulado           BIT NOT NULL DEFAULT 0,
        MotivoAnulacion   NVARCHAR(200) NULL
    );
    CREATE INDEX IX_Pedido_ClienteId ON dbo.Pedido(ClienteId);
    CREATE INDEX IX_Pedido_EstadoProceso ON dbo.Pedido(EstadoProceso);
    CREATE INDEX IX_Pedido_FechaIngreso ON dbo.Pedido(FechaIngreso DESC);
END
GO

IF OBJECT_ID('dbo.PedidoItem', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidoItem (
        Id           INT IDENTITY(1,1) PRIMARY KEY,
        PedidoId     INT NOT NULL FOREIGN KEY REFERENCES dbo.Pedido(Id) ON DELETE CASCADE,
        ServicioId   INT NOT NULL FOREIGN KEY REFERENCES dbo.Servicio(Id),
        Cantidad     DECIMAL(10,2) NOT NULL,
        PrecioUnit   DECIMAL(10,2) NOT NULL,
        Total        DECIMAL(10,2) NOT NULL,
        Descripcion  NVARCHAR(200) NULL
    );
    CREATE INDEX IX_PedidoItem_PedidoId ON dbo.PedidoItem(PedidoId);
END
GO

IF OBJECT_ID('dbo.PedidoHistorial', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidoHistorial (
        Id              INT IDENTITY(1,1) PRIMARY KEY,
        PedidoId        INT NOT NULL FOREIGN KEY REFERENCES dbo.Pedido(Id) ON DELETE CASCADE,
        AreaId          INT NULL FOREIGN KEY REFERENCES dbo.AreaLavado(Id),
        EstadoProceso   NVARCHAR(20) NOT NULL,
        UsuarioId       INT NULL FOREIGN KEY REFERENCES dbo.Usuario(Id),
        Fecha           DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        Nota            NVARCHAR(300) NULL,
        NotificadoWsp   BIT NOT NULL DEFAULT 0
    );
    CREATE INDEX IX_PedidoHistorial_PedidoId ON dbo.PedidoHistorial(PedidoId);
END
GO

-- ----------------------------------------------------------------
-- Caja
-- ----------------------------------------------------------------
IF OBJECT_ID('dbo.CuadreCaja', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CuadreCaja (
        Id                    INT IDENTITY(1,1) PRIMARY KEY,
        Fecha                 DATE NOT NULL,
        UsuarioId             INT NOT NULL FOREIGN KEY REFERENCES dbo.Usuario(Id),
        CajaInicial           DECIMAL(12,2) NOT NULL,
        PedidosPagadosEfect   DECIMAL(12,2) NOT NULL,
        Gastos                DECIMAL(12,2) NOT NULL,
        TotalContado          DECIMAL(12,2) NOT NULL,
        Diferencia            DECIMAL(12,2) NOT NULL,   -- >0 sobra, <0 falta
        CajaFinal             DECIMAL(12,2) NOT NULL,
        Observaciones         NVARCHAR(400) NULL,
        FechaCreacion         DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        CONSTRAINT UQ_CuadreCaja_Fecha UNIQUE (Fecha)
    );
END
GO

IF OBJECT_ID('dbo.MovimientoCaja', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MovimientoCaja (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        Fecha         DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        Tipo          NVARCHAR(20) NOT NULL,   -- INGRESO | GASTO
        MetodoPago    NVARCHAR(30) NOT NULL,   -- EFECTIVO | YAPE | PLIN | TRANSFERENCIA | POS
        Monto         DECIMAL(12,2) NOT NULL,
        Descripcion   NVARCHAR(300) NULL,
        PedidoId      INT NULL FOREIGN KEY REFERENCES dbo.Pedido(Id),
        UsuarioId     INT NOT NULL FOREIGN KEY REFERENCES dbo.Usuario(Id),
        TipoGastoId   INT NULL
    );
    CREATE INDEX IX_MovimientoCaja_Fecha ON dbo.MovimientoCaja(Fecha DESC);
END
GO

IF OBJECT_ID('dbo.TipoGasto', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TipoGasto (
        Id     INT IDENTITY(1,1) PRIMARY KEY,
        Nombre NVARCHAR(80) NOT NULL,
        Activo BIT NOT NULL DEFAULT 1
    );
END
GO

-- ----------------------------------------------------------------
-- Promociones
-- ----------------------------------------------------------------
IF OBJECT_ID('dbo.Promocion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Promocion (
        Id              INT IDENTITY(1,1) PRIMARY KEY,
        Tipo            NVARCHAR(60) NOT NULL,        -- VOLUMEN | FRECUENCIA | FIJA
        Descripcion     NVARCHAR(200) NOT NULL,
        DescuentoPct    DECIMAL(5,2) NULL,
        DescuentoMonto  DECIMAL(10,2) NULL,
        ServicioId      INT NULL FOREIGN KEY REFERENCES dbo.Servicio(Id),
        CantidadMinima  DECIMAL(10,2) NOT NULL DEFAULT 1,
        FechaInicio     DATE NULL,
        FechaFin        DATE NULL,
        Activa          BIT NOT NULL DEFAULT 1
    );
END
GO

-- ----------------------------------------------------------------
-- Plantillas de WhatsApp por etapa
-- ----------------------------------------------------------------
IF OBJECT_ID('dbo.PlantillaWhatsapp', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlantillaWhatsapp (
        Id       INT IDENTITY(1,1) PRIMARY KEY,
        Evento   NVARCHAR(40) NOT NULL,   -- INGRESO | CAMBIO_AREA | LISTO | DEMORA | ENTREGADO
        Mensaje  NVARCHAR(1000) NOT NULL,
        Activa   BIT NOT NULL DEFAULT 1
    );
END
GO

