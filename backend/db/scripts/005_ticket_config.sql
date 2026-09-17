-- ============================================================
-- Agrega configuracion del ticket a ConfiguracionNegocio
-- ============================================================
USE LunaLav;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = 'AnchoTicketMm' AND Object_ID = Object_ID('dbo.ConfiguracionNegocio')
)
BEGIN
    ALTER TABLE dbo.ConfiguracionNegocio
        ADD AnchoTicketMm INT NOT NULL DEFAULT 80;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = 'MensajePieTicket' AND Object_ID = Object_ID('dbo.ConfiguracionNegocio')
)
BEGIN
    ALTER TABLE dbo.ConfiguracionNegocio
        ADD MensajePieTicket NVARCHAR(300) NULL;
END
GO

-- Actualiza el default value si esta vacio
UPDATE dbo.ConfiguracionNegocio
   SET MensajePieTicket = N'Gracias por su preferencia. Presente este ticket al recoger su pedido.'
 WHERE MensajePieTicket IS NULL OR MensajePieTicket = '';
GO

