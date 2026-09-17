-- ============================================================
-- 018: Multi-tenant (Negocio) + multi-sede (Sede)
--
-- Convierte el sistema de single-tenant a multi-tenant SaaS:
--   Negocio (tenant que alquila el sistema) -> tiene 1 o varias Sedes.
--   Catalogo y clientes: compartidos a nivel Negocio (NegocioId).
--   Operacion (pedidos, caja, inventario, personal): por Sede (SedeId).
--
-- Es re-ejecutable: cada paso valida si ya se aplico antes de tocar nada.
-- IMPORTANTE: hacer backup de la base de datos antes de correr esto.
-- ============================================================
USE LunaLav;
GO

-- Promocion ya tiene un indice filtrado (UX_Promocion_Codigo); cualquier ALTER/UPDATE
-- sobre una tabla con indices filtrados requiere QUOTED_IDENTIFIER ON en la sesion.
SET QUOTED_IDENTIFIER ON;
GO

-- ---------- 1. Tablas nuevas ----------
IF OBJECT_ID('dbo.Negocio', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Negocio (
        Id              INT IDENTITY(1,1) PRIMARY KEY,
        Nombre          NVARCHAR(120) NOT NULL,
        RucEmpresa      NVARCHAR(20) NULL,
        TitularNombre   NVARCHAR(120) NULL,
        TitularEmail    NVARCHAR(120) NULL,
        Activo          BIT NOT NULL DEFAULT 1,
        FechaCreacion   DATETIME2 NOT NULL DEFAULT SYSDATETIME()
    );
END
GO

IF OBJECT_ID('dbo.Sede', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Sede (
        Id              INT IDENTITY(1,1) PRIMARY KEY,
        NegocioId       INT NOT NULL FOREIGN KEY REFERENCES dbo.Negocio(Id),
        Nombre          NVARCHAR(120) NOT NULL,
        Direccion       NVARCHAR(200) NULL,
        Telefono        NVARCHAR(30) NULL,
        Activo          BIT NOT NULL DEFAULT 1,
        FechaCreacion   DATETIME2 NOT NULL DEFAULT SYSDATETIME()
    );
    CREATE INDEX IX_Sede_NegocioId ON dbo.Sede(NegocioId);
END
GO

-- ---------- 2. Semilla: tenant demostrativo para una instalación nueva ----------
IF NOT EXISTS (SELECT 1 FROM dbo.Negocio WHERE Nombre = N'Lavandería Demo LunaLav')
    INSERT INTO dbo.Negocio (Nombre, Activo) VALUES (N'Lavandería Demo LunaLav', 1);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Sede WHERE Nombre = N'Principal')
    INSERT INTO dbo.Sede (NegocioId, Nombre, Activo)
    VALUES ((SELECT Id FROM dbo.Negocio WHERE Nombre = N'Lavandería Demo LunaLav'), N'Principal', 1);
GO

-- ---------- 3. Agregar NegocioId/SedeId a las tablas existentes ----------
-- Se hace en un solo batch con SQL dinamico para evitar repetir 4 pasos x 18 tablas.
DECLARE @NegocioId INT = (SELECT Id FROM dbo.Negocio WHERE Nombre = N'Lavandería Demo LunaLav');
DECLARE @SedeId INT = (SELECT Id FROM dbo.Sede WHERE Nombre = N'Principal');

DECLARE @Tablas TABLE (Tabla SYSNAME, Columna SYSNAME, RefTabla SYSNAME, ValorSemilla INT, EsNullable BIT);
INSERT INTO @Tablas (Tabla, Columna, RefTabla, ValorSemilla, EsNullable) VALUES
    -- Compartidos a nivel Negocio (catalogo/clientes)
    ('Usuario',              'NegocioId', 'Negocio', @NegocioId, 0),
    ('Usuario',              'SedeId',    'Sede',    @SedeId,    1),  -- nullable: NULL = admin con acceso a todas las sedes
    ('ConfiguracionNegocio', 'NegocioId', 'Negocio', @NegocioId, 0),
    ('Cliente',              'NegocioId', 'Negocio', @NegocioId, 0),
    ('Categoria',            'NegocioId', 'Negocio', @NegocioId, 0),
    ('Servicio',             'NegocioId', 'Negocio', @NegocioId, 0),
    ('Promocion',            'NegocioId', 'Negocio', @NegocioId, 0),
    ('PlantillaWhatsapp',    'NegocioId', 'Negocio', @NegocioId, 0),
    ('RolPersonal',          'NegocioId', 'Negocio', @NegocioId, 0),
    ('TipoGasto',            'NegocioId', 'Negocio', @NegocioId, 0),
    ('RolPermiso',           'NegocioId', 'Negocio', @NegocioId, 0),
    ('MovimientoPuntos',     'NegocioId', 'Negocio', @NegocioId, 0),
    -- Independientes por Sede (operacion)
    ('AreaLavado',           'SedeId',    'Sede',    @SedeId,    0),
    ('Pedido',               'SedeId',    'Sede',    @SedeId,    0),
    ('CuadreCaja',           'SedeId',    'Sede',    @SedeId,    0),
    ('MovimientoCaja',       'SedeId',    'Sede',    @SedeId,    0),
    ('Empleado',             'SedeId',    'Sede',    @SedeId,    0),
    ('Insumo',               'SedeId',    'Sede',    @SedeId,    0),
    ('MovimientoInsumo',     'SedeId',    'Sede',    @SedeId,    0);

DECLARE @Tabla SYSNAME, @Columna SYSNAME, @RefTabla SYSNAME, @Valor INT, @EsNullable BIT, @Sql NVARCHAR(MAX), @FkName SYSNAME, @IxName SYSNAME;

DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT Tabla, Columna, RefTabla, ValorSemilla, EsNullable FROM @Tablas;
OPEN cur;
FETCH NEXT FROM cur INTO @Tabla, @Columna, @RefTabla, @Valor, @EsNullable;
WHILE @@FETCH_STATUS = 0
BEGIN
    -- 3a. Agregar la columna (siempre nullable al inicio, para poder hacer backfill)
    IF COL_LENGTH('dbo.' + @Tabla, @Columna) IS NULL
    BEGIN
        SET @Sql = N'ALTER TABLE dbo.' + QUOTENAME(@Tabla) + N' ADD ' + QUOTENAME(@Columna) + N' INT NULL;';
        EXEC(@Sql);
    END

    -- 3b. Backfill de las filas existentes
    SET @Sql = N'UPDATE dbo.' + QUOTENAME(@Tabla) + N' SET ' + QUOTENAME(@Columna) + N' = ' +
               CAST(@Valor AS NVARCHAR(20)) + N' WHERE ' + QUOTENAME(@Columna) + N' IS NULL;';
    EXEC(@Sql);

    -- 3c. Pasar a NOT NULL, salvo columnas nullable por diseno (Usuario.SedeId)
    IF @EsNullable = 0
    BEGIN
        SET @Sql = N'ALTER TABLE dbo.' + QUOTENAME(@Tabla) + N' ALTER COLUMN ' + QUOTENAME(@Columna) + N' INT NOT NULL;';
        EXEC(@Sql);
    END

    -- 3d. Foreign key
    SET @FkName = N'FK_' + @Tabla + N'_' + @Columna;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = @FkName)
    BEGIN
        SET @Sql = N'ALTER TABLE dbo.' + QUOTENAME(@Tabla) + N' ADD CONSTRAINT ' + QUOTENAME(@FkName) +
                   N' FOREIGN KEY (' + QUOTENAME(@Columna) + N') REFERENCES dbo.' + QUOTENAME(@RefTabla) + N'(Id);';
        EXEC(@Sql);
    END

    -- 3e. Indice (las consultas van a filtrar por esta columna en las fases siguientes)
    SET @IxName = N'IX_' + @Tabla + N'_' + @Columna;
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = @IxName AND object_id = OBJECT_ID('dbo.' + @Tabla))
    BEGIN
        SET @Sql = N'CREATE INDEX ' + QUOTENAME(@IxName) + N' ON dbo.' + QUOTENAME(@Tabla) + N'(' + QUOTENAME(@Columna) + N');';
        EXEC(@Sql);
    END

    FETCH NEXT FROM cur INTO @Tabla, @Columna, @RefTabla, @Valor, @EsNullable;
