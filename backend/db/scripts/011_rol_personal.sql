-- ----------------------------------------------------------------
-- Catalogo de roles/cargos de personal (ej: Lavandero, Cajero,
-- Supervisor) - distinto de los roles de acceso al sistema (Rol)
-- ----------------------------------------------------------------
IF OBJECT_ID('dbo.RolPersonal', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RolPersonal (
        Id      INT IDENTITY(1,1) PRIMARY KEY,
        Nombre  NVARCHAR(60) NOT NULL,
        Activo  BIT NOT NULL DEFAULT 1
    );
END
GO
