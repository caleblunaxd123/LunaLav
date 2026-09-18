USE LunaLav;
GO

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'marketing') EXEC(N'CREATE SCHEMA marketing');
GO

IF OBJECT_ID(N'marketing.[User]', N'U') IS NULL
BEGIN
    CREATE TABLE marketing.[User] (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MarketingUser PRIMARY KEY,
        UsuarioLogin NVARCHAR(80) NOT NULL,
        NombreCompleto NVARCHAR(160) NOT NULL,
        Email NVARCHAR(180) NULL,
        PasswordHash NVARCHAR(255) NOT NULL,
        RolCodigo NVARCHAR(30) NOT NULL CONSTRAINT CK_MarketingUser_Rol CHECK (RolCodigo IN (N'ADMINISTRADOR', N'MARKETING', N'SUPERVISOR')),
        Activo BIT NOT NULL CONSTRAINT DF_MarketingUser_Activo DEFAULT 1,
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_MarketingUser_Creacion DEFAULT SYSUTCDATETIME(),
        UltimoAcceso DATETIME2 NULL,
        CONSTRAINT UQ_MarketingUser_Login UNIQUE (UsuarioLogin)
    );
    CREATE UNIQUE INDEX UX_MarketingUser_Email ON marketing.[User](Email) WHERE Email IS NOT NULL;
END
GO

IF OBJECT_ID(N'marketing.RefreshToken', N'U') IS NULL
BEGIN
    CREATE TABLE marketing.RefreshToken (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MarketingRefreshToken PRIMARY KEY,
        UserId INT NOT NULL CONSTRAINT FK_MarketingRefreshToken_User REFERENCES marketing.[User](Id),
        TokenHash CHAR(64) NOT NULL,
        FechaExpiracion DATETIME2 NOT NULL,
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_MarketingRefreshToken_Creacion DEFAULT SYSUTCDATETIME(),
        Revocado BIT NOT NULL CONSTRAINT DF_MarketingRefreshToken_Revocado DEFAULT 0,
        CONSTRAINT UQ_MarketingRefreshToken_Hash UNIQUE(TokenHash)
    );
    CREATE INDEX IX_MarketingRefreshToken_User ON marketing.RefreshToken(UserId, FechaExpiracion DESC);
END
GO

IF OBJECT_ID(N'marketing.Tag', N'U') IS NULL
BEGIN
    CREATE TABLE marketing.Tag (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MarketingTag PRIMARY KEY,
        Nombre NVARCHAR(60) NOT NULL,
        Color NVARCHAR(12) NOT NULL CONSTRAINT DF_MarketingTag_Color DEFAULT N'#2563eb',
        Activo BIT NOT NULL CONSTRAINT DF_MarketingTag_Activo DEFAULT 1,
        CONSTRAINT UQ_MarketingTag_Nombre UNIQUE(Nombre)
    );
END
GO