END
CLOSE cur;
DEALLOCATE cur;
GO

-- ---------- 4. Ajustar constraints unicos para incluir Negocio/Sede ----------

-- Pedido.Numero: era UNICO global, pasa a UNICO por Sede
DECLARE @PedidoNumeroConstraint SYSNAME, @Sql4 NVARCHAR(MAX);
SELECT @PedidoNumeroConstraint = kc.name
FROM sys.key_constraints kc
INNER JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE kc.parent_object_id = OBJECT_ID('dbo.Pedido') AND kc.type = 'UQ' AND c.name = 'Numero';
IF @PedidoNumeroConstraint IS NOT NULL
BEGIN
    SET @Sql4 = N'ALTER TABLE dbo.Pedido DROP CONSTRAINT ' + QUOTENAME(@PedidoNumeroConstraint);
    EXEC(@Sql4);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_Pedido_Sede_Numero' AND object_id = OBJECT_ID('dbo.Pedido'))
    CREATE UNIQUE INDEX UQ_Pedido_Sede_Numero ON dbo.Pedido(SedeId, Numero);
GO

-- CuadreCaja: de (Fecha, UsuarioId) a (SedeId, Fecha, UsuarioId). Se limpia cualquier
-- constraint/indice unico previo sobre la tabla (001_schema.sql y 016_cuadre_por_usuario.sql
-- crearon versiones distintas de este unique a lo largo del tiempo).
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_CuadreCaja_Fecha_Usuario' AND object_id = OBJECT_ID('dbo.CuadreCaja'))
    DROP INDEX UQ_CuadreCaja_Fecha_Usuario ON dbo.CuadreCaja;
