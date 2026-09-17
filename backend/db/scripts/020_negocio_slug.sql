-- ============================================================
-- 020: Slug de URL por Negocio (SaaS multi-empresa: /:empresaSlug/...)
--
-- Cada Negocio recibe un slug unico usado en el frontend para identificar la
-- empresa por la URL (ej. /mi-lavanderia/login) antes de que exista una sesion.
--
-- Es re-ejecutable: cada paso valida si ya se aplico antes de tocar nada.
-- ============================================================
USE LunaLav;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Negocio') AND name = 'Slug')
    ALTER TABLE dbo.Negocio ADD Slug NVARCHAR(50) NULL;
GO

-- Backfill del tenant demostrativo (solo si aun no tiene slug)
UPDATE dbo.Negocio SET Slug = 'demo' WHERE Nombre = N'Lavandería Demo LunaLav' AND Slug IS NULL;
GO

-- Red de seguridad: cualquier negocio que quede sin slug (futuro/manual) recibe uno generico
UPDATE dbo.Negocio SET Slug = 'negocio-' + CAST(Id AS NVARCHAR(10)) WHERE Slug IS NULL;
GO

ALTER TABLE dbo.Negocio ALTER COLUMN Slug NVARCHAR(50) NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Negocio_Slug' AND object_id = OBJECT_ID('dbo.Negocio'))
    CREATE UNIQUE INDEX UX_Negocio_Slug ON dbo.Negocio(Slug);
GO

