-- Repara datos heredados y protege las invariantes del flujo de pedidos.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Un pedido en proceso siempre debe conservar la etapa en la que se encuentra.
UPDATE p
   SET AreaActualId = COALESCE(hist.AreaId, primera.AreaId)
  FROM dbo.Pedido p
 OUTER APPLY (
       SELECT TOP (1) h.AreaId
         FROM dbo.PedidoHistorial h
         JOIN dbo.AreaLavado a ON a.Id = h.AreaId AND a.SedeId = p.SedeId AND a.Activa = 1
        WHERE h.PedidoId = p.Id AND h.AreaId IS NOT NULL
        ORDER BY h.Fecha DESC, h.Id DESC
 ) hist
 OUTER APPLY (
       SELECT TOP (1) a.Id AS AreaId
         FROM dbo.AreaLavado a
        WHERE a.SedeId = p.SedeId AND a.Activa = 1
        ORDER BY a.Orden, a.Id
 ) primera
 WHERE p.EstadoProceso = 'EN_PROCESO' AND p.AreaActualId IS NULL;

-- Si la sede no tiene áreas configuradas, se mantiene pendiente en lugar de fingir un proceso.
UPDATE dbo.Pedido
   SET EstadoProceso = 'PENDIENTE'
 WHERE EstadoProceso = 'EN_PROCESO' AND AreaActualId IS NULL;

-- Las entregas heredadas deben tener una fecha real para reportes y métricas.
UPDATE p
   SET FechaEntregaReal = COALESCE(entrega.Fecha, ultimo.Fecha, p.FechaIngreso)
  FROM dbo.Pedido p
 OUTER APPLY (
       SELECT MAX(h.Fecha) AS Fecha
         FROM dbo.PedidoHistorial h
        WHERE h.PedidoId = p.Id AND h.EstadoProceso = 'ENTREGADO'
 ) entrega
 OUTER APPLY (
       SELECT MAX(h.Fecha) AS Fecha
         FROM dbo.PedidoHistorial h
        WHERE h.PedidoId = p.Id
 ) ultimo
 WHERE p.EstadoProceso = 'ENTREGADO' AND p.FechaEntregaReal IS NULL;

-- Una fecha real de entrega no puede sobrevivir a un estado no entregado.
UPDATE dbo.Pedido
   SET FechaEntregaReal = NULL
 WHERE EstadoProceso <> 'ENTREGADO' AND FechaEntregaReal IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Pedido_EnProcesoRequiereArea')
    ALTER TABLE dbo.Pedido WITH CHECK ADD CONSTRAINT CK_Pedido_EnProcesoRequiereArea
        CHECK (EstadoProceso <> 'EN_PROCESO' OR AreaActualId IS NOT NULL);

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Pedido_FechaEntregaReal')
    ALTER TABLE dbo.Pedido WITH CHECK ADD CONSTRAINT CK_Pedido_FechaEntregaReal
        CHECK ((EstadoProceso = 'ENTREGADO' AND FechaEntregaReal IS NOT NULL)
            OR (EstadoProceso <> 'ENTREGADO' AND FechaEntregaReal IS NULL));

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Pedido_EntregadoPagado')
    ALTER TABLE dbo.Pedido WITH CHECK ADD CONSTRAINT CK_Pedido_EntregadoPagado
        CHECK (EstadoProceso <> 'ENTREGADO' OR MontoPagado + 0.01 >= Total);

-- Ni siquiera una actualización SQL accidental puede sacar un pedido de un estado terminal.
EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_Pedido_EstadoTerminal
ON dbo.Pedido
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
          FROM inserted i
          JOIN deleted d ON d.Id = i.Id
         WHERE d.EstadoProceso IN (''ENTREGADO'', ''ANULADO'', ''DONADO'')
           AND i.EstadoProceso <> d.EstadoProceso
    )
        THROW 51020, ''Un pedido en estado terminal no puede volver al flujo operativo.'', 1;
END');

COMMIT TRANSACTION;
