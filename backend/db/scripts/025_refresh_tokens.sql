-- ============================================================
-- 025: Refresh tokens + revocacion de sesion
--
-- Hasta ahora el sistema solo emitia un access token (JWT) de larga duracion sin forma de
-- invalidarlo antes de su expiracion natural: un logout no revocaba nada del lado del
-- servidor, y un token filtrado (XSS, dispositivo compartido) seguia siendo valido por horas.
--
-- Con esto el access token pasa a durar poco (ver Jwt:AccessTokenMinutes en appsettings) y el
-- cliente lo renueva en silencio contra /api/auth/refresh usando este refresh token, que si se
-- puede revocar server-side (logout real, o "cerrar sesion en todos lados").
--
-- Se guarda el HASH (SHA-256) del refresh token, nunca el valor real: si alguien copia la base
-- de datos no puede usarlo para autenticarse, solo el cliente que tiene el token original puede.
--
-- Es re-ejecutable: valida si ya existe antes de crear.
-- ============================================================
USE LunaLav;
GO

IF OBJECT_ID('dbo.RefreshToken', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RefreshToken (
        Id              INT IDENTITY(1,1) PRIMARY KEY,
        UsuarioId       INT NOT NULL FOREIGN KEY REFERENCES dbo.Usuario(Id),
        TokenHash       CHAR(64) NOT NULL,  -- SHA-256 en hex
        FechaCreacion   DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        FechaExpiracion DATETIME2 NOT NULL,
        Revocado        BIT NOT NULL DEFAULT 0,
        FechaRevocado   DATETIME2 NULL
    );
    CREATE UNIQUE INDEX UX_RefreshToken_Hash ON dbo.RefreshToken(TokenHash);
    CREATE INDEX IX_RefreshToken_UsuarioId ON dbo.RefreshToken(UsuarioId);
END
GO

