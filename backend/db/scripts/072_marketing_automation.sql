/* Cola y configuración del agente comercial. No autoriza envíos externos. */
IF OBJECT_ID(N'marketing.AutomationRule', N'U') IS NULL
BEGIN
    CREATE TABLE marketing.AutomationRule(
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_MarketingAutomationRule PRIMARY KEY,
        Nombre NVARCHAR(120) NOT NULL CONSTRAINT UQ_MarketingAutomationRule_Nombre UNIQUE,
        JobType NVARCHAR(120) NOT NULL, FrecuenciaMinutos INT NOT NULL,
        Activa BIT NOT NULL CONSTRAINT DF_MarketingAutomationRule_Activa DEFAULT 0,
        RequiereAprobacion BIT NOT NULL CONSTRAINT DF_MarketingAutomationRule_Aprobacion DEFAULT 1,
        UltimaEjecucion DATETIME2 NULL, FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_MarketingAutomationRule_Creacion DEFAULT SYSUTCDATETIME()
    );
    INSERT marketing.AutomationRule(Nombre,JobType,FrecuenciaMinutos,Activa,RequiereAprobacion) VALUES
      (N'Recalcular score de prospectos',N'MARKETING_SCORE_RECALCULATION',1440,1,0),
      (N'Revisar seguimientos vencidos',N'MARKETING_FOLLOWUP_REVIEW',60,1,0),
      (N'Preparar contactos iniciales',N'MARKETING_PREPARE_OUTREACH',60,0,1);
END
GO
IF NOT EXISTS(SELECT 1 FROM agents.AgentDefinition WHERE Nombre=N'Alex' AND Departamento=N'MARKETING')
    INSERT agents.AgentDefinition(Nombre,Departamento,Descripcion,Autonomia,Activo) VALUES(N'Alex',N'MARKETING',N'Asistente comercial de LunaLav: prioriza prospectos, prepara borradores y tareas; no envía contactos sin aprobación.',1,1);
GO
