-- ============================================================
-- Agrega marca de prioridad urgente (con recargo) al Pedido
-- ============================================================
USE LunaLav;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = 'EsUrgente' AND Object_ID = Object_ID('dbo.Pedido')
)
BEGIN
    ALTER TABLE dbo.Pedido ADD EsUrgente BIT NOT NULL DEFAULT 0;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = 'RecargoUrgente' AND Object_ID = Object_ID('dbo.Pedido')
)
BEGIN
    ALTER TABLE dbo.Pedido ADD RecargoUrgente DECIMAL(10,2) NOT NULL DEFAULT 0;
END
GO

