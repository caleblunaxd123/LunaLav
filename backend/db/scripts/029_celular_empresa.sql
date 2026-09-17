-- Panel del propietario: celular de contacto del titular de cada empresa.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Negocio') AND name = 'TitularCelular')
    ALTER TABLE dbo.Negocio ADD TitularCelular NVARCHAR(20) NULL;
GO
