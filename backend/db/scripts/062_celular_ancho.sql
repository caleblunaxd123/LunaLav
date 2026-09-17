-- 062_celular_ancho.sql
-- Amplía las columnas de celular para admitir números extranjeros de cualquier longitud
-- (con "+" y código de país), no solo los 9 dígitos de Perú. E.164 llega a 15 dígitos;
-- con el "+" y holgura usamos NVARCHAR(30).

-- Cliente.Celular tiene índices dependientes: se bajan, se amplía la columna y se recrean.
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Cliente') AND name = 'Celular' AND max_length < 60)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Cliente_Celular' AND object_id = OBJECT_ID('dbo.Cliente'))
        DROP INDEX IX_Cliente_Celular ON dbo.Cliente;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Cliente_Negocio_Nombre_Celular' AND object_id = OBJECT_ID('dbo.Cliente'))
        DROP INDEX UX_Cliente_Negocio_Nombre_Celular ON dbo.Cliente;

    ALTER TABLE dbo.Cliente ALTER COLUMN Celular NVARCHAR(30) NULL;

    CREATE INDEX IX_Cliente_Celular ON dbo.Cliente(Celular);
    CREATE UNIQUE INDEX UX_Cliente_Negocio_Nombre_Celular
        ON dbo.Cliente (NegocioId, Nombre, Celular)
        WHERE Celular IS NOT NULL AND Activo = 1;
END

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Empleado') AND name = 'Celular' AND max_length < 60)
    ALTER TABLE dbo.Empleado ALTER COLUMN Celular NVARCHAR(30) NULL;

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Motorizado') AND name = 'Celular' AND max_length < 60)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Motorizado_Sede_Celular' AND object_id = OBJECT_ID('dbo.Motorizado'))
        DROP INDEX UX_Motorizado_Sede_Celular ON dbo.Motorizado;

    ALTER TABLE dbo.Motorizado ALTER COLUMN Celular NVARCHAR(30) NULL;

    CREATE UNIQUE INDEX UX_Motorizado_Sede_Celular
        ON dbo.Motorizado (SedeId, Celular) WHERE Celular IS NOT NULL AND Activo = 1;
END

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Negocio') AND name = 'TitularCelular' AND max_length < 60)
    ALTER TABLE dbo.Negocio ALTER COLUMN TitularCelular NVARCHAR(30) NULL;
GO
