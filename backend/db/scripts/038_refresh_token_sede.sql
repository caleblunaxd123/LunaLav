USE LunaLav;
GO

-- Conserva la sede activa de cada sesion al rotar el refresh token. Un administrador
-- multi-sede mantiene Usuario.SedeId en NULL, por lo que este contexto no debe persistirse
-- sobre el usuario ni perderse al renovar el access token.
IF COL_LENGTH('dbo.RefreshToken', 'SedeId') IS NULL
BEGIN
    ALTER TABLE dbo.RefreshToken ADD SedeId INT NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = 'FK_RefreshToken_Sede'
      AND parent_object_id = OBJECT_ID('dbo.RefreshToken')
)
BEGIN
    ALTER TABLE dbo.RefreshToken WITH CHECK
        ADD CONSTRAINT FK_RefreshToken_Sede
        FOREIGN KEY (SedeId) REFERENCES dbo.Sede(Id);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_RefreshToken_SedeId'
      AND object_id = OBJECT_ID('dbo.RefreshToken')
)
BEGIN
    CREATE INDEX IX_RefreshToken_SedeId ON dbo.RefreshToken(SedeId);
END
GO

