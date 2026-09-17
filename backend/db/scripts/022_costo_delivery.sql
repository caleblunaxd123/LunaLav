-- Cargo de delivery: monto fijo configurable por negocio que se agrega automaticamente
-- al carrito de Registrar cuando la modalidad es Delivery (ver Ajustes > Negocio).

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.ConfiguracionNegocio') AND name = 'CostoDelivery'
)
    ALTER TABLE dbo.ConfiguracionNegocio ADD CostoDelivery DECIMAL(10,2) NOT NULL DEFAULT 0;
GO

-- Fila de "servicio" de sistema que ancla el cargo de delivery a un ServicioId real
-- (PedidoItem.ServicioId es FK obligatoria a dbo.Servicio). Se oculta del catalogo normal
-- de Registrar/Ajustes > Servicios filtrando por EsCargoDelivery = 0 en esos listados.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Servicio') AND name = 'EsCargoDelivery'
)
    ALTER TABLE dbo.Servicio ADD EsCargoDelivery BIT NOT NULL DEFAULT 0;
GO

-- Backfill: asegurar 1 servicio de sistema "Servicio a Domicilio" por cada negocio existente
-- que aun no tenga uno (snapshot en tabla variable para no auto-excluirse tras el primer INSERT).
DECLARE @NegociosSinCargoDelivery TABLE (NegocioId INT);
INSERT INTO @NegociosSinCargoDelivery (NegocioId)
SELECT n.Id
FROM dbo.Negocio n
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Servicio s WHERE s.NegocioId = n.Id AND s.EsCargoDelivery = 1
);

INSERT INTO dbo.Servicio (NegocioId, Nombre, Precio, Unidad, CategoriaId, Activo, EsCargoDelivery)
SELECT n.NegocioId, 'Servicio a Domicilio', ISNULL(c.CostoDelivery, 0), 'Unidad', NULL, 1, 1
FROM @NegociosSinCargoDelivery n
LEFT JOIN dbo.ConfiguracionNegocio c ON c.NegocioId = n.NegocioId;
GO
