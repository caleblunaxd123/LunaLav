-- ============================================================
-- Matriz de permisos por rol y módulo (accesos del sistema)
-- ============================================================
USE LunaLav;
GO

IF OBJECT_ID('dbo.RolPermiso', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RolPermiso (
        Id            INT IDENTITY(1,1) PRIMARY KEY,
        RolId         INT NOT NULL FOREIGN KEY REFERENCES dbo.Rol(Id),
        Modulo        NVARCHAR(40) NOT NULL,
        PuedeAcceder  BIT NOT NULL DEFAULT 0,
        CONSTRAINT UQ_RolPermiso_RolModulo UNIQUE (RolId, Modulo)
    );
END
GO

-- Semilla de permisos por defecto (ADMIN siempre tiene acceso total a nivel de código,
-- pero igual se registra aquí para que la matriz se vea completa en la UI de Ajustes).
DECLARE @Modulos TABLE (Modulo NVARCHAR(40));
INSERT INTO @Modulos VALUES ('INICIO'), ('PEDIDOS'), ('REGISTRAR'), ('CAJA'), ('CLIENTES'), ('PROMOCIONES'), ('REPORTES'), ('AJUSTES');

INSERT INTO dbo.RolPermiso (RolId, Modulo, PuedeAcceder)
SELECT r.Id, m.Modulo,
    CASE
        WHEN r.Codigo = 'ADMIN' THEN 1
        WHEN r.Codigo = 'COORDINADOR' AND m.Modulo IN ('INICIO','PEDIDOS','REGISTRAR','CAJA','CLIENTES','REPORTES') THEN 1
        WHEN r.Codigo = 'TRABAJADOR' AND m.Modulo IN ('INICIO','PEDIDOS','REGISTRAR') THEN 1
        ELSE 0
    END
FROM dbo.Rol r
CROSS JOIN @Modulos m
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.RolPermiso rp WHERE rp.RolId = r.Id AND rp.Modulo = m.Modulo
);
GO

