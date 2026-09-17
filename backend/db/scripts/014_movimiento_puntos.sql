-- Historial manual de puntos de fidelizacion por cliente (ficha 360 del cliente)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'MovimientoPuntos')
BEGIN
    CREATE TABLE dbo.MovimientoPuntos (
        Id INT IDENTITY PRIMARY KEY,
        ClienteId INT NOT NULL FOREIGN KEY REFERENCES dbo.Cliente(Id),
        Fecha DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        Motivo NVARCHAR(200) NOT NULL,
        Puntos INT NOT NULL,
        Tipo NVARCHAR(10) NOT NULL, -- SUMA | RESTA
        UsuarioId INT NULL FOREIGN KEY REFERENCES dbo.Usuario(Id)
    );
    CREATE INDEX IX_MovimientoPuntos_Cliente ON dbo.MovimientoPuntos(ClienteId, Fecha DESC);
END
GO
