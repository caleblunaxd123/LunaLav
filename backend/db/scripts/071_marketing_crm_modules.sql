/* Módulos operativos del CRM LunaLav Marketing. No habilita envíos externos. */
IF OBJECT_ID(N'marketing.Campaign', N'U') IS NULL
BEGIN
    CREATE TABLE marketing.Campaign (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MarketingCampaign PRIMARY KEY,
        Nombre NVARCHAR(160) NOT NULL, Audiencia NVARCHAR(200) NOT NULL,
        Canal NVARCHAR(30) NOT NULL CONSTRAINT DF_MarketingCampaign_Canal DEFAULT N'EMAIL',
        Asunto NVARCHAR(200) NULL, Mensaje NVARCHAR(MAX) NULL,
        Estado NVARCHAR(20) NOT NULL CONSTRAINT DF_MarketingCampaign_Estado DEFAULT N'BORRADOR',
        FechaProgramada DATETIME2 NULL, CreadoPorId INT NULL CONSTRAINT FK_MarketingCampaign_User REFERENCES marketing.[User](Id),
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_MarketingCampaign_Creacion DEFAULT SYSUTCDATETIME(),
        FechaActualizacion DATETIME2 NOT NULL CONSTRAINT DF_MarketingCampaign_Actualizacion DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_MarketingCampaign_Estado CHECK(Estado IN(N'BORRADOR',N'PENDIENTE_APROBACION',N'APROBADA',N'PAUSADA',N'FINALIZADA'))
    );
    CREATE INDEX IX_MarketingCampaign_Estado ON marketing.Campaign(Estado, FechaProgramada DESC);
END
GO

IF OBJECT_ID(N'marketing.MessageDraft', N'U') IS NULL
BEGIN
    CREATE TABLE marketing.MessageDraft (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MarketingMessageDraft PRIMARY KEY,
        ProspectId BIGINT NULL CONSTRAINT FK_MarketingMessageDraft_Prospect REFERENCES marketing.Prospect(Id),
        Canal NVARCHAR(30) NOT NULL CONSTRAINT DF_MarketingMessageDraft_Canal DEFAULT N'EMAIL',
        Destinatario NVARCHAR(200) NOT NULL, Asunto NVARCHAR(200) NULL, Cuerpo NVARCHAR(MAX) NOT NULL,
        Estado NVARCHAR(20) NOT NULL CONSTRAINT DF_MarketingMessageDraft_Estado DEFAULT N'BORRADOR',
        CreadoPorId INT NULL CONSTRAINT FK_MarketingMessageDraft_User REFERENCES marketing.[User](Id),
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_MarketingMessageDraft_Creacion DEFAULT SYSUTCDATETIME(),
        FechaActualizacion DATETIME2 NOT NULL CONSTRAINT DF_MarketingMessageDraft_Actualizacion DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_MarketingMessageDraft_Estado CHECK(Estado IN(N'BORRADOR',N'PENDIENTE_APROBACION',N'APROBADO',N'ARCHIVADO'))
    );
    CREATE INDEX IX_MarketingMessageDraft_Estado ON marketing.MessageDraft(Estado, FechaCreacion DESC);
END
GO

IF OBJECT_ID(N'marketing.AgentSetting', N'U') IS NULL
BEGIN
    CREATE TABLE marketing.AgentSetting (
        Id INT NOT NULL CONSTRAINT PK_MarketingAgentSetting PRIMARY KEY CONSTRAINT CK_MarketingAgentSetting_One CHECK(Id=1),
        Nombre NVARCHAR(80) NOT NULL CONSTRAINT DF_MarketingAgentSetting_Nombre DEFAULT N'Alex',
        AnalizarProspectos BIT NOT NULL CONSTRAINT DF_MarketingAgentSetting_Analizar DEFAULT 1,
        GenerarBorradores BIT NOT NULL CONSTRAINT DF_MarketingAgentSetting_Borradores DEFAULT 1,
        CrearSeguimientos BIT NOT NULL CONSTRAINT DF_MarketingAgentSetting_Seguimientos DEFAULT 1,
        PrimerContactoAutomatico BIT NOT NULL CONSTRAINT DF_MarketingAgentSetting_PrimerContacto DEFAULT 0,
        SeguimientosAutomaticos BIT NOT NULL CONSTRAINT DF_MarketingAgentSetting_AutoFollow DEFAULT 0,
        LimiteDiario INT NOT NULL CONSTRAINT DF_MarketingAgentSetting_Limite DEFAULT 10,
        FechaActualizacion DATETIME2 NOT NULL CONSTRAINT DF_MarketingAgentSetting_Actualizacion DEFAULT SYSUTCDATETIME()
    );
    INSERT marketing.AgentSetting(Id) VALUES(1);
END
GO
