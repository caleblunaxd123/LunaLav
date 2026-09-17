USE LunaLav;
GO

IF OBJECT_ID(N'dbo.InteresadoDemo', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InteresadoDemo (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Nombre NVARCHAR(120) NOT NULL,
        Negocio NVARCHAR(120) NOT NULL,
        Celular NVARCHAR(25) NOT NULL,
        Email NVARCHAR(150) NULL,
        PlanInteres NVARCHAR(20) NOT NULL,
        Consentimiento BIT NOT NULL,
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_InteresadoDemo_Fecha DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_InteresadoDemo_Fecha ON dbo.InteresadoDemo(FechaCreacion DESC);
END
GO