IF OBJECT_ID(N'marketing.Prospect', N'U') IS NULL
BEGIN
    CREATE TABLE marketing.Prospect (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MarketingProspect PRIMARY KEY,
        NombreComercial NVARCHAR(180) NOT NULL,
        RazonSocial NVARCHAR(180) NULL, Ruc NVARCHAR(16) NULL,
        Telefono NVARCHAR(40) NULL, Whatsapp NVARCHAR(40) NULL, Email NVARCHAR(180) NULL,
        SitioWeb NVARCHAR(300) NULL, Instagram NVARCHAR(160) NULL, Facebook NVARCHAR(220) NULL, TikTok NVARCHAR(180) NULL,
        Direccion NVARCHAR(300) NULL, Distrito NVARCHAR(100) NULL, Provincia NVARCHAR(100) NULL,
        Departamento NVARCHAR(100) NULL, Pais NVARCHAR(80) NOT NULL CONSTRAINT DF_MarketingProspect_Pais DEFAULT N'Perú',
        Latitud DECIMAL(9,6) NULL, Longitud DECIMAL(9,6) NULL,
        TipoNegocio NVARCHAR(80) NULL, NumeroSedesEstimado INT NULL, TieneDelivery BIT NOT NULL CONSTRAINT DF_MarketingProspect_Delivery DEFAULT 0,
        Rating DECIMAL(2,1) NULL, NumeroResenas INT NULL,
        Estado NVARCHAR(30) NOT NULL CONSTRAINT DF_MarketingProspect_Estado DEFAULT N'NUEVO',
        Prioridad NVARCHAR(15) NOT NULL CONSTRAINT DF_MarketingProspect_Prioridad DEFAULT N'MEDIA',
        Score INT NOT NULL CONSTRAINT DF_MarketingProspect_Score DEFAULT 0,
        Fuente NVARCHAR(80) NOT NULL CONSTRAINT DF_MarketingProspect_Fuente DEFAULT N'MANUAL',
        FormaTrabajoActual NVARCHAR(40) NOT NULL CONSTRAINT DF_MarketingProspect_FormaTrabajo DEFAULT N'DESCONOCIDO',
        SoftwareActual NVARCHAR(120) NULL, Observaciones NVARCHAR(2000) NULL,
        ResponsableId INT NULL CONSTRAINT FK_MarketingProspect_Responsable REFERENCES marketing.[User](Id),
        FechaDescubierto DATETIME2 NOT NULL CONSTRAINT DF_MarketingProspect_Descubierto DEFAULT SYSUTCDATETIME(),
        FechaUltimoContacto DATETIME2 NULL, FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_MarketingProspect_Creacion DEFAULT SYSUTCDATETIME(),
        FechaActualizacion DATETIME2 NOT NULL CONSTRAINT DF_MarketingProspect_Actualizacion DEFAULT SYSUTCDATETIME(),
        Activo BIT NOT NULL CONSTRAINT DF_MarketingProspect_Activo DEFAULT 1,
        CONSTRAINT CK_MarketingProspect_Estado CHECK (Estado IN (N'NUEVO',N'INVESTIGAR',N'LISTO_CONTACTAR',N'CONTACTADO',N'RESPONDIO',N'INTERESADO',N'DEMO_PROGRAMADA',N'DEMO_REALIZADA',N'NEGOCIACION',N'GANADO',N'PERDIDO',N'NO_INTERESADO',N'NO_RESPONDE',N'RECONTACTAR',N'DUPLICADO')),
        CONSTRAINT CK_MarketingProspect_Score CHECK (Score BETWEEN 0 AND 100)
    );
    CREATE INDEX IX_MarketingProspect_Listado ON marketing.Prospect(Activo, Estado, Distrito, Score DESC, FechaCreacion DESC);
    CREATE INDEX IX_MarketingProspect_Responsable ON marketing.Prospect(ResponsableId, Estado, FechaUltimoContacto);
    CREATE INDEX IX_MarketingProspect_Telefono ON marketing.Prospect(Telefono) WHERE Telefono IS NOT NULL;
END
GO

IF OBJECT_ID(N'marketing.ProspectTag', N'U') IS NULL
BEGIN
    CREATE TABLE marketing.ProspectTag (
        ProspectId BIGINT NOT NULL CONSTRAINT FK_MarketingProspectTag_Prospect REFERENCES marketing.Prospect(Id),
        TagId INT NOT NULL CONSTRAINT FK_MarketingProspectTag_Tag REFERENCES marketing.Tag(Id),
        CONSTRAINT PK_MarketingProspectTag PRIMARY KEY (ProspectId, TagId)
    );
END
GO

