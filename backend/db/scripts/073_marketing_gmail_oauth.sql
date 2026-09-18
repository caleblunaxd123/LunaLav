USE LunaLav;
GO
IF OBJECT_ID(N'communication.OAuthState',N'U') IS NULL CREATE TABLE communication.OAuthState(
 Id BIGINT IDENTITY PRIMARY KEY,
 Provider NVARCHAR(40) NOT NULL,
 State NVARCHAR(180) NOT NULL UNIQUE,
 RequestedBy NVARCHAR(120) NOT NULL,
 ExpiresAt DATETIME2 NOT NULL,
 UsedAt DATETIME2 NULL,
 CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_CommunicationOAuthState_Valid')
 CREATE INDEX IX_CommunicationOAuthState_Valid ON communication.OAuthState(Provider,State,ExpiresAt,UsedAt);
GO
