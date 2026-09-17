-- Agrega el texto de condiciones del servicio, mostrado en el ticket tipo "Cliente"
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.ConfiguracionNegocio') AND name = 'CondicionesServicio'
)
    ALTER TABLE dbo.ConfiguracionNegocio ADD CondicionesServicio NVARCHAR(MAX) NULL;
GO

UPDATE dbo.ConfiguracionNegocio
SET CondicionesServicio = N'Entrega solo con boleta. No se entregan prendas sin ella.
Después de 45 días se cobra 20% por almacenamiento.
Prendas no retiradas en 90 días serán donadas o rematadas.
No nos responsabilizamos por daños en prendas frágiles, muy usadas o de mala confección.
No aceptamos ropa interior; no nos responsabilizamos si es enviada.
Mascarillas serán desechadas sin derecho a reclamo.
No garantizamos eliminación total de manchas difíciles.
En casos fortuitos comprobados, no hay responsabilidad por prendas fuera de plazo.'
WHERE CondicionesServicio IS NULL;
GO
