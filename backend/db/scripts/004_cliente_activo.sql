-- ============================================================
-- Agrega campo Activo a Cliente para soft-delete
-- ============================================================
USE LunaLav;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = 'Activo' AND Object_ID = Object_ID('dbo.Cliente')
)
BEGIN
    ALTER TABLE dbo.Cliente ADD Activo BIT NOT NULL DEFAULT 1;
END
GO

