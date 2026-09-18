USE LunaLav;
GO

-- Cola comercial: no envía mensajes por sí sola. Mantiene el consentimiento y la
-- aprobación humana antes de que un proveedor autorizado pueda entregar un mensaje.
IF COL_LENGTH(N'dbo.InteresadoDemo', N'EstadoSeguimiento') IS NULL
BEGIN
    ALTER TABLE dbo.InteresadoDemo ADD
        EstadoSeguimiento NVARCHAR(30) NOT NULL CONSTRAINT DF_InteresadoDemo_Estado DEFAULT N'NUEVO',
        NotaSeguimiento NVARCHAR(600) NULL,
        MensajeAprobado NVARCHAR(2000) NULL,
        AprobadoPor NVARCHAR(120) NULL,
        FechaAprobacion DATETIME2 NULL,
        FechaUltimoSeguimiento DATETIME2 NULL;
    CREATE INDEX IX_InteresadoDemo_EstadoFecha ON dbo.InteresadoDemo(EstadoSeguimiento, FechaCreacion DESC);
END
GO
