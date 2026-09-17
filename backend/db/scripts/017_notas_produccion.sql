-- Notas fijas para el ticket "Producción" (instrucciones al operario)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.ConfiguracionNegocio') AND name = 'NotasProduccion'
)
    ALTER TABLE dbo.ConfiguracionNegocio ADD NotasProduccion NVARCHAR(500) NULL;
GO