IF OBJECT_ID(N'marketing.Activity', N'U') IS NULL
BEGIN
    CREATE TABLE marketing.Activity (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MarketingActivity PRIMARY KEY,
        ProspectId BIGINT NOT NULL CONSTRAINT FK_MarketingActivity_Prospect REFERENCES marketing.Prospect(Id),
        UserId INT NULL CONSTRAINT FK_MarketingActivity_User REFERENCES marketing.[User](Id),
        Tipo NVARCHAR(30) NOT NULL, Canal NVARCHAR(30) NULL, Titulo NVARCHAR(180) NOT NULL,
        Detalle NVARCHAR(2000) NULL, Fecha DATETIME2 NOT NULL CONSTRAINT DF_MarketingActivity_Fecha DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_MarketingActivity_Tipo CHECK (Tipo IN (N'LLAMADA',N'WHATSAPP',N'EMAIL',N'INSTAGRAM',N'FACEBOOK',N'REUNION',N'DEMO',N'NOTA',N'CAMBIO_ESTADO',N'SEGUIMIENTO',N'PROPUESTA',N'COMENTARIO'))
    );
    CREATE INDEX IX_MarketingActivity_ProspectFecha ON marketing.Activity(ProspectId, Fecha DESC);
END
GO

IF OBJECT_ID(N'marketing.FollowUp', N'U') IS NULL
BEGIN
    CREATE TABLE marketing.FollowUp (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MarketingFollowUp PRIMARY KEY,
        ProspectId BIGINT NOT NULL CONSTRAINT FK_MarketingFollowUp_Prospect REFERENCES marketing.Prospect(Id),
        ResponsableId INT NULL CONSTRAINT FK_MarketingFollowUp_User REFERENCES marketing.[User](Id),
        Tipo NVARCHAR(30) NOT NULL, Prioridad NVARCHAR(15) NOT NULL CONSTRAINT DF_MarketingFollowUp_Prioridad DEFAULT N'MEDIA',
        Descripcion NVARCHAR(500) NOT NULL, FechaProgramada DATETIME2 NOT NULL, Completado BIT NOT NULL CONSTRAINT DF_MarketingFollowUp_Completado DEFAULT 0,
        FechaCompletado DATETIME2 NULL, FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_MarketingFollowUp_Creacion DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_MarketingFollowUp_Cola ON marketing.FollowUp(Completado, FechaProgramada, ResponsableId);
END
GO

IF OBJECT_ID(N'marketing.Task', N'U') IS NULL
BEGIN
    CREATE TABLE marketing.Task (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MarketingTask PRIMARY KEY,
        ProspectId BIGINT NULL CONSTRAINT FK_MarketingTask_Prospect REFERENCES marketing.Prospect(Id),
        ResponsableId INT NULL CONSTRAINT FK_MarketingTask_User REFERENCES marketing.[User](Id),
        Titulo NVARCHAR(180) NOT NULL, Prioridad NVARCHAR(15) NOT NULL CONSTRAINT DF_MarketingTask_Prioridad DEFAULT N'MEDIA',
        FechaVencimiento DATETIME2 NULL, Estado NVARCHAR(20) NOT NULL CONSTRAINT DF_MarketingTask_Estado DEFAULT N'PENDIENTE',
        Notas NVARCHAR(1000) NULL, FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_MarketingTask_Creacion DEFAULT SYSUTCDATETIME(), FechaCompletado DATETIME2 NULL
    );
    CREATE INDEX IX_MarketingTask_Cola ON marketing.Task(Estado, FechaVencimiento, ResponsableId);
END
GO

IF OBJECT_ID(N'marketing.Audit', N'U') IS NULL
BEGIN
    CREATE TABLE marketing.Audit (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MarketingAudit PRIMARY KEY,
        UserId INT NULL, Accion NVARCHAR(80) NOT NULL, Entidad NVARCHAR(80) NOT NULL, EntidadId NVARCHAR(80) NULL,
        Detalle NVARCHAR(1000) NULL, Ip NVARCHAR(64) NULL, Fecha DATETIME2 NOT NULL CONSTRAINT DF_MarketingAudit_Fecha DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_MarketingAudit_Entidad ON marketing.Audit(Entidad, EntidadId, Fecha DESC);
END
GO