GO

DECLARE @CuadreFechaConstraint SYSNAME, @Sql4b NVARCHAR(MAX);
SELECT @CuadreFechaConstraint = name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('dbo.CuadreCaja') AND type = 'UQ';
IF @CuadreFechaConstraint IS NOT NULL
BEGIN
    SET @Sql4b = N'ALTER TABLE dbo.CuadreCaja DROP CONSTRAINT ' + QUOTENAME(@CuadreFechaConstraint);
    EXEC(@Sql4b);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_CuadreCaja_Sede_Fecha_Usuario' AND object_id = OBJECT_ID('dbo.CuadreCaja'))
    CREATE UNIQUE INDEX UQ_CuadreCaja_Sede_Fecha_Usuario ON dbo.CuadreCaja(SedeId, Fecha, UsuarioId);
GO

-- Promocion.Codigo: de unico global a unico por Negocio
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Promocion_Codigo' AND object_id = OBJECT_ID('dbo.Promocion'))
    DROP INDEX UX_Promocion_Codigo ON dbo.Promocion;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Promocion_Negocio_Codigo' AND object_id = OBJECT_ID('dbo.Promocion'))
    CREATE UNIQUE INDEX UX_Promocion_Negocio_Codigo ON dbo.Promocion(NegocioId, Codigo) WHERE Codigo IS NOT NULL;
GO

-- RolPermiso: de (RolId, Modulo) a (NegocioId, RolId, Modulo) -- cada negocio puede
-- personalizar sus propios permisos por rol sin afectar a otros negocios.
DECLARE @RolPermisoConstraint SYSNAME, @Sql4c NVARCHAR(MAX);
SELECT @RolPermisoConstraint = name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('dbo.RolPermiso') AND type = 'UQ';
IF @RolPermisoConstraint IS NOT NULL
BEGIN
    SET @Sql4c = N'ALTER TABLE dbo.RolPermiso DROP CONSTRAINT ' + QUOTENAME(@RolPermisoConstraint);
    EXEC(@Sql4c);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_RolPermiso_Negocio_Rol_Modulo' AND object_id = OBJECT_ID('dbo.RolPermiso'))
    CREATE UNIQUE INDEX UQ_RolPermiso_Negocio_Rol_Modulo ON dbo.RolPermiso(NegocioId, RolId, Modulo);
GO

