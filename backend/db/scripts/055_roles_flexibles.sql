-- ============================================================
-- 055: Roles de acceso FLEXIBLES por negocio.
--   Antes: Rol era global (ADMIN/COORDINADOR/TRABAJADOR compartidos).
--   Ahora: ADMIN y PROPIETARIO quedan como roles de SISTEMA (globales,
--   fijos). Cada negocio tiene sus PROPIOS roles (editables/eliminables).
--   Coordinador y Trabajador se convierten en roles por-negocio (ejemplos).
-- ============================================================
USE LunaLav;
GO

-- 1) Columnas nuevas en dbo.Rol
IF COL_LENGTH('dbo.Rol', 'NegocioId') IS NULL
    ALTER TABLE dbo.Rol ADD NegocioId INT NULL;
GO
IF COL_LENGTH('dbo.Rol', 'EsSistema') IS NULL
    ALTER TABLE dbo.Rol ADD EsSistema BIT NOT NULL CONSTRAINT DF_Rol_EsSistema DEFAULT 0;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Rol_Negocio')
    ALTER TABLE dbo.Rol ADD CONSTRAINT FK_Rol_Negocio FOREIGN KEY (NegocioId) REFERENCES dbo.Negocio(Id);
GO

-- 2) ADMIN y PROPIETARIO = roles de sistema (globales, no editables)
UPDATE dbo.Rol SET EsSistema = 1, NegocioId = NULL WHERE Codigo IN ('ADMIN', 'PROPIETARIO');
GO

-- 3) Quitar el UNIQUE global de Codigo y reemplazarlo por índices condicionales:
--    - system roles (NegocioId NULL): Codigo único
--    - roles por negocio: (NegocioId, Codigo) único
DECLARE @uq SYSNAME = (SELECT name FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('dbo.Rol') AND type = 'UQ');
IF @uq IS NOT NULL EXEC('ALTER TABLE dbo.Rol DROP CONSTRAINT ' + @uq);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Rol_Sistema_Codigo' AND object_id = OBJECT_ID('dbo.Rol'))
    CREATE UNIQUE INDEX UX_Rol_Sistema_Codigo ON dbo.Rol(Codigo) WHERE NegocioId IS NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Rol_Negocio_Codigo' AND object_id = OBJECT_ID('dbo.Rol'))
    CREATE UNIQUE INDEX UX_Rol_Negocio_Codigo ON dbo.Rol(NegocioId, Codigo) WHERE NegocioId IS NOT NULL;
GO

-- 4) Convertir COORDINADOR/TRABAJADOR globales en roles por-negocio (editables)
DECLARE @coordGlobal INT = (SELECT Id FROM dbo.Rol WHERE Codigo = 'COORDINADOR' AND NegocioId IS NULL);
DECLARE @trabGlobal  INT = (SELECT Id FROM dbo.Rol WHERE Codigo = 'TRABAJADOR'  AND NegocioId IS NULL);
DECLARE @negId INT, @newCoord INT, @newTrab INT;

DECLARE cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT Id FROM dbo.Negocio WHERE ISNULL(Slug, '') <> 'plataforma-interna';
OPEN cur;
FETCH NEXT FROM cur INTO @negId;
WHILE @@FETCH_STATUS = 0
BEGIN
    -- COORDINADOR por negocio
    IF @coordGlobal IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Rol WHERE NegocioId = @negId AND Codigo = 'COORDINADOR')
    BEGIN
        INSERT INTO dbo.Rol (Codigo, Nombre, NegocioId, EsSistema) VALUES ('COORDINADOR', N'Coordinador', @negId, 0);
        SET @newCoord = SCOPE_IDENTITY();
        INSERT INTO dbo.RolPermiso (RolId, Modulo, PuedeAcceder, NegocioId)
        SELECT @newCoord, rp.Modulo, rp.PuedeAcceder, @negId
        FROM dbo.RolPermiso rp
        WHERE rp.NegocioId = @negId AND rp.RolId = @coordGlobal
          AND NOT EXISTS (SELECT 1 FROM dbo.RolPermiso x WHERE x.NegocioId = @negId AND x.RolId = @newCoord AND x.Modulo = rp.Modulo);
        UPDATE dbo.Usuario SET RolId = @newCoord WHERE NegocioId = @negId AND RolId = @coordGlobal;
    END
    -- TRABAJADOR por negocio
    IF @trabGlobal IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Rol WHERE NegocioId = @negId AND Codigo = 'TRABAJADOR')
    BEGIN
        INSERT INTO dbo.Rol (Codigo, Nombre, NegocioId, EsSistema) VALUES ('TRABAJADOR', N'Trabajador', @negId, 0);
        SET @newTrab = SCOPE_IDENTITY();
        INSERT INTO dbo.RolPermiso (RolId, Modulo, PuedeAcceder, NegocioId)
        SELECT @newTrab, rp.Modulo, rp.PuedeAcceder, @negId
        FROM dbo.RolPermiso rp
        WHERE rp.NegocioId = @negId AND rp.RolId = @trabGlobal
          AND NOT EXISTS (SELECT 1 FROM dbo.RolPermiso x WHERE x.NegocioId = @negId AND x.RolId = @newTrab AND x.Modulo = rp.Modulo);
        UPDATE dbo.Usuario SET RolId = @newTrab WHERE NegocioId = @negId AND RolId = @trabGlobal;
    END
    FETCH NEXT FROM cur INTO @negId;
END
CLOSE cur;
DEALLOCATE cur;
GO

-- 5) Eliminar los roles globales COORDINADOR/TRABAJADOR (ya reemplazados por-negocio)
DELETE rp FROM dbo.RolPermiso rp
    INNER JOIN dbo.Rol r ON r.Id = rp.RolId
    WHERE r.NegocioId IS NULL AND r.Codigo IN ('COORDINADOR', 'TRABAJADOR');
DELETE FROM dbo.Rol WHERE NegocioId IS NULL AND Codigo IN ('COORDINADOR', 'TRABAJADOR');
GO

PRINT 'OK 055: roles flexibles por negocio (ADMIN/PROPIETARIO de sistema; Coordinador/Trabajador ahora por negocio y editables).';
GO

